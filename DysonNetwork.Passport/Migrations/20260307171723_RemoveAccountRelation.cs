using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAccountRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_account_check_in_results_sn_account_account_id",
                table: "account_check_in_results");

            migrationBuilder.DropForeignKey(
                name: "fk_account_statuses_sn_account_account_id",
                table: "account_statuses");

            migrationBuilder.DropForeignKey(
                name: "fk_affiliation_spells_sn_account_account_id",
                table: "affiliation_spells");

            migrationBuilder.DropForeignKey(
                name: "fk_badges_sn_account_account_id",
                table: "badges");

            migrationBuilder.DropForeignKey(
                name: "fk_experience_records_sn_account_account_id",
                table: "experience_records");

            migrationBuilder.DropForeignKey(
                name: "fk_magic_spells_sn_account_account_id",
                table: "magic_spells");

            migrationBuilder.DropForeignKey(
                name: "fk_presence_activities_sn_account_account_id",
                table: "presence_activities");

            migrationBuilder.DropForeignKey(
                name: "fk_rewind_points_sn_account_account_id",
                table: "rewind_points");

            migrationBuilder.DropForeignKey(
                name: "fk_social_credit_records_sn_account_account_id",
                table: "social_credit_records");

            migrationBuilder.DropForeignKey(
                name: "fk_ticket_messages_sn_account_sender_id",
                table: "ticket_messages");

            migrationBuilder.DropForeignKey(
                name: "fk_tickets_sn_account_assignee_id",
                table: "tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_tickets_sn_account_creator_id",
                table: "tickets");

            migrationBuilder.DropTable(
                name: "lotteries");

            migrationBuilder.DropIndex(
                name: "ix_tickets_assignee_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_creator_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_ticket_messages_sender_id",
                table: "ticket_messages");

            migrationBuilder.DropIndex(
                name: "ix_social_credit_records_account_id",
                table: "social_credit_records");

            migrationBuilder.DropIndex(
                name: "ix_rewind_points_account_id",
                table: "rewind_points");

            migrationBuilder.DropIndex(
                name: "ix_presence_activities_account_id",
                table: "presence_activities");

            migrationBuilder.DropIndex(
                name: "ix_magic_spells_account_id",
                table: "magic_spells");

            migrationBuilder.DropIndex(
                name: "ix_experience_records_account_id",
                table: "experience_records");

            migrationBuilder.DropIndex(
                name: "ix_badges_account_id",
                table: "badges");

            migrationBuilder.DropIndex(
                name: "ix_affiliation_spells_account_id",
                table: "affiliation_spells");

            migrationBuilder.DropIndex(
                name: "ix_account_statuses_account_id",
                table: "account_statuses");

            migrationBuilder.DropIndex(
                name: "ix_account_check_in_results_account_id",
                table: "account_check_in_results");

            migrationBuilder.AddColumn<Guid>(
                name: "sn_account_id",
                table: "badges",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sn_subscription_reference_object",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "text", nullable: false),
                    begun_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    is_free_trial = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric", nullable: false),
                    final_price = table.Column<decimal>(type: "numeric", nullable: false),
                    renewal_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sn_subscription_reference_object", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_badges_sn_account_id",
                table: "badges",
                column: "sn_account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_badges_sn_account_sn_account_id",
                table: "badges",
                column: "sn_account_id",
                principalTable: "sn_account",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_badges_sn_account_sn_account_id",
                table: "badges");

            migrationBuilder.DropTable(
                name: "sn_subscription_reference_object");

            migrationBuilder.DropIndex(
                name: "ix_badges_sn_account_id",
                table: "badges");

            migrationBuilder.DropColumn(
                name: "sn_account_id",
                table: "badges");

            migrationBuilder.CreateTable(
                name: "lotteries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    draw_date = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    draw_status = table.Column<int>(type: "integer", nullable: false),
                    matched_region_one_numbers = table.Column<string>(type: "jsonb", nullable: true),
                    matched_region_two_number = table.Column<int>(type: "integer", nullable: true),
                    multiplier = table.Column<int>(type: "integer", nullable: false),
                    region_one_numbers = table.Column<string>(type: "jsonb", nullable: false),
                    region_two_number = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lotteries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_assignee_id",
                table: "tickets",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_creator_id",
                table: "tickets",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_messages_sender_id",
                table: "ticket_messages",
                column: "sender_id");

            migrationBuilder.CreateIndex(
                name: "ix_social_credit_records_account_id",
                table: "social_credit_records",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_rewind_points_account_id",
                table: "rewind_points",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_presence_activities_account_id",
                table: "presence_activities",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_magic_spells_account_id",
                table: "magic_spells",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_experience_records_account_id",
                table: "experience_records",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_badges_account_id",
                table: "badges",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_affiliation_spells_account_id",
                table: "affiliation_spells",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_statuses_account_id",
                table: "account_statuses",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_check_in_results_account_id",
                table: "account_check_in_results",
                column: "account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_account_check_in_results_sn_account_account_id",
                table: "account_check_in_results",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_account_statuses_sn_account_account_id",
                table: "account_statuses",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_affiliation_spells_sn_account_account_id",
                table: "affiliation_spells",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_badges_sn_account_account_id",
                table: "badges",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_experience_records_sn_account_account_id",
                table: "experience_records",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_magic_spells_sn_account_account_id",
                table: "magic_spells",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_presence_activities_sn_account_account_id",
                table: "presence_activities",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_rewind_points_sn_account_account_id",
                table: "rewind_points",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_social_credit_records_sn_account_account_id",
                table: "social_credit_records",
                column: "account_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_ticket_messages_sn_account_sender_id",
                table: "ticket_messages",
                column: "sender_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_sn_account_assignee_id",
                table: "tickets",
                column: "assignee_id",
                principalTable: "sn_account",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_sn_account_creator_id",
                table: "tickets",
                column: "creator_id",
                principalTable: "sn_account",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
