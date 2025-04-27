using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Sphere.Migrations
{
    /// <inheritdoc />
    public partial class AddPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "topic",
                table: "notifications",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "permission_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permission_group_member",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    affected_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_group_member", x => new { x.group_id, x.account_id });
                    table.ForeignKey(
                        name: "fk_permission_group_member_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_permission_group_member_permission_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "permission_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permission_nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    area = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    value = table.Column<object>(type: "jsonb", nullable: false),
                    expired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    affected_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    permission_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_nodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_permission_nodes_permission_groups_permission_group_id",
                        column: x => x.permission_group_id,
                        principalTable: "permission_groups",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_permission_nodes_permission_nodes_group_id",
                        column: x => x.group_id,
                        principalTable: "permission_nodes",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_permission_group_member_account_id",
                table: "permission_group_member",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_permission_nodes_group_id",
                table: "permission_nodes",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_permission_nodes_key_area_actor",
                table: "permission_nodes",
                columns: new[] { "key", "area", "actor" });

            migrationBuilder.CreateIndex(
                name: "ix_permission_nodes_permission_group_id",
                table: "permission_nodes",
                column: "permission_group_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "permission_group_member");

            migrationBuilder.DropTable(
                name: "permission_nodes");

            migrationBuilder.DropTable(
                name: "permission_groups");

            migrationBuilder.DropColumn(
                name: "topic",
                table: "notifications");
        }
    }
}
