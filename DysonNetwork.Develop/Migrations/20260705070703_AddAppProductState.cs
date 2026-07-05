using System;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Develop.Migrations
{
    /// <inheritdoc />
    public partial class AddAppProductState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<SnAppProductFulfillment>(
                name: "fulfillment",
                table: "app_products",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "app_product_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    stock_mode = table.Column<int>(type: "integer", nullable: false),
                    stock_quantity = table.Column<int>(type: "integer", nullable: true),
                    last_restocked_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_restocked_quantity = table.Column<int>(type: "integer", nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_product_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_app_product_states_app_products_product_id",
                        column: x => x.product_id,
                        principalTable: "app_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_product_states_product_id",
                table: "app_product_states",
                column: "product_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_product_states");

            migrationBuilder.DropColumn(
                name: "fulfillment",
                table: "app_products");
        }
    }
}
