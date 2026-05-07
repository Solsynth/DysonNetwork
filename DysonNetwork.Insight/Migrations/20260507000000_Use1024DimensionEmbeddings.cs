using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class Use1024DimensionEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE memory_records SET embedding = NULL WHERE embedding IS NOT NULL;");
            migrationBuilder.Sql("UPDATE sn_doc_chunks SET embedding = NULL WHERE embedding IS NOT NULL;");
            migrationBuilder.Sql("UPDATE thinking_sequences SET summary_embedding = NULL WHERE summary_embedding IS NOT NULL;");

            migrationBuilder.AlterColumn<Vector>(
                name: "embedding",
                table: "memory_records",
                type: "vector(1024)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(1536)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Vector>(
                name: "embedding",
                table: "sn_doc_chunks",
                type: "vector(1024)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(1536)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Vector>(
                name: "summary_embedding",
                table: "thinking_sequences",
                type: "vector(1024)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(1536)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE memory_records SET embedding = NULL WHERE embedding IS NOT NULL;");
            migrationBuilder.Sql("UPDATE sn_doc_chunks SET embedding = NULL WHERE embedding IS NOT NULL;");
            migrationBuilder.Sql("UPDATE thinking_sequences SET summary_embedding = NULL WHERE summary_embedding IS NOT NULL;");

            migrationBuilder.AlterColumn<Vector>(
                name: "embedding",
                table: "memory_records",
                type: "vector(1536)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(1024)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Vector>(
                name: "embedding",
                table: "sn_doc_chunks",
                type: "vector(1536)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(1024)",
                oldNullable: true);

            migrationBuilder.AlterColumn<Vector>(
                name: "summary_embedding",
                table: "thinking_sequences",
                type: "vector(1536)",
                nullable: true,
                oldClrType: typeof(Vector),
                oldType: "vector(1024)",
                oldNullable: true);
        }
    }
}
