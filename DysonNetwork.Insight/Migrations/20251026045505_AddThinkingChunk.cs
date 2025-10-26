using System.Collections.Generic;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class AddThinkingChunk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<SnThinkingChunk>>(
                name: "chunks",
                table: "thinking_thoughts",
                type: "jsonb",
                nullable: false,
                defaultValue: new List<SnThinkingChunk>()
                );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chunks",
                table: "thinking_thoughts");
        }
    }
}
