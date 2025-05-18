using System.Collections.Generic;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeFileStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "image_id",
                table: "stickers",
                type: "character varying(32)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)");

            migrationBuilder.AlterColumn<string>(
                name: "picture_id",
                table: "realms",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "background_id",
                table: "realms",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "picture_id",
                table: "publishers",
                type: "character varying(32)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "background_id",
                table: "publishers",
                type: "character varying(32)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "files",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AddColumn<List<CloudFileSensitiveMark>>(
                name: "sensitive_marks",
                table: "files",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_id",
                table: "files",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "storage_url",
                table: "files",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "picture_id",
                table: "chat_rooms",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "background_id",
                table: "chat_rooms",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "picture_id",
                table: "account_profiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "background_id",
                table: "account_profiles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sensitive_marks",
                table: "files");

            migrationBuilder.DropColumn(
                name: "storage_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "storage_url",
                table: "files");

            migrationBuilder.AlterColumn<string>(
                name: "image_id",
                table: "stickers",
                type: "character varying(128)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)");

            migrationBuilder.AlterColumn<string>(
                name: "picture_id",
                table: "realms",
                type: "character varying(128)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "background_id",
                table: "realms",
                type: "character varying(128)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "picture_id",
                table: "publishers",
                type: "character varying(128)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "background_id",
                table: "publishers",
                type: "character varying(128)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "files",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "picture_id",
                table: "chat_rooms",
                type: "character varying(128)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "background_id",
                table: "chat_rooms",
                type: "character varying(128)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "picture_id",
                table: "account_profiles",
                type: "character varying(128)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "background_id",
                table: "account_profiles",
                type: "character varying(128)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);
        }
    }
}
