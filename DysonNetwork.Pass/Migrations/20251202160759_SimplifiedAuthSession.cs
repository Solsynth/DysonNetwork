using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class SimplifiedAuthSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_auth_sessions_auth_challenges_challenge_id",
                table: "auth_sessions");

            migrationBuilder.DropIndex(
                name: "ix_auth_sessions_challenge_id",
                table: "auth_sessions");

            migrationBuilder.DropColumn(
                name: "type",
                table: "auth_challenges");

            migrationBuilder.AddColumn<List<string>>(
                name: "audiences",
                table: "auth_sessions",
                type: "jsonb",
                nullable: false,
                defaultValue: new List<string>());

            migrationBuilder.AddColumn<string>(
                name: "ip_address",
                table: "auth_sessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "scopes",
                table: "auth_sessions",
                type: "jsonb",
                nullable: false,
                defaultValue: new List<string>());

            migrationBuilder.AddColumn<int>(
                name: "type",
                table: "auth_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "user_agent",
                table: "auth_sessions",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "audiences",
                table: "auth_sessions");

            migrationBuilder.DropColumn(
                name: "ip_address",
                table: "auth_sessions");

            migrationBuilder.DropColumn(
                name: "scopes",
                table: "auth_sessions");

            migrationBuilder.DropColumn(
                name: "type",
                table: "auth_sessions");

            migrationBuilder.DropColumn(
                name: "user_agent",
                table: "auth_sessions");

            migrationBuilder.AddColumn<int>(
                name: "type",
                table: "auth_challenges",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_challenge_id",
                table: "auth_sessions",
                column: "challenge_id");

            migrationBuilder.AddForeignKey(
                name: "fk_auth_sessions_auth_challenges_challenge_id",
                table: "auth_sessions",
                column: "challenge_id",
                principalTable: "auth_challenges",
                principalColumn: "id");
        }
    }
}
