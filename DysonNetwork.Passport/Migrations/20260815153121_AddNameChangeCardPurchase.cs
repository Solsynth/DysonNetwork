using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddNameChangeCardPurchase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "name_change_card_purchases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    fulfilled_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    consumed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    target_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    old_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    new_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_name_change_card_purchases", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_name_change_card_purchases_account_id_created_at",
                table: "name_change_card_purchases",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_name_change_card_purchases_order_id",
                table: "name_change_card_purchases",
                column: "order_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "name_change_card_purchases");
        }
    }
}
