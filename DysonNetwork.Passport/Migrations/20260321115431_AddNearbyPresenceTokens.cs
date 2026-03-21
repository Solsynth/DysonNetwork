using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddNearbyPresenceTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "nearby_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    discoverable = table.Column<bool>(type: "boolean", nullable: false),
                    friend_only = table.Column<bool>(type: "boolean", nullable: false),
                    capabilities = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    last_heartbeat_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_token_issued_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nearby_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "nearby_presence_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    valid_from = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    discoverable = table.Column<bool>(type: "boolean", nullable: false),
                    friend_only = table.Column<bool>(type: "boolean", nullable: false),
                    capabilities = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nearby_presence_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_nearby_presence_tokens_nearby_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "nearby_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_nearby_devices_user_id_device_id",
                table: "nearby_devices",
                columns: new[] { "user_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nearby_presence_tokens_device_id_slot",
                table: "nearby_presence_tokens",
                columns: new[] { "device_id", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nearby_presence_tokens_token_hash",
                table: "nearby_presence_tokens",
                column: "token_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "nearby_presence_tokens");

            migrationBuilder.DropTable(
                name: "nearby_devices");
        }
    }
}
