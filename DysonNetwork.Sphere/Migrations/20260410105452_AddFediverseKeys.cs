using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddFediverseKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fediverse_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    key_pem = table.Column<string>(type: "TEXT", nullable: false),
                    private_key_pem = table.Column<string>(type: "TEXT", nullable: true),
                    publisher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    rotated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fediverse_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_fediverse_keys_fediverse_actors_actor_id",
                        column: x => x.actor_id,
                        principalTable: "fediverse_actors",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_keys_actor_id",
                table: "fediverse_keys",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_fediverse_keys_key_id",
                table: "fediverse_keys",
                column: "key_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fediverse_keys");
        }
    }
}
