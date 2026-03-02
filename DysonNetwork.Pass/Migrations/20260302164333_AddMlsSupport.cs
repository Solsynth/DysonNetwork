using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class AddMlsSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mls_device_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mls_group_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    joined_epoch = table.Column<long>(type: "bigint", nullable: false),
                    last_seen_epoch = table.Column<long>(type: "bigint", nullable: true),
                    last_reshare_required_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    last_reshare_completed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mls_device_memberships", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mls_group_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mls_group_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    epoch = table.Column<long>(type: "bigint", nullable: false),
                    state_version = table.Column<long>(type: "bigint", nullable: false),
                    last_commit_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mls_group_states", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mls_key_packages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    device_label = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    key_package = table.Column<byte[]>(type: "bytea", nullable: false),
                    ciphersuite = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_consumed = table.Column<bool>(type: "boolean", nullable: false),
                    consumed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    consumed_by_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mls_key_packages", x => x.id);
                    table.ForeignKey(
                        name: "fk_mls_key_packages_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mls_device_memberships_chat_room_id_account_id_device_id",
                table: "mls_device_memberships",
                columns: new[] { "chat_room_id", "account_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mls_device_memberships_mls_group_id_last_seen_epoch",
                table: "mls_device_memberships",
                columns: new[] { "mls_group_id", "last_seen_epoch" });

            migrationBuilder.CreateIndex(
                name: "ix_mls_group_states_chat_room_id",
                table: "mls_group_states",
                column: "chat_room_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mls_group_states_mls_group_id_epoch",
                table: "mls_group_states",
                columns: new[] { "mls_group_id", "epoch" });

            migrationBuilder.CreateIndex(
                name: "ix_mls_key_packages_account_id_device_id_is_consumed",
                table: "mls_key_packages",
                columns: new[] { "account_id", "device_id", "is_consumed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mls_device_memberships");

            migrationBuilder.DropTable(
                name: "mls_group_states");

            migrationBuilder.DropTable(
                name: "mls_key_packages");
        }
    }
}
