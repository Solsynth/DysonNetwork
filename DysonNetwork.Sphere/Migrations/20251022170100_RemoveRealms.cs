using System;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRealms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chat_rooms_realms_sn_realm_id",
                table: "chat_rooms");

            migrationBuilder.DropForeignKey(
                name: "fk_publishers_realms_realm_id",
                table: "publishers");

            migrationBuilder.DropTable(
                name: "realm_members");

            migrationBuilder.DropTable(
                name: "realms");

            migrationBuilder.DropIndex(
                name: "ix_publishers_realm_id",
                table: "publishers");

            migrationBuilder.DropIndex(
                name: "ix_chat_rooms_sn_realm_id",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "sn_realm_id",
                table: "chat_rooms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "sn_realm_id",
                table: "chat_rooms",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "realms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    background = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    background_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    is_community = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    picture = table.Column<SnCloudFileReferenceObject>(type: "jsonb", nullable: true),
                    picture_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    slug = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    verification = table.Column<SnVerificationMark>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "realm_members",
                columns: table => new
                {
                    realm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    joined_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    leave_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    role = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_realm_members", x => new { x.realm_id, x.account_id });
                    table.ForeignKey(
                        name: "fk_realm_members_realms_realm_id",
                        column: x => x.realm_id,
                        principalTable: "realms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_publishers_realm_id",
                table: "publishers",
                column: "realm_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_rooms_sn_realm_id",
                table: "chat_rooms",
                column: "sn_realm_id");

            migrationBuilder.CreateIndex(
                name: "ix_realms_slug",
                table: "realms",
                column: "slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_chat_rooms_realms_sn_realm_id",
                table: "chat_rooms",
                column: "sn_realm_id",
                principalTable: "realms",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_publishers_realms_realm_id",
                table: "publishers",
                column: "realm_id",
                principalTable: "realms",
                principalColumn: "id");
        }
    }
}
