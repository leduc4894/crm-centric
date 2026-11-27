using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text.RegularExpressions;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed class CustomFieldSchemaTests : IAsyncLifetime
{
    private const string Previous = "20261127020000_ContactTagsFoundation";
    private readonly PostgresFixture db = new();
    private readonly Guid tenant = Guid.NewGuid(), other = Guid.NewGuid();
    private NpgsqlConnection owner = null!;
    private PlatformDbContext context = null!;

    public async Task InitializeAsync()
    {
        await db.InitializeAsync();
        owner = new NpgsqlConnection(db.MigrationConnectionString);
        await owner.OpenAsync();
        context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(owner,
            p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await context.GetService<IMigrator>().MigrateAsync(Previous);
        foreach (var id in new[] { tenant, other })
            await Sql(owner, "SELECT platform.create_tenant_with_owner($1,'Example','issuer','subject','correlation')", id);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(owner, db.ApplicationRole, default);
    }

    public async Task DisposeAsync()
    {
        await context.DisposeAsync();
        await owner.DisposeAsync();
        await db.DisposeAsync();
    }

    private static async Task<object?> Sql(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.Add(new NpgsqlParameter { Value = value });
        return await command.ExecuteScalarAsync();
    }

    private static async Task SetTenant(NpgsqlConnection conn, Guid id) =>
        await Sql(conn, "SELECT set_config('crm.tenant_id',$1,true)", id.ToString());

    [Fact]
    public async Task Audit_actions_in_model_and_snapshot_match_the_migrated_database()
    {
        const string name = "entries_action_check";
        var databaseSql = (string)(await Sql(owner, """
            SELECT pg_get_constraintdef(oid) FROM pg_constraint
            WHERE conrelid='audit.entries'::regclass AND conname=$1
            """, name))!;
        static string[] Actions(string sql) => Regex.Matches(sql, "'([^']+)'")
            .Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal).ToArray();

        var expected = Actions(databaseSql);
        Assert.Contains("tag.created", expected);
        Assert.Contains("custom_field.created", expected);
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        foreach (var model in new[] { context.GetService<IDesignTimeModel>().Model, snapshot })
        {
            var check = model.GetEntityTypes().SelectMany(type => type.GetCheckConstraints()).Single(c => c.Name == name);
            Assert.Equal(expected, Actions(check.Sql));
        }
    }

    [Fact]
    public async Task New_tables_are_tenant_scoped_and_definition_is_immutable()
    {
        foreach (var table in new[] { "crm.custom_field_defs", "crm.contact_field_values" })
            Assert.Equal(true, await Sql(owner, "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid=$1::regclass", table));
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        Assert.Equal(false, await Sql(app, "SELECT has_table_privilege(current_user,'crm.custom_field_defs','UPDATE,DELETE')"));
        Assert.Equal(0L, await Sql(app, "SELECT count(*) FROM crm.custom_field_defs"));
        await context.GetService<IMigrator>().MigrateAsync(Previous);
        Assert.Equal(DBNull.Value, await Sql(owner, "SELECT to_regclass('crm.custom_field_defs')::text"));
    }
    [Fact]
    public async Task Application_executes_only_the_journaled_attribute_mutation()
    {
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        foreach (var function in new[] { "platform.set_contact_tag(uuid,uuid,uuid,text,text,boolean)",
            "platform.set_contact_field(uuid,uuid,uuid,text,text,text)",
            "platform.canonical_json_number(text)", "platform.add_decimal_integer(text,text)" })
            Assert.Equal(false, await Sql(app, "SELECT has_function_privilege(current_user,$1,'EXECUTE')", function));
        Assert.Equal(true, await Sql(app, "SELECT has_function_privilege(current_user,'platform.mutate_contact_attribute(uuid,uuid,uuid,text,text,bigint,text,text,text)','EXECUTE')"));
    }

    [Fact]
    public async Task Existing_app_grants_upgrade_to_the_atomic_interface()
    {
        await context.GetService<IMigrator>().MigrateAsync("20261127032000_ContactFieldValue");
        var role = new NpgsqlCommandBuilder().QuoteIdentifier(db.ApplicationRole);
        await Sql(owner, $"GRANT EXECUTE ON FUNCTION platform.set_contact_tag(uuid,uuid,uuid,text,text,boolean) TO {role}");
        await DatabaseRoleGrants.ValidateAsync(owner, db.ApplicationRole, default);
        await context.Database.MigrateAsync();
        await DatabaseRoleGrants.ApplyAsync(owner, db.ApplicationRole, default);
        Assert.Equal(false, await Sql(owner, "SELECT has_function_privilege($1,'platform.set_contact_tag(uuid,uuid,uuid,text,text,boolean)','EXECUTE')", db.ApplicationRole));
    }

    [Fact]
    public async Task Downgrade_preserves_embedded_nul_names_in_another_tenant()
    {
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await SetTenant(owner, other);
            await Sql(owner, """
                INSERT INTO crm.tags(tenant_id,id,name,name_parts,normalized_parts)
                VALUES($1,gen_random_uuid(),'a'||chr(1)||'b',ARRAY['a','b'],ARRAY['a','b'])
                """, other);
            await tx.CommitAsync();
        }
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            context.GetService<IMigrator>().MigrateAsync("20261127033000_AtomicContactAttributes"));
        Assert.Contains("back up and use a forward fix", error.MessageText);
        await using var read = await owner.BeginTransactionAsync();
        await SetTenant(owner, other);
        Assert.Equal(new[] { "a", "b" }, (string[])(await Sql(owner, "SELECT name_parts FROM crm.tags WHERE tenant_id=$1", other))!);
    }

    [Fact]
    public async Task Number_canonicalization_matches_numeric_equality_and_large_exponent_carries()
    {
        Assert.Equal(0L, await Sql(owner, """
            SELECT count(*) FROM generate_series(-1000,1000) n
            WHERE platform.canonical_json_number(n::text) <>
                platform.canonical_json_number((n::bigint*10)::text||'e-1')
            """));
        foreach (var (a, b) in new[] {
            ("1e1000000000000", "10e999999999999"),
            ("1e-1000000000000", "10e-1000000000001"),
            ("1e999999999999999999999999", "0.1e1000000000000000000000000"),
            ("-1e-999999999999999999999999", "-0.1e-999999999999999999999998") })
            Assert.Equal(true, await Sql(owner, "SELECT platform.canonical_json_number($1)=platform.canonical_json_number($2)", a, b));
    }

    [Fact]
    public async Task Exact_numbers_upgrade_existing_values_and_guard_downgrade_across_tenants()
    {
        const string previous = "20261127034000_DefinitionNames";
        await context.GetService<IMigrator>().MigrateAsync(previous);
        var contact = Guid.NewGuid();
        var field = Guid.NewGuid();
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await SetTenant(owner, other);
            await Sql(owner, "INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES($1,$2,'Contact')", other, contact);
            await Sql(owner, "INSERT INTO crm.custom_field_defs(tenant_id,id,key,display_name,data_type,required) VALUES($1,$2,'num','Number','number',false)", other, field);
            await Sql(owner, "INSERT INTO crm.contact_field_values(tenant_id,contact_id,field_id,data_type,value) VALUES($1,$2,$3,'number','123.4500'::jsonb)", other, contact, field);
            await tx.CommitAsync();
        }
        await context.Database.MigrateAsync();
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await SetTenant(owner, other);
            Assert.Equal("123.4500", await Sql(owner, "SELECT value::text FROM crm.contact_field_values WHERE tenant_id=$1 AND contact_id=$2", other, contact));
            Assert.Equal(true, await Sql(owner, "SELECT platform.canonical_json_number(value::text)=platform.canonical_json_number('12345e-2') FROM crm.contact_field_values WHERE tenant_id=$1 AND contact_id=$2", other, contact));
            await Sql(owner, "UPDATE crm.contact_field_values SET number_raw='1e131072',number_key=platform.canonical_json_number('1e131072'),value='0'::jsonb WHERE tenant_id=$1 AND contact_id=$2", other, contact);
            await tx.CommitAsync();
        }
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(previous));
        Assert.Contains("back up and use a forward fix", error.MessageText);
        await using var verify = await owner.BeginTransactionAsync();
        await SetTenant(owner, other);
        Assert.Equal("1e131072", await Sql(owner, "SELECT number_raw FROM crm.contact_field_values WHERE tenant_id=$1 AND contact_id=$2", other, contact));
    }

    private const string InsertDefinition = """
        INSERT INTO crm.custom_field_defs(tenant_id,id,key,display_name,data_type,required)
        VALUES($1,$2,'anniversary','Anniversary','date',false)
        """;

    private async Task ExpectError(string sql, string code, params object[] arguments)
    {
        await using var tx = await owner.BeginTransactionAsync();
        await SetTenant(owner, tenant);
        var error = await Assert.ThrowsAsync<PostgresException>(() => Sql(owner, sql, arguments));
        Assert.Equal(code, error.SqlState);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Populated_upgrade_enforces_typed_values_keys_and_rollback_boundary()
    {
        var contact = Guid.NewGuid();
        var field = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        const string valueSql = "INSERT INTO crm.contact_field_values VALUES($1,$2,$3,$4,$5::jsonb)";
        foreach (var t in new[] { tenant, other })
        {
            await using var tx = await owner.BeginTransactionAsync();
            await SetTenant(owner, t);
            await Sql(owner, InsertDefinition, t, t == tenant ? field : foreign);
            await Sql(owner, "INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES($1,$2,'Contact')", t, t == tenant ? contact : foreign);
            await tx.CommitAsync();
        }
        await ExpectError(InsertDefinition, PostgresErrorCodes.UniqueViolation, tenant, Guid.NewGuid());
        await ExpectError("UPDATE crm.custom_field_defs SET display_name='Changed' WHERE tenant_id=$1", PostgresErrorCodes.ObjectNotInPrerequisiteState, tenant);
        foreach (var invalid in new[] { "null", "true", "3", "[]", "{}", "\"2026-02-29\"", "\"26-11-27\"" })
            await ExpectError(valueSql, PostgresErrorCodes.CheckViolation, tenant, contact, field, "date", invalid);
        await ExpectError(valueSql, PostgresErrorCodes.ForeignKeyViolation, tenant, contact, field, "number", "1");
        await ExpectError(valueSql, PostgresErrorCodes.ForeignKeyViolation, tenant, foreign, field, "date", "\"2024-02-29\"");
        await ExpectError(valueSql, PostgresErrorCodes.ForeignKeyViolation, tenant, contact, foreign, "date", "\"2024-02-29\"");
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await SetTenant(owner, tenant);
            await Sql(owner, valueSql, tenant, contact, field, "date", "\"2024-02-29\"");
            await tx.CommitAsync();
        }
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        Assert.Equal(0L, await Sql(app, "SELECT count(*) FROM crm.contact_field_values"));
        Assert.Equal(0L, await Sql(app, "SELECT count(*) FROM crm.custom_field_defs"));
        Assert.Equal(true, await Sql(app, "SELECT NOT rolbypassrls AND NOT rolsuper FROM pg_roles WHERE rolname=current_user"));
        Assert.Equal(false, await Sql(owner, "SELECT has_table_privilege($1,'audit.entries','INSERT,UPDATE,DELETE')", db.ApplicationRole));
        Assert.Equal(false, await Sql(app, "SELECT bool_or(pg_has_role(current_user,relowner,'MEMBER')) FROM pg_class WHERE oid IN ('crm.contact_field_values'::regclass,'crm.custom_field_defs'::regclass)"));
        var failure = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(Previous));
        Assert.Contains("back up", failure.MessageText);
        Assert.Contains("forward fix", failure.MessageText);
    }

    [Fact]
    public async Task Downgrade_preserves_strings_that_need_json_escaping()
    {
        var contact = Guid.NewGuid();
        var field = Guid.NewGuid();
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await SetTenant(owner, other);
            await Sql(owner, "INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES($1,$2,'Contact')", other, contact);
            await Sql(owner, "INSERT INTO crm.custom_field_defs(tenant_id,id,key,display_name,data_type,required) VALUES($1,$2,'x','X','string',false)", other, field);
            await Sql(owner, """
                INSERT INTO crm.contact_field_values(tenant_id,contact_id,field_id,data_type,value,escaped_string)
                VALUES($1,$2,$3,'string',to_jsonb($4::text),true)
                """, other, contact, field, """ "a\u0000" """.Trim());
            await tx.CommitAsync();
        }
        var failure = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync("20261127031000_ContactTagAssignment"));
        Assert.Contains("escaped field strings exist", failure.MessageText);
        await using var verify = await owner.BeginTransactionAsync();
        await SetTenant(owner, other);
        Assert.Equal(true, await Sql(owner, "SELECT escaped_string FROM crm.contact_field_values WHERE tenant_id=$1 AND contact_id=$2 AND field_id=$3", other, contact, field));
    }

    [Fact]
    public async Task Validation_rejects_an_assumable_role_with_definition_update()
    {
        await using var admin = new NpgsqlConnection(db.AdminConnectionString);
        await admin.OpenAsync();
        await Sql(admin, "CREATE ROLE field_editor NOLOGIN");
        await Sql(owner, "GRANT UPDATE ON crm.custom_field_defs TO field_editor");
        await Sql(admin, $"GRANT field_editor TO {PostgresFixture.Quote(db.ApplicationRole)} WITH INHERIT FALSE");
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGrants.ValidateAsync(owner, db.ApplicationRole, default));
    }
}
