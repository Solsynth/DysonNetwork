using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class RenamePollToSurvey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_poll_answers_polls_poll_id",
                table: "poll_answers");

            migrationBuilder.DropForeignKey(
                name: "fk_poll_questions_polls_poll_id",
                table: "poll_questions");

            migrationBuilder.DropForeignKey(
                name: "fk_polls_publishers_publisher_id",
                table: "polls");

            migrationBuilder.DropPrimaryKey(
                name: "pk_polls",
                table: "polls");

            migrationBuilder.DropPrimaryKey(
                name: "pk_poll_questions",
                table: "poll_questions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_poll_answers",
                table: "poll_answers");

            migrationBuilder.RenameTable(
                name: "polls",
                newName: "surveys");

            migrationBuilder.RenameTable(
                name: "poll_questions",
                newName: "survey_questions");

            migrationBuilder.RenameTable(
                name: "poll_answers",
                newName: "survey_answers");

            migrationBuilder.RenameIndex(
                name: "ix_polls_publisher_id",
                table: "surveys",
                newName: "ix_surveys_publisher_id");

            migrationBuilder.RenameColumn(
                name: "poll_id",
                table: "survey_questions",
                newName: "survey_id");

            migrationBuilder.RenameIndex(
                name: "ix_poll_questions_poll_id",
                table: "survey_questions",
                newName: "ix_survey_questions_survey_id");

            migrationBuilder.RenameColumn(
                name: "poll_id",
                table: "survey_answers",
                newName: "survey_id");

            migrationBuilder.RenameIndex(
                name: "ix_poll_answers_poll_id",
                table: "survey_answers",
                newName: "ix_survey_answers_survey_id");

            migrationBuilder.AddColumn<List<SnCloudFileReferenceObject>>(
                name: "attachments",
                table: "surveys",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<bool>(
                name: "notify_subscribers",
                table: "surveys",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Instant>(
                name: "published_at",
                table: "surveys",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "surveys",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<List<SnCloudFileReferenceObject>>(
                name: "attachments",
                table: "survey_questions",
                type: "jsonb",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "max_length",
                table: "survey_questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_selections",
                table: "survey_questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "max_value",
                table: "survey_questions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "min_value",
                table: "survey_questions",
                type: "double precision",
                nullable: true);

            // Backfill: all pre-existing rows were already live. Mark them Published so
            // prior submissions remain immutable under the new lifecycle, and mirror
            // CreatedAt into PublishedAt (the surveys went live when they were created).
            migrationBuilder.Sql(@"
                UPDATE surveys
                SET status = 1,                -- SurveyStatus.Published
                    published_at = COALESCE(published_at, created_at)
                WHERE status = 0;              -- SurveyStatus.Draft (default value of the new column)
            ");

            migrationBuilder.AddPrimaryKey(
                name: "pk_surveys",
                table: "surveys",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_survey_questions",
                table: "survey_questions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_survey_answers",
                table: "survey_answers",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_survey_answers_surveys_survey_id",
                table: "survey_answers",
                column: "survey_id",
                principalTable: "surveys",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_survey_questions_surveys_survey_id",
                table: "survey_questions",
                column: "survey_id",
                principalTable: "surveys",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_surveys_publishers_publisher_id",
                table: "surveys",
                column: "publisher_id",
                principalTable: "publishers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_survey_answers_surveys_survey_id",
                table: "survey_answers");

            migrationBuilder.DropForeignKey(
                name: "fk_survey_questions_surveys_survey_id",
                table: "survey_questions");

            migrationBuilder.DropForeignKey(
                name: "fk_surveys_publishers_publisher_id",
                table: "surveys");

            migrationBuilder.DropPrimaryKey(
                name: "pk_surveys",
                table: "surveys");

            migrationBuilder.DropPrimaryKey(
                name: "pk_survey_questions",
                table: "survey_questions");

            migrationBuilder.DropPrimaryKey(
                name: "pk_survey_answers",
                table: "survey_answers");

            migrationBuilder.DropColumn(
                name: "attachments",
                table: "surveys");

            migrationBuilder.DropColumn(
                name: "notify_subscribers",
                table: "surveys");

            migrationBuilder.DropColumn(
                name: "published_at",
                table: "surveys");

            migrationBuilder.DropColumn(
                name: "status",
                table: "surveys");

            migrationBuilder.DropColumn(
                name: "attachments",
                table: "survey_questions");

            migrationBuilder.DropColumn(
                name: "max_length",
                table: "survey_questions");

            migrationBuilder.DropColumn(
                name: "max_selections",
                table: "survey_questions");

            migrationBuilder.DropColumn(
                name: "max_value",
                table: "survey_questions");

            migrationBuilder.DropColumn(
                name: "min_value",
                table: "survey_questions");

            migrationBuilder.RenameTable(
                name: "surveys",
                newName: "polls");

            migrationBuilder.RenameTable(
                name: "survey_questions",
                newName: "poll_questions");

            migrationBuilder.RenameTable(
                name: "survey_answers",
                newName: "poll_answers");

            migrationBuilder.RenameIndex(
                name: "ix_surveys_publisher_id",
                table: "polls",
                newName: "ix_polls_publisher_id");

            migrationBuilder.RenameColumn(
                name: "survey_id",
                table: "poll_questions",
                newName: "poll_id");

            migrationBuilder.RenameIndex(
                name: "ix_survey_questions_survey_id",
                table: "poll_questions",
                newName: "ix_poll_questions_poll_id");

            migrationBuilder.RenameColumn(
                name: "survey_id",
                table: "poll_answers",
                newName: "poll_id");

            migrationBuilder.RenameIndex(
                name: "ix_survey_answers_survey_id",
                table: "poll_answers",
                newName: "ix_poll_answers_poll_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_polls",
                table: "polls",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_poll_questions",
                table: "poll_questions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_poll_answers",
                table: "poll_answers",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_poll_answers_polls_poll_id",
                table: "poll_answers",
                column: "poll_id",
                principalTable: "polls",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_poll_questions_polls_poll_id",
                table: "poll_questions",
                column: "poll_id",
                principalTable: "polls",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_polls_publishers_publisher_id",
                table: "polls",
                column: "publisher_id",
                principalTable: "publishers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
