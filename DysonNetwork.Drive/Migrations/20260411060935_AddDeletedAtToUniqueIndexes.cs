using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedAtToUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bundles_slug",
                table: "bundles");

            migrationBuilder.CreateIndex(
                name: "ix_bundles_slug_deleted_at",
                table: "bundles",
                columns: new[] { "slug", "deleted_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bundles_slug_deleted_at",
                table: "bundles");

            migrationBuilder.CreateIndex(
                name: "ix_bundles_slug",
                table: "bundles",
                column: "slug",
                unique: true);
        }
    }
}
