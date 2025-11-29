using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Pass.Migrations
{
    /// <inheritdoc />
    public partial class DecoupleAuthSessionAndChallenge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_auth_challenges_auth_clients_client_id",
                table: "auth_challenges");

            migrationBuilder.DropIndex(
                name: "ix_auth_challenges_client_id",
                table: "auth_challenges");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "auth_challenges");

            migrationBuilder.AddColumn<Guid>(
                name: "client_id",
                table: "auth_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_session_id",
                table: "auth_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "device_id",
                table: "auth_challenges",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "device_name",
                table: "auth_challenges",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "platform",
                table: "auth_challenges",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_client_id",
                table: "auth_sessions",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_parent_session_id",
                table: "auth_sessions",
                column: "parent_session_id");

            migrationBuilder.AddForeignKey(
                name: "fk_auth_sessions_auth_clients_client_id",
                table: "auth_sessions",
                column: "client_id",
                principalTable: "auth_clients",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_auth_sessions_auth_sessions_parent_session_id",
                table: "auth_sessions",
                column: "parent_session_id",
                principalTable: "auth_sessions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_auth_sessions_auth_clients_client_id",
                table: "auth_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_auth_sessions_auth_sessions_parent_session_id",
                table: "auth_sessions");

            migrationBuilder.DropIndex(
                name: "ix_auth_sessions_client_id",
                table: "auth_sessions");

            migrationBuilder.DropIndex(
                name: "ix_auth_sessions_parent_session_id",
                table: "auth_sessions");

            migrationBuilder.DropColumn(
                name: "client_id",
                table: "auth_sessions");

            migrationBuilder.DropColumn(
                name: "parent_session_id",
                table: "auth_sessions");

            migrationBuilder.DropColumn(
                name: "device_id",
                table: "auth_challenges");

            migrationBuilder.DropColumn(
                name: "device_name",
                table: "auth_challenges");

            migrationBuilder.DropColumn(
                name: "platform",
                table: "auth_challenges");

            migrationBuilder.AddColumn<Guid>(
                name: "client_id",
                table: "auth_challenges",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_auth_challenges_client_id",
                table: "auth_challenges",
                column: "client_id");

            migrationBuilder.AddForeignKey(
                name: "fk_auth_challenges_auth_clients_client_id",
                table: "auth_challenges",
                column: "client_id",
                principalTable: "auth_clients",
                principalColumn: "id");
        }
    }
}
