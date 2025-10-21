using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class ChangeRealmReferenceMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_rooms_realms_realm_id",
                table: "chat_rooms");

            migrationBuilder.DropForeignKey(
                name: "fk_posts_realms_realm_id",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_posts_realm_id",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_chat_rooms_realm_id",
                table: "chat_rooms");

            migrationBuilder.AddColumn<Guid>(
                name: "sn_realm_id",
                table: "chat_rooms",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_rooms_sn_realm_id",
                table: "chat_rooms",
                column: "sn_realm_id");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_rooms_realms_sn_realm_id",
                table: "chat_rooms",
                column: "sn_realm_id",
                principalTable: "realms",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_rooms_realms_sn_realm_id",
                table: "chat_rooms");

            migrationBuilder.DropIndex(
                name: "ix_chat_rooms_sn_realm_id",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "sn_realm_id",
                table: "chat_rooms");

            migrationBuilder.CreateIndex(
                name: "ix_posts_realm_id",
                table: "posts",
                column: "realm_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_rooms_realm_id",
                table: "chat_rooms",
                column: "realm_id");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_rooms_realms_realm_id",
                table: "chat_rooms",
                column: "realm_id",
                principalTable: "realms",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_posts_realms_realm_id",
                table: "posts",
                column: "realm_id",
                principalTable: "realms",
                principalColumn: "id");
        }
    }
}
