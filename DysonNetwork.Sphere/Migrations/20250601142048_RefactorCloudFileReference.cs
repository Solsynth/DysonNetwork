using System;
using System.Collections.Generic;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class RefactorCloudFileReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_account_profiles_files_background_id",
                table: "account_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_account_profiles_files_picture_id",
                table: "account_profiles");

            migrationBuilder.DropForeignKey(
                name: "fk_chat_rooms_files_background_id",
                table: "chat_rooms");

            migrationBuilder.DropForeignKey(
                name: "fk_chat_rooms_files_picture_id",
                table: "chat_rooms");

            migrationBuilder.DropForeignKey(
                name: "fk_posts_posts_threaded_post_id",
                table: "posts");

            migrationBuilder.DropForeignKey(
                name: "fk_publishers_files_background_id",
                table: "publishers");

            migrationBuilder.DropForeignKey(
                name: "fk_publishers_files_picture_id",
                table: "publishers");

            migrationBuilder.DropForeignKey(
                name: "fk_realms_files_background_id",
                table: "realms");

            migrationBuilder.DropForeignKey(
                name: "fk_realms_files_picture_id",
                table: "realms");

            migrationBuilder.DropForeignKey(
                name: "fk_stickers_files_image_id",
                table: "stickers");

            migrationBuilder.DropIndex(
                name: "ix_stickers_image_id",
                table: "stickers");

            migrationBuilder.DropIndex(
                name: "ix_realms_background_id",
                table: "realms");

            migrationBuilder.DropIndex(
                name: "ix_realms_picture_id",
                table: "realms");

            migrationBuilder.DropIndex(
                name: "ix_publishers_background_id",
                table: "publishers");

            migrationBuilder.DropIndex(
                name: "ix_publishers_picture_id",
                table: "publishers");

            migrationBuilder.DropIndex(
                name: "ix_posts_threaded_post_id",
                table: "posts");

            migrationBuilder.DropIndex(
                name: "ix_chat_rooms_background_id",
                table: "chat_rooms");

            migrationBuilder.DropIndex(
                name: "ix_chat_rooms_picture_id",
                table: "chat_rooms");

            migrationBuilder.DropIndex(
                name: "ix_account_profiles_background_id",
                table: "account_profiles");

            migrationBuilder.DropIndex(
                name: "ix_account_profiles_picture_id",
                table: "account_profiles");

            migrationBuilder.DropColumn(
                name: "threaded_post_id",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "expired_at",
                table: "files");

            migrationBuilder.DropColumn(
                name: "usage",
                table: "files");

            migrationBuilder.DropColumn(
                name: "used_count",
                table: "files");

            migrationBuilder.AlterColumn<string>(
                name: "image_id",
                table: "stickers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<CloudFileReferenceObject>(
                name: "image",
                table: "stickers",
                type: "jsonb",
                nullable: true,
                defaultValueSql: "'[]'::jsonb"
                );

            migrationBuilder.AddColumn<CloudFileReferenceObject>(
                name: "background",
                table: "realms",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<CloudFileReferenceObject>(
                name: "picture",
                table: "realms",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<CloudFileReferenceObject>(
                name: "background",
                table: "publishers",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<CloudFileReferenceObject>(
                name: "picture",
                table: "publishers",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<List<CloudFileReferenceObject>>(
                name: "attachments",
                table: "posts",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb"
                );

            migrationBuilder.AddColumn<CloudFileReferenceObject>(
                name: "background",
                table: "chat_rooms",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<CloudFileReferenceObject>(
                name: "picture",
                table: "chat_rooms",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<List<CloudFileReferenceObject>>(
                name: "attachments",
                table: "chat_messages",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb"
                );

            migrationBuilder.AddColumn<CloudFileReferenceObject>(
                name: "background",
                table: "account_profiles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<CloudFileReferenceObject>(
                name: "picture",
                table: "account_profiles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "file_references",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    usage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_references", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_references_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_file_references_file_id",
                table: "file_references",
                column: "file_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_references");

            migrationBuilder.DropColumn(
                name: "image",
                table: "stickers");

            migrationBuilder.DropColumn(
                name: "background",
                table: "realms");

            migrationBuilder.DropColumn(
                name: "picture",
                table: "realms");

            migrationBuilder.DropColumn(
                name: "background",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "picture",
                table: "publishers");

            migrationBuilder.DropColumn(
                name: "attachments",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "background",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "picture",
                table: "chat_rooms");

            migrationBuilder.DropColumn(
                name: "attachments",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "background",
                table: "account_profiles");

            migrationBuilder.DropColumn(
                name: "picture",
                table: "account_profiles");

            migrationBuilder.AlterColumn<string>(
                name: "image_id",
                table: "stickers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "threaded_post_id",
                table: "posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "expired_at",
                table: "files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "usage",
                table: "files",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "used_count",
                table: "files",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_stickers_image_id",
                table: "stickers",
                column: "image_id");

            migrationBuilder.CreateIndex(
                name: "ix_realms_background_id",
                table: "realms",
                column: "background_id");

            migrationBuilder.CreateIndex(
                name: "ix_realms_picture_id",
                table: "realms",
                column: "picture_id");

            migrationBuilder.CreateIndex(
                name: "ix_publishers_background_id",
                table: "publishers",
                column: "background_id");

            migrationBuilder.CreateIndex(
                name: "ix_publishers_picture_id",
                table: "publishers",
                column: "picture_id");

            migrationBuilder.CreateIndex(
                name: "ix_posts_threaded_post_id",
                table: "posts",
                column: "threaded_post_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_rooms_background_id",
                table: "chat_rooms",
                column: "background_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_rooms_picture_id",
                table: "chat_rooms",
                column: "picture_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_profiles_background_id",
                table: "account_profiles",
                column: "background_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_profiles_picture_id",
                table: "account_profiles",
                column: "picture_id");

            migrationBuilder.AddForeignKey(
                name: "fk_account_profiles_files_background_id",
                table: "account_profiles",
                column: "background_id",
                principalTable: "files",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_account_profiles_files_picture_id",
                table: "account_profiles",
                column: "picture_id",
                principalTable: "files",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_rooms_files_background_id",
                table: "chat_rooms",
                column: "background_id",
                principalTable: "files",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_chat_rooms_files_picture_id",
                table: "chat_rooms",
                column: "picture_id",
                principalTable: "files",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_posts_posts_threaded_post_id",
                table: "posts",
                column: "threaded_post_id",
                principalTable: "posts",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_publishers_files_background_id",
                table: "publishers",
                column: "background_id",
                principalTable: "files",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_publishers_files_picture_id",
                table: "publishers",
                column: "picture_id",
                principalTable: "files",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_realms_files_background_id",
                table: "realms",
                column: "background_id",
                principalTable: "files",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_realms_files_picture_id",
                table: "realms",
                column: "picture_id",
                principalTable: "files",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_stickers_files_image_id",
                table: "stickers",
                column: "image_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
