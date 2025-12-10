using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DysonNetwork.Zone.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteGlobalConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<PublicationSiteConfig>(
                name: "config",
                table: "publication_sites",
                type: "jsonb",
                nullable: false,
                defaultValue: new PublicationSiteConfig());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "config",
                table: "publication_sites");
        }
    }
}
