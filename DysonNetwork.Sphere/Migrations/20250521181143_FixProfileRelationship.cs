using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class FixProfileRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_account_profiles_accounts_id",
                table: "account_profiles");

            migrationBuilder.CreateIndex(
                name: "ix_account_profiles_account_id",
                table: "account_profiles",
                column: "account_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_account_profiles_accounts_account_id",
                table: "account_profiles",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_account_profiles_accounts_account_id",
                table: "account_profiles");

            migrationBuilder.DropIndex(
                name: "ix_account_profiles_account_id",
                table: "account_profiles");

            migrationBuilder.AddForeignKey(
                name: "fk_account_profiles_accounts_id",
                table: "account_profiles",
                column: "id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
