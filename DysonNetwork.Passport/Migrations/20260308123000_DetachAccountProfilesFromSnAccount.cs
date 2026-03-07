using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations;

public partial class DetachAccountProfilesFromSnAccount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE account_profiles
            DROP CONSTRAINT IF EXISTS fk_account_profiles_sn_account_account_id;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE account_profiles
            ADD CONSTRAINT fk_account_profiles_sn_account_account_id
            FOREIGN KEY (account_id) REFERENCES sn_account (id) ON DELETE CASCADE;
            """);
    }
}
