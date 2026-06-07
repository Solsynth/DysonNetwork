using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddRealmPermissionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "realm_post_moderation_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    post_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moderator_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    moderated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realm_post_moderation_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_realm_post_moderation_logs_realms_realm_id",
                        column: x => x.realm_id,
                        principalTable: "realms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "realm_role_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_level = table.Column<int>(type: "integer", nullable: false),
                    can_chat = table.Column<bool>(type: "boolean", nullable: false),
                    can_post = table.Column<bool>(type: "boolean", nullable: false),
                    can_comment = table.Column<bool>(type: "boolean", nullable: false),
                    can_upload_media = table.Column<bool>(type: "boolean", nullable: false),
                    can_moderate_posts = table.Column<bool>(type: "boolean", nullable: false),
                    can_moderate_chat = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_members = table.Column<bool>(type: "boolean", nullable: false),
                    can_manage_realm = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realm_role_permissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_realm_role_permissions_realms_realm_id",
                        column: x => x.realm_id,
                        principalTable: "realms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "realm_user_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    can_chat = table.Column<bool>(type: "boolean", nullable: true),
                    can_post = table.Column<bool>(type: "boolean", nullable: true),
                    can_comment = table.Column<bool>(type: "boolean", nullable: true),
                    can_upload_media = table.Column<bool>(type: "boolean", nullable: true),
                    can_moderate_posts = table.Column<bool>(type: "boolean", nullable: true),
                    can_moderate_chat = table.Column<bool>(type: "boolean", nullable: true),
                    can_manage_members = table.Column<bool>(type: "boolean", nullable: true),
                    can_manage_realm = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realm_user_permissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_realm_user_permissions_realms_realm_id",
                        column: x => x.realm_id,
                        principalTable: "realms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_realm_post_moderation_logs_realm_id",
                table: "realm_post_moderation_logs",
                column: "realm_id");

            migrationBuilder.CreateIndex(
                name: "ix_realm_role_permissions_realm_id_role_level",
                table: "realm_role_permissions",
                columns: new[] { "realm_id", "role_level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_realm_user_permissions_realm_id_account_id",
                table: "realm_user_permissions",
                columns: new[] { "realm_id", "account_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "realm_post_moderation_logs");

            migrationBuilder.DropTable(
                name: "realm_role_permissions");

            migrationBuilder.DropTable(
                name: "realm_user_permissions");
        }
    }
}
