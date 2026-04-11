using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedAtToUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sticker_packs_prefix",
                table: "sticker_packs");

            migrationBuilder.DropIndex(
                name: "ix_publishing_settings_account_id",
                table: "publishing_settings");

            migrationBuilder.DropIndex(
                name: "ix_publishers_name",
                table: "publishers");

            migrationBuilder.DropIndex(
                name: "ix_post_interest_profiles_account_id_kind_reference_id",
                table: "post_interest_profiles");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_keys_key_id",
                table: "fediverse_keys");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_instances_domain",
                table: "fediverse_instances");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_actors_uri",
                table: "fediverse_actors");

            migrationBuilder.DropIndex(
                name: "ix_discovery_preferences_account_id_kind_reference_id",
                table: "discovery_preferences");

            migrationBuilder.DropIndex(
                name: "ix_automod_rules_name",
                table: "automod_rules");

            migrationBuilder.CreateIndex(
                name: "ix_sticker_packs_prefix_deleted_at",
                table: "sticker_packs",
                columns: new[] { "prefix", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publishing_settings_account_id_deleted_at",
                table: "publishing_settings",
                columns: new[] { "account_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publishers_name_deleted_at",
                table: "publishers",
                columns: new[] { "name", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_interest_profiles_account_id_kind_reference_id_deleted",
                table: "post_interest_profiles",
                columns: new[] { "account_id", "kind", "reference_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_keys_key_id_deleted_at",
                table: "fediverse_keys",
                columns: new[] { "key_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_instances_domain_deleted_at",
                table: "fediverse_instances",
                columns: new[] { "domain", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_actors_uri_deleted_at",
                table: "fediverse_actors",
                columns: new[] { "uri", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discovery_preferences_account_id_kind_reference_id_deleted_",
                table: "discovery_preferences",
                columns: new[] { "account_id", "kind", "reference_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_automod_rules_name_deleted_at",
                table: "automod_rules",
                columns: new[] { "name", "deleted_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sticker_packs_prefix_deleted_at",
                table: "sticker_packs");

            migrationBuilder.DropIndex(
                name: "ix_publishing_settings_account_id_deleted_at",
                table: "publishing_settings");

            migrationBuilder.DropIndex(
                name: "ix_publishers_name_deleted_at",
                table: "publishers");

            migrationBuilder.DropIndex(
                name: "ix_post_interest_profiles_account_id_kind_reference_id_deleted",
                table: "post_interest_profiles");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_keys_key_id_deleted_at",
                table: "fediverse_keys");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_instances_domain_deleted_at",
                table: "fediverse_instances");

            migrationBuilder.DropIndex(
                name: "ix_fediverse_actors_uri_deleted_at",
                table: "fediverse_actors");

            migrationBuilder.DropIndex(
                name: "ix_discovery_preferences_account_id_kind_reference_id_deleted_",
                table: "discovery_preferences");

            migrationBuilder.DropIndex(
                name: "ix_automod_rules_name_deleted_at",
                table: "automod_rules");

            migrationBuilder.CreateIndex(
                name: "ix_sticker_packs_prefix",
                table: "sticker_packs",
                column: "prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publishing_settings_account_id",
                table: "publishing_settings",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_publishers_name",
                table: "publishers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_post_interest_profiles_account_id_kind_reference_id",
                table: "post_interest_profiles",
                columns: new[] { "account_id", "kind", "reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_keys_key_id",
                table: "fediverse_keys",
                column: "key_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_instances_domain",
                table: "fediverse_instances",
                column: "domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_actors_uri",
                table: "fediverse_actors",
                column: "uri",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discovery_preferences_account_id_kind_reference_id",
                table: "discovery_preferences",
                columns: new[] { "account_id", "kind", "reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_automod_rules_name",
                table: "automod_rules",
                column: "name",
                unique: true);
        }
    }
}
