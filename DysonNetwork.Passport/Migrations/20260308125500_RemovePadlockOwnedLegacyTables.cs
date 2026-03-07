using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations;

public partial class RemovePadlockOwnedLegacyTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS sn_mls_device_membership CASCADE;
            DROP TABLE IF EXISTS sn_mls_group_state CASCADE;
            DROP TABLE IF EXISTS sn_mls_key_package CASCADE;
            DROP TABLE IF EXISTS sn_e2ee_one_time_pre_key CASCADE;
            DROP TABLE IF EXISTS sn_e2ee_envelope CASCADE;
            DROP TABLE IF EXISTS sn_e2ee_session CASCADE;
            DROP TABLE IF EXISTS sn_e2ee_key_bundle CASCADE;
            DROP TABLE IF EXISTS sn_e2ee_device CASCADE;
            DROP TABLE IF EXISTS sn_auth_session CASCADE;
            DROP TABLE IF EXISTS sn_auth_client CASCADE;
            DROP TABLE IF EXISTS sn_auth_challenge CASCADE;
            DROP TABLE IF EXISTS sn_account_auth_factor CASCADE;
            DROP TABLE IF EXISTS sn_account_connection CASCADE;
            DROP TABLE IF EXISTS sn_account_contact CASCADE;
            DROP TABLE IF EXISTS sn_subscription_reference_object CASCADE;
            DROP TABLE IF EXISTS sn_account CASCADE;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally left empty. These tables are Padlock-owned and should not be recreated by Passport.
    }
}
