using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Passport.Migrations
{
    /// <inheritdoc />
    public partial class UpdateNfcTag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_nfc_tags_user_id",
                table: "nfc_tags");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "nfc_tags");

            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                table: "nfc_tags",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_nfc_tags_account_id",
                table: "nfc_tags",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_nfc_tags_account_id",
                table: "nfc_tags");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "nfc_tags");

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "nfc_tags",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_nfc_tags_user_id",
                table: "nfc_tags",
                column: "user_id");
        }
    }
}
