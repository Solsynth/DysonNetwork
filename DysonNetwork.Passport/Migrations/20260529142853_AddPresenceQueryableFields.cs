using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddPresenceQueryableFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "presence_activities",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "queryable_terms",
                table: "presence_activities",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "reference_id",
                table: "presence_activities",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_presence_activities_account_id_provider_deleted_at",
                table: "presence_activities",
                columns: new[] { "account_id", "provider", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_presence_activities_provider_reference_id_deleted_at",
                table: "presence_activities",
                columns: new[] { "provider", "reference_id", "deleted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_presence_activities_account_id_provider_deleted_at",
                table: "presence_activities");

            migrationBuilder.DropIndex(
                name: "ix_presence_activities_provider_reference_id_deleted_at",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "provider",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "queryable_terms",
                table: "presence_activities");

            migrationBuilder.DropColumn(
                name: "reference_id",
                table: "presence_activities");
        }
    }
}
