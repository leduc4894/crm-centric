using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PlatformDbContext))]
[Migration("20261128010000_ManifestRegistryIsolation")]
public sealed class ManifestRegistryIsolation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        ManifestRegistry.RegisterFunctionSql("""
            -- The SELECT following the advisory lock needs a fresh statement snapshot.
            IF current_setting('transaction_isolation') <> 'read committed' THEN
                RETURN 'unsupported_isolation';
            END IF;
            """));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(ManifestRegistry.RegisterFunctionSql(""));
}
