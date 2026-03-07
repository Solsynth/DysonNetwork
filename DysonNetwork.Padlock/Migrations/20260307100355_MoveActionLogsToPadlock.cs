using System;
using System.Collections.Generic;
using DysonNetwork.Shared.Geometry;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace DysonNetwork.Padlock.Migrations
{
    /// <inheritdoc />
    public partial class MoveActionLogsToPadlock : Migration
    {
        private static void EnsureAccountsPrimaryKey(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('public.accounts') IS NULL THEN
                        RAISE EXCEPTION 'Table public.accounts does not exist.';
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conrelid = 'public.accounts'::regclass
                          AND contype = 'p'
                    ) THEN
                        IF EXISTS (
                            SELECT id
                            FROM public.accounts
                            GROUP BY id
                            HAVING COUNT(*) > 1
                        ) THEN
                            RAISE EXCEPTION 'Cannot add primary key to public.accounts(id): duplicate ids exist.';
                        END IF;

                        ALTER TABLE public.accounts
                            ADD CONSTRAINT pk_accounts PRIMARY KEY (id);
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            EnsureAccountsPrimaryKey(migrationBuilder);

            migrationBuilder.CreateTable(
                name: "action_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    meta = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    location = table.Column<GeoPoint>(type: "jsonb", nullable: true),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_action_logs_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_action_logs_account_id",
                table: "action_logs",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "action_logs");
        }
    }
}
