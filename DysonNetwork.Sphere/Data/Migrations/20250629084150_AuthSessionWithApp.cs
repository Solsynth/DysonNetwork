using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuthSessionWithApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "app_id",
                table: "auth_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_auth_sessions_app_id",
                table: "auth_sessions",
                column: "app_id");

            migrationBuilder.AddForeignKey(
                name: "fk_auth_sessions_custom_apps_app_id",
                table: "auth_sessions",
                column: "app_id",
                principalTable: "custom_apps",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_auth_sessions_custom_apps_app_id",
                table: "auth_sessions");

            migrationBuilder.DropIndex(
                name: "ix_auth_sessions_app_id",
                table: "auth_sessions");

            migrationBuilder.DropColumn(
                name: "app_id",
                table: "auth_sessions");
        }
    }
}
