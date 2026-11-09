using Npgsql;

namespace Platform.Infrastructure.Local;

public sealed record LocalBootstrapOptions(string AdminConnectionString, string DatabaseName, string ProjectId,
    string MigratorSecretFile, string ApplicationSecretFile);

public sealed class LocalSecretStateMismatchException : Exception;

/// <summary>Local-only administrative provisioning; never runs in the API or default migrator.</summary>
public static class LocalDatabaseBootstrap
{
    public static async Task RunAsync(LocalBootstrapOptions options, CancellationToken cancellationToken)
    {
        if (options.DatabaseName != "crm" || !System.Text.RegularExpressions.Regex.IsMatch(options.ProjectId, "^[a-z0-9][a-z0-9_-]{0,62}$"))
            throw new ArgumentException("Invalid local bootstrap configuration.");
        var passwords = new Dictionary<string, string>
        {
            ["crm_migrator"] = ReadSecret(options.MigratorSecretFile),
            ["crm_app"] = ReadSecret(options.ApplicationSecretFile)
        };
        var adminSettings = new NpgsqlConnectionStringBuilder(options.AdminConnectionString)
            { Database = "postgres", Pooling = false, IncludeErrorDetail = false, LogParameters = false };
        await using var admin = new NpgsqlConnection(adminSettings.ConnectionString);
        await admin.OpenAsync(cancellationToken);
        // Suppress credential-bearing SQL even when the operator enabled statement/error logging.
        // These session settings require the administrative identity; never downgrade on failure.
        await Execute(admin, "SET log_statement='none'; SET log_min_error_statement='panic'; SET log_duration=off; SET log_min_duration_sample=-1; SET log_transaction_sample_rate=0; SET log_min_duration_statement=-1; SET standard_conforming_strings=on", cancellationToken);
        await Execute(admin, "SELECT pg_advisory_lock(184903241, 4)", cancellationToken);
        try
        {
            var exists = await Scalar<bool>(admin, "SELECT to_regclass('_local.bootstrap') IS NOT NULL", cancellationToken);
            if (!exists)
            {
                if (await Scalar<bool>(admin, "SELECT EXISTS(SELECT FROM pg_database WHERE datname='crm') OR EXISTS(SELECT FROM pg_roles WHERE rolname IN ('crm_migrator','crm_app')) OR EXISTS(SELECT FROM pg_namespace WHERE nspname='_local')", cancellationToken))
                    throw new LocalSecretStateMismatchException();
                await using var transaction = await admin.BeginTransactionAsync(cancellationToken);
                await Execute(admin, "CREATE SCHEMA _local; REVOKE ALL ON SCHEMA _local FROM PUBLIC; CREATE TABLE _local.bootstrap(database_name text PRIMARY KEY, project_id text NOT NULL, complete boolean NOT NULL); REVOKE ALL ON _local.bootstrap FROM PUBLIC", cancellationToken);
                foreach (var (role, password) in passwords)
                    await Execute(admin, $"CREATE ROLE {Quote(role)} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD {Literal(password)}", cancellationToken);
                await using var insert = new NpgsqlCommand("INSERT INTO _local.bootstrap VALUES ('crm',@project,false)", admin, transaction);
                insert.Parameters.AddWithValue("project", options.ProjectId);
                await insert.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            // A pre-existing registry is only trusted when owned by this administrator.
            if (!await Scalar<bool>(admin, "SELECT n.nspowner=(SELECT oid FROM pg_roles WHERE rolname=current_user) AND c.relowner=n.nspowner FROM pg_namespace n JOIN pg_class c ON c.relnamespace=n.oid AND NOT EXISTS(SELECT FROM aclexplode(n.nspacl) a WHERE a.grantee<>n.nspowner) AND NOT EXISTS(SELECT FROM aclexplode(c.relacl) a WHERE a.grantee<>c.relowner) WHERE n.nspname='_local' AND c.relname='bootstrap'", cancellationToken))
                throw new LocalSecretStateMismatchException();
            await using var marker = new NpgsqlCommand("SELECT complete FROM _local.bootstrap WHERE database_name='crm' AND project_id=@project", admin);
            marker.Parameters.AddWithValue("project", options.ProjectId);
            var completeValue = await marker.ExecuteScalarAsync(cancellationToken);
            if (completeValue is not bool complete) throw new LocalSecretStateMismatchException();
            if (!await RolesValid(admin, cancellationToken)) throw new LocalSecretStateMismatchException();
            // A matching manifest alone is insufficient: authenticate both actual role secrets.
            foreach (var (role, password) in passwords)
                await Authenticate(adminSettings, "postgres", role, password, cancellationToken);
            var databaseExists = await Scalar<bool>(admin, "SELECT EXISTS(SELECT FROM pg_database WHERE datname='crm')", cancellationToken);
            if (!databaseExists)
            {
                if (complete) throw new LocalSecretStateMismatchException();
                await Execute(admin, "CREATE DATABASE crm", cancellationToken);
            }
            if (!await Scalar<bool>(admin, "SELECT datdba=(SELECT oid FROM pg_roles WHERE rolname=current_user) FROM pg_database WHERE datname='crm'", cancellationToken))
                throw new LocalSecretStateMismatchException();
            var targetSettings = new NpgsqlConnectionStringBuilder(adminSettings.ConnectionString) { Database = "crm" };
            await using var target = new NpgsqlConnection(targetSettings.ConnectionString);
            await target.OpenAsync(cancellationToken);
            if (!complete)
            {
                // Only the durable interrupted-provisioning marker authorizes these fixed grants.
                await using var transaction = await target.BeginTransactionAsync(cancellationToken);
                await Execute(target, "REVOKE ALL ON DATABASE crm FROM PUBLIC,crm_migrator,crm_app; REVOKE ALL ON SCHEMA public FROM PUBLIC,crm_migrator,crm_app; GRANT CONNECT,CREATE ON DATABASE crm TO crm_migrator; GRANT CONNECT ON DATABASE crm TO crm_app", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            if (!await AclValid(target, cancellationToken)) throw new LocalSecretStateMismatchException();
            foreach (var (role, password) in passwords)
                await Authenticate(adminSettings, "crm", role, password, cancellationToken);
            if (!complete) await Execute(admin, "UPDATE _local.bootstrap SET complete=true WHERE database_name='crm'", cancellationToken);
        }
        finally
        {
            // Connection close also releases the session advisory lock on any failure.
            await admin.CloseAsync();
        }
    }

    private static async Task<bool> RolesValid(NpgsqlConnection connection, CancellationToken token) => await Scalar<bool>(connection, """
        SELECT count(*)=2 AND bool_and(rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication
          AND NOT EXISTS(SELECT FROM pg_auth_members WHERE member=r.oid))
        FROM pg_roles r WHERE rolname IN ('crm_migrator','crm_app')
        """, token);

    private static async Task<bool> AclValid(NpgsqlConnection connection, CancellationToken token) => await Scalar<bool>(connection, """
        SELECT has_database_privilege('crm_migrator','crm','CONNECT') AND has_database_privilege('crm_migrator','crm','CREATE')
          AND has_database_privilege('crm_app','crm','CONNECT') AND NOT has_database_privilege('crm_app','crm','CREATE')
          AND NOT has_database_privilege('crm_app','crm','TEMPORARY') AND NOT has_database_privilege('crm_migrator','crm','TEMPORARY')
          AND NOT has_schema_privilege('crm_app','public','CREATE') AND NOT has_schema_privilege('crm_migrator','public','CREATE')
          AND NOT EXISTS(SELECT FROM pg_database d CROSS JOIN LATERAL aclexplode(d.datacl) a WHERE d.datname='crm' AND
            (a.grantee NOT IN (d.datdba,(SELECT oid FROM pg_roles WHERE rolname='crm_migrator'),(SELECT oid FROM pg_roles WHERE rolname='crm_app')) OR a.is_grantable AND a.grantee<>d.datdba))
          AND NOT EXISTS(SELECT FROM pg_namespace n CROSS JOIN LATERAL aclexplode(n.nspacl) a WHERE n.nspname='public' AND a.grantee=0)
          AND NOT EXISTS(SELECT FROM pg_namespace n WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND
            (n.nspowner=(SELECT oid FROM pg_roles WHERE rolname='crm_app') OR has_schema_privilege('crm_app',n.oid,'CREATE')))
          AND NOT EXISTS(SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='platform' AND
            (c.relowner=(SELECT oid FROM pg_roles WHERE rolname='crm_app') OR
             c.relkind IN ('r','p') AND has_table_privilege('crm_app',c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')))
        """, token);

    private static async Task Authenticate(NpgsqlConnectionStringBuilder admin, string database, string role, string password, CancellationToken token)
    {
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database, Username = role, Password = password, Pooling = false };
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync(token);
        }
        catch (NpgsqlException) { throw new LocalSecretStateMismatchException(); }
    }
    private static string ReadSecret(string path)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var file = new FileInfo(path);
        for (FileSystemInfo? entry = file; entry is not null; entry = entry is FileInfo f ? f.Directory : ((DirectoryInfo)entry).Parent)
            if (entry.LinkTarget is not null) throw new LocalSecretStateMismatchException();
        if (!file.Exists || File.GetUnixFileMode(path) != (UnixFileMode.UserRead | UnixFileMode.UserWrite)) throw new LocalSecretStateMismatchException();
        var secret = File.ReadAllText(path);
        if (secret.Length is < 32 or > 4096 || secret.Contains('\0') || secret.Contains('\n') || secret.Contains('\r')) throw new LocalSecretStateMismatchException();
        return secret;
    }
    private static string Quote(string value) => new NpgsqlCommandBuilder().QuoteIdentifier(value);
    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    private static async Task Execute(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(token);
    }
    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(token) ?? throw new LocalSecretStateMismatchException());
    }
}
