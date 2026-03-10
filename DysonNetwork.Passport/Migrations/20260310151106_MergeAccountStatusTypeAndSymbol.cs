using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class MergeAccountStatusTypeAndSymbol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "symbol",
                table: "account_statuses",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "type",
                table: "account_statuses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE account_statuses
                SET type = CASE
                    WHEN is_invisible THEN 3
                    WHEN is_not_disturb THEN 2
                    ELSE 0
                END;
                """);

            migrationBuilder.DropColumn(
                name: "is_invisible",
                table: "account_statuses");

            migrationBuilder.DropColumn(
                name: "is_not_disturb",
                table: "account_statuses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_invisible",
                table: "account_statuses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_not_disturb",
                table: "account_statuses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE account_statuses
                SET is_invisible = CASE WHEN type = 3 THEN TRUE ELSE FALSE END,
                    is_not_disturb = CASE WHEN type = 2 THEN TRUE ELSE FALSE END;
                """);

            migrationBuilder.DropColumn(
                name: "symbol",
                table: "account_statuses");

            migrationBuilder.DropColumn(
                name: "type",
                table: "account_statuses");
        }
    }
}
