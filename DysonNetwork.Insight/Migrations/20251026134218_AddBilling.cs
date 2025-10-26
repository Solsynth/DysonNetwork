using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Insight.Migrations
{
    /// <inheritdoc />
    public partial class AddBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "model_name",
                table: "thinking_thoughts",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "token_count",
                table: "thinking_thoughts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "paid_token",
                table: "thinking_sequences",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "total_token",
                table: "thinking_sequences",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "model_name",
                table: "thinking_thoughts");

            migrationBuilder.DropColumn(
                name: "token_count",
                table: "thinking_thoughts");

            migrationBuilder.DropColumn(
                name: "paid_token",
                table: "thinking_sequences");

            migrationBuilder.DropColumn(
                name: "total_token",
                table: "thinking_sequences");
        }
    }
}
