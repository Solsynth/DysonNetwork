using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Develop.Migrations
{
    /// <inheritdoc />
    public partial class AddDevProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_custom_apps_developers_developer_id",
                table: "custom_apps");

            migrationBuilder.RenameColumn(
                name: "developer_id",
                table: "custom_apps",
                newName: "project_id");

            migrationBuilder.RenameIndex(
                name: "ix_custom_apps_developer_id",
                table: "custom_apps",
                newName: "ix_custom_apps_project_id");

            migrationBuilder.CreateTable(
                name: "dev_projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    developer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dev_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_dev_projects_developers_developer_id",
                        column: x => x.developer_id,
                        principalTable: "developers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dev_projects_developer_id",
                table: "dev_projects",
                column: "developer_id");

            migrationBuilder.AddForeignKey(
                name: "fk_custom_apps_dev_projects_project_id",
                table: "custom_apps",
                column: "project_id",
                principalTable: "dev_projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_custom_apps_dev_projects_project_id",
                table: "custom_apps");

            migrationBuilder.DropTable(
                name: "dev_projects");

            migrationBuilder.RenameColumn(
                name: "project_id",
                table: "custom_apps",
                newName: "developer_id");

            migrationBuilder.RenameIndex(
                name: "ix_custom_apps_project_id",
                table: "custom_apps",
                newName: "ix_custom_apps_developer_id");

            migrationBuilder.AddForeignKey(
                name: "fk_custom_apps_developers_developer_id",
                table: "custom_apps",
                column: "developer_id",
                principalTable: "developers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
