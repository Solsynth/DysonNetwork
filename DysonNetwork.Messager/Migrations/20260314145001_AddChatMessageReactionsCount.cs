using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Messager.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMessageReactionsCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Dictionary<string, int>>(
                name: "reactions_count",
                table: "chat_messages",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.Sql("""
                UPDATE chat_messages AS m
                SET reactions_count = aggregated.reactions_count
                FROM (
                    SELECT grouped.message_id, jsonb_object_agg(grouped.symbol, grouped.reaction_count) AS reactions_count
                    FROM (
                        SELECT message_id, symbol, COUNT(*) AS reaction_count
                        FROM chat_reactions
                        GROUP BY message_id, symbol
                    ) AS grouped
                    GROUP BY grouped.message_id
                ) AS aggregated
                WHERE m.id = aggregated.message_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reactions_count",
                table: "chat_messages");
        }
    }
}
