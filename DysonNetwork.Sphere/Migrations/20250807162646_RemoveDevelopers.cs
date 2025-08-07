using System;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDevelopers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_app_secrets");

            migrationBuilder.DropTable(
                name: "realm_tags");

            migrationBuilder.DropTable(
                name: "custom_apps");

            migrationBuilder.DropTable(
                name: "tags");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "custom_apps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    background = table.Column<CloudFileReferenceObject>(type: "jsonb", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    picture = table.Column<CloudFileReferenceObject>(type: "jsonb", nullable: true),
                    slug = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    verification = table.Column<VerificationMark>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_apps", x => x.id);
                    table.ForeignKey(
                        name: "fk_custom_apps_publishers_publisher_id",
                        column: x => x.publisher_id,
                        principalTable: "publishers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "custom_app_secrets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_oidc = table.Column<bool>(type: "boolean", nullable: false),
                    secret = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "realm_tags",
                columns: table => new
                {
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realm_tags", x => new { x.realm_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_realm_tags_realms_realm_id",
                        column: x => x.realm_id,
                        principalTable: "realms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_realm_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_custom_app_secrets_app_id",
                table: "custom_app_secrets",
                column: "app_id");

            migrationBuilder.CreateIndex(
                name: "ix_custom_app_secrets_secret",
                table: "custom_app_secrets",
                column: "secret",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_custom_apps_publisher_id",
                table: "custom_apps",
                column: "publisher_id");

            migrationBuilder.CreateIndex(
                name: "ix_realm_tags_tag_id",
                table: "realm_tags",
                column: "tag_id");
        }
    }
}
