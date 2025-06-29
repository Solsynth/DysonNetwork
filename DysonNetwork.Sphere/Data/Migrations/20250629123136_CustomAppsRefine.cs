using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Developer;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomAppsRefine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allow_offline_access",
                table: "custom_apps");

            migrationBuilder.DropColumn(
                name: "allowed_grant_types",
                table: "custom_apps");

            migrationBuilder.DropColumn(
                name: "allowed_scopes",
                table: "custom_apps");

            migrationBuilder.DropColumn(
                name: "client_uri",
                table: "custom_apps");

            migrationBuilder.DropColumn(
                name: "logo_uri",
                table: "custom_apps");

            migrationBuilder.DropColumn(
                name: "post_logout_redirect_uris",
                table: "custom_apps");

            migrationBuilder.DropColumn(
                name: "redirect_uris",
                table: "custom_apps");

            migrationBuilder.DropColumn(
                name: "require_pkce",
                table: "custom_apps");

            migrationBuilder.DropColumn(
                name: "verified_at",
                table: "custom_apps");

            migrationBuilder.RenameColumn(
                name: "verified_as",
                table: "custom_apps",
                newName: "description");

            migrationBuilder.AddColumn<CloudFileReferenceObject>(
                name: "background",
                table: "custom_apps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<CustomAppLinks>(
                name: "links",
                table: "custom_apps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<CustomAppOauthConfig>(
                name: "oauth_config",
                table: "custom_apps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<CloudFileReferenceObject>(
                name: "picture",
                table: "custom_apps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<VerificationMark>(
                name: "verification",
                table: "custom_apps",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "background",
                table: "custom_apps");

            migrationBuilder.DropColumn(
                name: "links",
                table: "custom_apps");

            migrationBuilder.DropColumn(
                name: "oauth_config",
                table: "custom_apps");

            migrationBuilder.DropColumn(
                name: "picture",
                table: "custom_apps");

            migrationBuilder.DropColumn(
                name: "verification",
                table: "custom_apps");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "custom_apps",
                newName: "verified_as");

            migrationBuilder.AddColumn<bool>(
                name: "allow_offline_access",
                table: "custom_apps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "allowed_grant_types",
                table: "custom_apps",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "allowed_scopes",
                table: "custom_apps",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "client_uri",
                table: "custom_apps",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "logo_uri",
                table: "custom_apps",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "post_logout_redirect_uris",
                table: "custom_apps",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "redirect_uris",
                table: "custom_apps",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "require_pkce",
                table: "custom_apps",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Instant>(
                name: "verified_at",
                table: "custom_apps",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
