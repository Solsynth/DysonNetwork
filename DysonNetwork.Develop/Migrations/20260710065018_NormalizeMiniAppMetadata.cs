using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Develop.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeMiniAppMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "author",
                table: "mini_apps",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<SnCloudFileReferenceObject>(
                name: "background",
                table: "mini_apps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "mini_apps",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "entry_url",
                table: "mini_apps",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "homepage",
                table: "mini_apps",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<SnCloudFileReferenceObject>(
                name: "icon",
                table: "mini_apps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "mini_apps",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "package_sha256",
                table: "mini_apps",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "package_size",
                table: "mini_apps",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "package_storage_key",
                table: "mini_apps",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "package_url",
                table: "mini_apps",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plugin_id",
                table: "mini_apps",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "version",
                table: "mini_apps",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE mini_apps
                SET plugin_id = COALESCE(NULLIF(manifest ->> 'id', ''), slug),
                    name = COALESCE(NULLIF(manifest ->> 'name', ''), slug),
                    version = COALESCE(NULLIF(manifest ->> 'version', ''), '1.0.0'),
                    author = NULLIF(manifest ->> 'author', ''),
                    description = NULLIF(manifest ->> 'description', ''),
                    entry_url = COALESCE(NULLIF(manifest ->> 'entry_url', ''), ''),
                    homepage = NULLIF(manifest ->> 'homepage', '');
                """);

            migrationBuilder.CreateIndex(
                name: "ix_mini_apps_plugin_id",
                table: "mini_apps",
                column: "plugin_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mini_apps_plugin_id",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "author",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "background",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "description",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "entry_url",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "homepage",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "icon",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "name",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "package_sha256",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "package_size",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "package_storage_key",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "package_url",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "plugin_id",
                table: "mini_apps");

            migrationBuilder.DropColumn(
                name: "version",
                table: "mini_apps");
        }
    }
}
