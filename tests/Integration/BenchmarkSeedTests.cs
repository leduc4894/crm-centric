using Microsoft.EntityFrameworkCore;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Integration.Fixtures;
using Xunit;

namespace Integration;

public sealed class BenchmarkSeedTests
{
    [Fact]
    public async Task Seed_is_repeatable_scoped_and_refuses_non_benchmark_database()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "CrmPlatform.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var seed = Path.Combine(root.FullName, "scripts/benchmarks/seed_contacts.sql");
        await using var db = new PostgreSqlBuilder(ContainerImages.Pins["POSTGRES_IMAGE"])
            .WithPassword(Guid.NewGuid().ToString("N"))
            .WithResourceMapping(seed, "/tmp/benchmark-seed/")
            .Build();
        await db.StartAsync();
        var dbName = "crm_bench_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(db.GetConnectionString());
        await admin.OpenAsync();
        await PostgresFixture.ExecuteAsync(admin, $"CREATE DATABASE {PostgresFixture.Quote(dbName)}");
        var builder = new NpgsqlConnectionStringBuilder(db.GetConnectionString()) { Database = dbName };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(connection,
            p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options;
        await using var context = new PlatformDbContext(options);
        await context.Database.MigrateAsync();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await using (var tenants = new NpgsqlCommand("INSERT INTO platform.tenants (id,name,status,created_at) VALUES (@a,'A','active',now()),(@b,'B','active',now())", connection))
        { tenants.Parameters.AddWithValue("a",a); tenants.Parameters.AddWithValue("b",b); await tenants.ExecuteNonQueryAsync(); }
        async Task<long> Count(string sql)
        { await using var command = new NpgsqlCommand(sql,connection); return (long)(await command.ExecuteScalarAsync())!; }
        async Task<long?> Seed(string database, string confirmation = "ISOLATED_BENCHMARK_ONLY")
        {
            var result = await db.ExecAsync(["sh", "-c", "export PGPASSWORD=\"$POSTGRES_PASSWORD\"; exec psql \"$@\"", "seed", "-U","postgres","-d",database,"-v","tenant_a="+a,"-v","tenant_b="+b,"-v","bench_confirm="+confirmation,"-v","VERBOSITY=sqlstate","-f","/tmp/benchmark-seed/seed_contacts.sql"]);
            if (database == dbName && confirmation == "ISOLATED_BENCHMARK_ONLY" && result.ExitCode != 0)
            {
                var labels = new[] { "syntax error", "permission denied", "does not exist", "BENCHMARK_DATABASE_IDENTITY_INVALID", "BENCHMARK_TENANTS_MISSING", "BENCHMARK_DATASET_MISMATCH", "BENCHMARK_MARKER_MISMATCH", "BENCHMARK_CONFIRMATION_REQUIRED", "authentication failed", "connection failed", "duplicate key", "FATAL", "No such file", "could not open", "invalid command", "could not connect", "password supplied", "psql:", "ERROR", "WARNING", "unrecognized", "invalid", "BEGIN", "INSERT", "COMMIT", "connection", "server", "socket", "failed", "role", "database", "option", "file", "password", "access", "usage", "not found", "local", "user", "root", "postgres", "directory", "certificate", "service", "host" };
                var output = result.Stdout + result.Stderr;
                var state = System.Text.RegularExpressions.Regex.Match(output, @"ERROR:\s+([A-Z0-9]{5})");
                Assert.Fail("Seed failure categories: " + string.Join(',', labels.Where(x => output.Contains(x, StringComparison.OrdinalIgnoreCase))) + "; SQLSTATE=" + (state.Success ? state.Groups[1].Value : "unknown") + "; exit=" + result.ExitCode + "; output-length=" + output.Length);
            }
            return result.ExitCode;
        }
        Assert.NotEqual(0L,await Seed("postgres"));
        Assert.NotEqual(0L,await Seed(dbName, "NO"));
        Assert.Equal(0,await Count("SELECT count(*) FROM crm.contacts"));
        Assert.Equal(0L,await Seed(dbName));
        Assert.Equal(0L,await Seed(dbName));
        Assert.Equal(50000,await Count("SELECT count(*) FROM crm.contacts WHERE email LIKE 'contact-%@benchmark.invalid'"));
        Assert.Equal(500,await Count("SELECT count(*) FROM crm.contacts WHERE display_name LIKE '%segment-07%'"));
        Assert.Equal(10,await Count("SELECT count(*) FROM crm.tags"));
        Assert.Equal(50000,await Count("SELECT count(*) FROM crm.contact_tags"));
        Assert.Equal(1,await Count("SELECT count(*) FROM crm.contacts WHERE display_name='Bench tenant B marker'"));
    }
}
