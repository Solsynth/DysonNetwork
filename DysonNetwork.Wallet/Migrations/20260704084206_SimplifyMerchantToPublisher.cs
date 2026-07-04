using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Wallet.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyMerchantToPublisher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "type",
                table: "merchants");

            migrationBuilder.RenameColumn(
                name: "entity_id",
                table: "merchants",
                newName: "publisher_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "publisher_id",
                table: "merchants",
                newName: "entity_id");

            migrationBuilder.AddColumn<int>(
                name: "type",
                table: "merchants",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
