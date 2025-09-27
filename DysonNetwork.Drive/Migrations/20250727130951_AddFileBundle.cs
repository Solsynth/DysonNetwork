using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class AddFileBundle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bundle_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bundles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    passcode = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bundles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_files_bundle_id",
                table: "files",
                column: "bundle_id");

            migrationBuilder.CreateIndex(
                name: "ix_bundles_slug",
                table: "bundles",
                column: "slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_files_bundles_bundle_id",
                table: "files",
                column: "bundle_id",
                principalTable: "bundles",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_bundles_bundle_id",
                table: "files");

            migrationBuilder.DropTable(
                name: "bundles");

            migrationBuilder.DropIndex(
                name: "ix_files_bundle_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "bundle_id",
                table: "files");
        }
    }
}
