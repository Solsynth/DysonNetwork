using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUploadTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bundle_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "chunk_size",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "chunks_count",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "chunks_uploaded",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "content_type",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "discriminator",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "encrypt_password",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "file_name",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "file_size",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "hash",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "path",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "pool_id",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "uploaded_chunks",
                table: "tasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bundle_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "chunk_size",
                table: "tasks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "chunks_count",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "chunks_uploaded",
                table: "tasks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_type",
                table: "tasks",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "discriminator",
                table: "tasks",
                type: "character varying(21)",
                maxLength: 21,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "encrypt_password",
                table: "tasks",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "file_name",
                table: "tasks",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "file_size",
                table: "tasks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hash",
                table: "tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "path",
                table: "tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pool_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<List<int>>(
                name: "uploaded_chunks",
                table: "tasks",
                type: "integer[]",
                nullable: true);
        }
    }
}
