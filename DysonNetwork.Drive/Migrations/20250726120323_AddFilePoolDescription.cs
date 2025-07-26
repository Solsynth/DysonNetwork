using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Drive.Migrations
{
    /// <inheritdoc />
    public partial class AddFilePoolDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "pools",
                type: "character varying(8192)",
                maxLength: 8192,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "description",
                table: "pools");
        }
    }
}
