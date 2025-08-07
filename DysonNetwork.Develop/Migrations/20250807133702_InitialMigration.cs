using System;
using DysonNetwork.Develop.Identity;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Develop.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "developers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_developers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "custom_apps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    picture = table.Column<CloudFileReferenceObject>(type: "jsonb", nullable: true),
                    background = table.Column<CloudFileReferenceObject>(type: "jsonb", nullable: true),
                    verification = table.Column<VerificationMark>(type: "jsonb", nullable: true),
                    oauth_config = table.Column<CustomAppOauthConfig>(type: "jsonb", nullable: true),
                    links = table.Column<CustomAppLinks>(type: "jsonb", nullable: true),
                    developer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_apps", x => x.id);
                    table.ForeignKey(
                        name: "fk_custom_apps_developers_developer_id",
                        column: x => x.developer_id,
                        principalTable: "developers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "custom_app_secrets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_oidc = table.Column<bool>(type: "boolean", nullable: false),
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
                name: "ix_custom_app_secrets_app_id",
                table: "custom_app_secrets",
                column: "app_id");

            migrationBuilder.CreateIndex(
                name: "ix_custom_apps_developer_id",
                table: "custom_apps",
                column: "developer_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_app_secrets");

            migrationBuilder.DropTable(
                name: "custom_apps");

            migrationBuilder.DropTable(
                name: "developers");
        }
    }
}
