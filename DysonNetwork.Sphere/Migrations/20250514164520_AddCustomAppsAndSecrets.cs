using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomAppsAndSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "issuer_app_id",
                table: "payment_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "custom_app_secrets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    remarks = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_app_secrets", x => x.id);
                    table.ForeignKey(
                        name: "fk_custom_app_secrets_custom_apps_app_id",
                        column: x => x.app_id,
                        principalTable: "custom_apps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_orders_issuer_app_id",
                table: "payment_orders",
                column: "issuer_app_id");

            migrationBuilder.CreateIndex(
                name: "ix_custom_app_secrets_app_id",
                table: "custom_app_secrets",
                column: "app_id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_orders_custom_apps_issuer_app_id",
                table: "payment_orders",
                column: "issuer_app_id",
                principalTable: "custom_apps",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_orders_custom_apps_issuer_app_id",
                table: "payment_orders");

            migrationBuilder.DropTable(
                name: "custom_app_secrets");

            migrationBuilder.DropIndex(
                name: "ix_payment_orders_issuer_app_id",
                table: "payment_orders");

            migrationBuilder.DropColumn(
                name: "issuer_app_id",
                table: "payment_orders");
        }
    }
}
