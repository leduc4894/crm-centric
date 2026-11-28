using System.Security.Cryptography;
using System.Text;
using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NSec.Cryptography;
using Platform.Infrastructure.Integration;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed class ManifestRegistryTests : IAsyncLifetime
{
    private const string Previous = "20261127041000_AtomicContactNote";
    private readonly PostgresFixture db = new();
    private static readonly Uri Origin = new("https://demo.example.net");
    private const string Module = "com.example.demo";
    private const string KeyId = "fixture-publisher-1";
    private NpgsqlConnection owner = null!;
    private NpgsqlDataSource app = null!;
    private PlatformDbContext context = null!;
    private ManifestRegistry registry = null!;
    private static byte[] Read(string file) => File.ReadAllBytes(Path.Combine(ContainerImages.Root, "tests/Contract/fixtures/wave3-publisher", file));

    public async Task InitializeAsync()
    {
        await db.InitializeAsync();
        owner = new(db.MigrationConnectionString);
        await owner.OpenAsync();
        context = new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(owner,
            o => o.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.GetService<IMigrator>().MigrateAsync(Previous);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(owner, db.ApplicationRole, default);
        app = NpgsqlDataSource.Create(db.ApplicationConnectionString);
        registry = new(app);
    }

    public async Task DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
        if (context is not null) await context.DisposeAsync();
        if (owner is not null) await owner.DisposeAsync();
        await db.DisposeAsync();
    }

    private static ValidatedManifest Candidate(string version, bool variant = false)
    {
        string json = Encoding.UTF8.GetString(Read("manifest.json")).Replace("\"1.0.0\"", $"\"{version}\"");
        byte[] bytes = Encoding.UTF8.GetBytes(variant ? json + " " : json);
        return ManifestValidator.Validate(bytes, new(Module, KeyId, 1, Convert.ToHexStringLower(SHA256.HashData(bytes)), "demo.", [Origin]));
    }

    private static Task<IReadOnlyList<SchemaSnapshot>> Schemas(ValidatedManifest manifest) =>
        new SchemaSnapshotLoader((uri, _, _) => Task.FromResult(new FetchResult(
            [.. File.ReadAllBytes(Path.Combine(ContainerImages.Root, "docs/contracts", Path.GetFileName(uri.AbsolutePath)))], uri)))
        .LoadAsync(manifest, new(Origin), default);

    private async Task<RegistryResult> Store(ValidatedManifest manifest) =>
        await registry.RegisterAsync(manifest, await Schemas(manifest), Guid.NewGuid(), default);

    private async Task<object?> Scalar(string sql) => await new NpgsqlCommand(sql, owner).ExecuteScalarAsync();

    [Fact]
    public async Task Concurrent_writers_preserve_winner_and_compare_versions_numerically()
    {
        var first = Candidate("1.2.0");
        var schemas = await Schemas(first);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => registry.RegisterAsync(first, schemas, Guid.NewGuid(), default)));
        Assert.Single(results, r => r == RegistryResult.Inserted);
        Assert.Equal(5, results.Count(r => r == RegistryResult.AlreadyRegistered));
        var upgrades = new[] { Candidate("1.10.0"), Candidate("1.10.0", true) };
        async Task<string> Submit(ValidatedManifest candidate)
        {
            try { return (await Store(candidate)).ToString(); }
            catch (RegistryException ex) { return ex.Reason.ToString(); }
        }
        var race = await Task.WhenAll(upgrades.Select(Submit));
        Assert.Contains("Inserted", race);
        Assert.Contains("VersionConflict", race);
        Assert.Equal("Downgrade", await Submit(first));
        Assert.Equal("Downgrade", await Submit(Candidate("1.9.0")));
        Assert.Equal(8L, await Scalar("SELECT count(*) FROM platform.manifest_schemas"));
        Assert.Equal("Inserted", await Submit(Candidate("123456789012345678901234567890.0.0")));
        Assert.Equal("Downgrade", await Submit(Candidate("99999999999999999999999999999.0.0")));
    }

    [Fact]
    public async Task Database_failure_rolls_back_all_rows_and_tampering_fails_closed()
    {
        var candidate = Candidate("1.0.0");
        var schemas = await Schemas(candidate);
        var correlation = Guid.NewGuid();
        async Task Failure(IReadOnlyList<SchemaSnapshot> input, RegistryFailure reason)
        {
            var ex = await Assert.ThrowsAsync<RegistryException>(() => registry.RegisterAsync(candidate, input, correlation, default));
            Assert.Equal(reason, ex.Reason);
            Assert.Equal(correlation, ex.CorrelationId);
            Assert.Equal(0L, await Scalar("SELECT count(*) FROM platform.manifest_candidates"));
            Assert.Equal(0L, await Scalar("SELECT count(*) FROM platform.manifest_schemas"));
        }
        await Failure(schemas.Skip(1).ToArray(), RegistryFailure.InvalidSnapshot);
        await Failure([schemas[0] with { Bytes = [1] }, .. schemas.Skip(1)], RegistryFailure.InvalidSnapshot);
        await Scalar("ALTER TABLE platform.manifest_schemas ADD CHECK (uri NOT LIKE '%demo-panel%')");
        await Failure(schemas, RegistryFailure.Storage);
    }

    [Fact]
    public async Task Revocation_before_write_preserves_historical_candidate_and_guarded_rollback()
    {
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        await migrator.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(owner, db.ApplicationRole, default);
        var key = PublicKey.Import(SignatureAlgorithm.Ed25519, Read("public.pem"), KeyBlobFormat.PkixPublicKeyText);
        var entry = new PublisherTrustEntry(Module, KeyId, [.. key.Export(KeyBlobFormat.RawPublicKey)], [Origin], PublisherKeyStatus.Active);
        var current = new PublisherTrustSnapshot(1, [entry], [new(Module, "demo.")]);
        var manifest = new FetchResult([.. Read("manifest.json")], Origin);
        var signature = new FetchResult([.. Read("manifest.json.sig.json")], Origin);
        var candidate = ManifestValidator.Validate(manifest.Bytes.AsMemory(), PublisherVerifier.Verify(manifest, signature, current));
        current = new(2, [entry with { Status = PublisherKeyStatus.Revoked }], [new(Module, "demo.")]);
        Assert.Equal(RegistryResult.Inserted, await Store(candidate));
        Assert.Throws<PublisherVerificationException>(() => PublisherVerifier.Verify(manifest, signature, current));
        Assert.Equal("verified_candidate:1", await Scalar("SELECT status || ':' || trust_revision FROM platform.manifest_candidates"));
        Assert.Equal(manifest.Bytes.ToArray(), (byte[]) (await Scalar("SELECT manifest_bytes FROM platform.manifest_candidates"))!);
        Assert.Equal(candidate.Publisher.ManifestHash, await Scalar("SELECT encode(manifest_hash,'hex') FROM platform.manifest_candidates"));
        Assert.Equal(KeyId, await Scalar("SELECT publisher_key_id FROM platform.manifest_candidates"));
        Assert.Equal(new[] { "demo.events.publish", "demo.actions.invoke", "demo.panel.read" },
            (string[])(await Scalar("SELECT proposed_scopes FROM platform.manifest_candidates"))!);
        Assert.Empty((string[])(await Scalar("SELECT requested_origins FROM platform.manifest_candidates"))!);
        var descriptorBytes = new[] { "events", "actions", "panels" }
            .SelectMany(kind => candidate.Document.GetProperty(kind).EnumerateArray())
            .Select(d => Encoding.UTF8.GetBytes(d.GetRawText())).ToArray();
        Assert.Equal(descriptorBytes, (byte[][])(await Scalar("SELECT descriptor_bytes FROM platform.manifest_candidates"))!);
        Assert.Equal(descriptorBytes.Select(b => SHA256.HashData(b)).ToArray(),
            (byte[][])(await Scalar("SELECT descriptor_hashes FROM platform.manifest_candidates"))!);
        foreach (var schema in await Schemas(candidate))
        {
            await using var read = new NpgsqlCommand("SELECT schema_bytes FROM platform.manifest_schemas WHERE uri=$1", owner);
            read.Parameters.AddWithValue(schema.Reference.Uri.AbsoluteUri);
            Assert.Equal(schema.Bytes.ToArray(), (byte[])(await read.ExecuteScalarAsync())!);
        }
        await using var login = await app.OpenConnectionAsync();
        Assert.Equal(db.ApplicationRole, await new NpgsqlCommand("SELECT current_user", login).ExecuteScalarAsync());
        foreach (string table in new[] { "manifest_candidates", "manifest_schemas" })
            foreach (string statement in new[] { $"DELETE FROM platform.{table}", $"UPDATE platform.{table} SET {(table == "manifest_schemas" ? "uri" : "module_id")}='other'",
                $"INSERT INTO platform.{table} DEFAULT VALUES", $"TRUNCATE platform.{table}" })
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(
                    () => new NpgsqlCommand(statement, login).ExecuteNonQueryAsync())).SqlState);
        await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(Previous));
        Assert.Equal(4L, await Scalar("SELECT count(*) FROM platform.manifest_schemas"));
    }
}
