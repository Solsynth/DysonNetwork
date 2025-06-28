using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOidcProviderSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "remarks",
                table: "custom_app_secrets",
                newName: "description");

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

            migrationBuilder.AddColumn<bool>(
                name: "is_oidc",
                table: "custom_app_secrets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_custom_app_secrets_secret",
                table: "custom_app_secrets",
                column: "secret",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_custom_app_secrets_secret",
                table: "custom_app_secrets");

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
                name: "is_oidc",
                table: "custom_app_secrets");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "custom_app_secrets",
                newName: "remarks");
        }
    }
}
