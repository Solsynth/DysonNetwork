using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedAtToUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_realms_slug",
                table: "realms");

            migrationBuilder.DropIndex(
                name: "ix_quest_definitions_identifier",
                table: "quest_definitions");

            migrationBuilder.DropIndex(
                name: "ix_progress_reward_grants_reward_token",
                table: "progress_reward_grants");

            migrationBuilder.DropIndex(
                name: "ix_progress_event_receipts_event_id_definition_type_definition",
                table: "progress_event_receipts");

            migrationBuilder.DropIndex(
                name: "ix_nearby_presence_tokens_device_id_slot",
                table: "nearby_presence_tokens");

            migrationBuilder.DropIndex(
                name: "ix_nearby_devices_user_id_device_id",
                table: "nearby_devices");

            migrationBuilder.DropIndex(
                name: "ix_magic_spells_spell",
                table: "magic_spells");

            migrationBuilder.DropIndex(
                name: "ix_affiliation_spells_spell",
                table: "affiliation_spells");

            migrationBuilder.DropIndex(
                name: "ix_achievement_definitions_identifier",
                table: "achievement_definitions");

            migrationBuilder.DropIndex(
                name: "ix_account_quest_progresses_account_id_quest_definition_id_per",
                table: "account_quest_progresses");

            migrationBuilder.DropIndex(
                name: "ix_account_achievements_account_id_achievement_definition_id",
                table: "account_achievements");

            migrationBuilder.CreateIndex(
                name: "ix_realms_slug_deleted_at",
                table: "realms",
                columns: new[] { "slug", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quest_definitions_identifier_deleted_at",
                table: "quest_definitions",
                columns: new[] { "identifier", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_progress_reward_grants_reward_token_deleted_at",
                table: "progress_reward_grants",
                columns: new[] { "reward_token", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_progress_event_receipts_event_id_definition_type_definition",
                table: "progress_event_receipts",
                columns: new[] { "event_id", "definition_type", "definition_identifier", "period_key", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nearby_presence_tokens_device_id_slot_deleted_at",
                table: "nearby_presence_tokens",
                columns: new[] { "device_id", "slot", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nearby_devices_user_id_device_id_deleted_at",
                table: "nearby_devices",
                columns: new[] { "user_id", "device_id", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_magic_spells_spell_deleted_at",
                table: "magic_spells",
                columns: new[] { "spell", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_affiliation_spells_spell_deleted_at",
                table: "affiliation_spells",
                columns: new[] { "spell", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_achievement_definitions_identifier_deleted_at",
                table: "achievement_definitions",
                columns: new[] { "identifier", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_quest_progresses_account_id_quest_definition_id_per",
                table: "account_quest_progresses",
                columns: new[] { "account_id", "quest_definition_id", "period_key", "deleted_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_achievements_account_id_achievement_definition_id_d",
                table: "account_achievements",
                columns: new[] { "account_id", "achievement_definition_id", "deleted_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_realms_slug_deleted_at",
                table: "realms");

            migrationBuilder.DropIndex(
                name: "ix_quest_definitions_identifier_deleted_at",
                table: "quest_definitions");

            migrationBuilder.DropIndex(
                name: "ix_progress_reward_grants_reward_token_deleted_at",
                table: "progress_reward_grants");

            migrationBuilder.DropIndex(
                name: "ix_progress_event_receipts_event_id_definition_type_definition",
                table: "progress_event_receipts");

            migrationBuilder.DropIndex(
                name: "ix_nearby_presence_tokens_device_id_slot_deleted_at",
                table: "nearby_presence_tokens");

            migrationBuilder.DropIndex(
                name: "ix_nearby_devices_user_id_device_id_deleted_at",
                table: "nearby_devices");

            migrationBuilder.DropIndex(
                name: "ix_magic_spells_spell_deleted_at",
                table: "magic_spells");

            migrationBuilder.DropIndex(
                name: "ix_affiliation_spells_spell_deleted_at",
                table: "affiliation_spells");

            migrationBuilder.DropIndex(
                name: "ix_achievement_definitions_identifier_deleted_at",
                table: "achievement_definitions");

            migrationBuilder.DropIndex(
                name: "ix_account_quest_progresses_account_id_quest_definition_id_per",
                table: "account_quest_progresses");

            migrationBuilder.DropIndex(
                name: "ix_account_achievements_account_id_achievement_definition_id_d",
                table: "account_achievements");

            migrationBuilder.CreateIndex(
                name: "ix_realms_slug",
                table: "realms",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quest_definitions_identifier",
                table: "quest_definitions",
                column: "identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_progress_reward_grants_reward_token",
                table: "progress_reward_grants",
                column: "reward_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_progress_event_receipts_event_id_definition_type_definition",
                table: "progress_event_receipts",
                columns: new[] { "event_id", "definition_type", "definition_identifier", "period_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nearby_presence_tokens_device_id_slot",
                table: "nearby_presence_tokens",
                columns: new[] { "device_id", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_nearby_devices_user_id_device_id",
                table: "nearby_devices",
                columns: new[] { "user_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_magic_spells_spell",
                table: "magic_spells",
                column: "spell",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_affiliation_spells_spell",
                table: "affiliation_spells",
                column: "spell",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_achievement_definitions_identifier",
                table: "achievement_definitions",
                column: "identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_quest_progresses_account_id_quest_definition_id_per",
                table: "account_quest_progresses",
                columns: new[] { "account_id", "quest_definition_id", "period_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_achievements_account_id_achievement_definition_id",
                table: "account_achievements",
                columns: new[] { "account_id", "achievement_definition_id" },
                unique: true);
        }
    }
}
