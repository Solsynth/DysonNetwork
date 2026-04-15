using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class AddPunishmentCreator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "creator_id",
                table: "punishments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_punishments_creator_id",
                table: "punishments",
                column: "creator_id");

            migrationBuilder.AddForeignKey(
                name: "fk_punishments_accounts_creator_id",
                table: "punishments",
                column: "creator_id",
                principalTable: "accounts",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_punishments_accounts_creator_id",
                table: "punishments");

            migrationBuilder.DropIndex(
                name: "ix_punishments_creator_id",
                table: "punishments");

            migrationBuilder.DropColumn(
                name: "creator_id",
                table: "punishments");
        }
    }
}
