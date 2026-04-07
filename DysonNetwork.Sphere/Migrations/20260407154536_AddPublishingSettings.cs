using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "publishing_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_posting_publisher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    default_reply_publisher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    default_fediverse_publisher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_publishing_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_publishing_settings_publishers_default_fediverse_publisher_",
                        column: x => x.default_fediverse_publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_publishing_settings_publishers_default_posting_publisher_id",
                        column: x => x.default_posting_publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_publishing_settings_publishers_default_reply_publisher_id",
                        column: x => x.default_reply_publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_publishing_settings_account_id",
                table: "publishing_settings",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publishing_settings_default_fediverse_publisher_id",
                table: "publishing_settings",
                column: "default_fediverse_publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_publishing_settings_default_posting_publisher_id",
                table: "publishing_settings",
                column: "default_posting_publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_publishing_settings_default_reply_publisher_id",
                table: "publishing_settings",
                column: "default_reply_publisher_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "publishing_settings");
        }
    }
}
