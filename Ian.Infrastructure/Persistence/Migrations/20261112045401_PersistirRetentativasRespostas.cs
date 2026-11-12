using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistirRetentativasRespostas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogicalReplyKey",
                table: "telegram_agent_turns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextSendAttemptAtUtc",
                table: "telegram_agent_turns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SendLeaseUntilUtc",
                table: "telegram_agent_turns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SendToken",
                table: "telegram_agent_turns",
                type: "uuid",
                nullable: true);

            // Preserve existing replies; only definitely rejected sends may be scheduled again.
            migrationBuilder.Sql("""
                UPDATE telegram_agent_turns
                SET "LogicalReplyKey" = 'private-reply:' || "UpdateId"::text || ':' || "TelegramUserId"::text;
                UPDATE telegram_agent_turns SET "NextSendAttemptAtUtc" = now()
                WHERE "ResponseText" IS NOT NULL AND
                    ("SendState" = 'Pending' OR ("SendState" = 'Failed' AND "SendStatus" = 'RateLimited' AND "SendAttempts" < 3));
                UPDATE telegram_agent_turns SET "SendState" = 'Uncertain', "SendStatus" = 'LegacyInterrupted'
                WHERE "SendState" = 'Sending';
                """);
            migrationBuilder.AlterColumn<string>(
                name: "LogicalReplyKey", table: "telegram_agent_turns", type: "character varying(100)",
                maxLength: 100, nullable: false, oldClrType: typeof(string),
                oldType: "character varying(100)", oldMaxLength: 100, oldNullable: true);

            migrationBuilder.CreateTable(
                name: "telegram_reply_attempts",
                columns: table => new
                {
                    UpdateId = table.Column<long>(type: "bigint", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TransportAttempts = table.Column<int>(type: "integer", nullable: false),
                    DeliveryUncertain = table.Column<bool>(type: "boolean", nullable: false),
                    TelegramMessageId = table.Column<long>(type: "bigint", nullable: true),
                    RetryAfterSeconds = table.Column<long>(type: "bigint", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_reply_attempts", x => new { x.UpdateId, x.Number });
                    table.CheckConstraint("ck_reply_attempt_number", "\"Number\" BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_reply_transport_attempts", "\"TransportAttempts\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_telegram_reply_attempts_telegram_agent_turns_UpdateId",
                        column: x => x.UpdateId,
                        principalTable: "telegram_agent_turns",
                        principalColumn: "UpdateId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_agent_turns_LogicalReplyKey",
                table: "telegram_agent_turns",
                column: "LogicalReplyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_agent_turns_NextSendAttemptAtUtc",
                table: "telegram_agent_turns",
                column: "NextSendAttemptAtUtc",
                filter: "\"NextSendAttemptAtUtc\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM telegram_reply_attempts) OR
                        EXISTS (SELECT 1 FROM telegram_agent_turns WHERE "ResponseText" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Rollback recusado: existem respostas ou resultados de envio persistidos.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "telegram_reply_attempts");

            migrationBuilder.DropIndex(
                name: "IX_telegram_agent_turns_LogicalReplyKey",
                table: "telegram_agent_turns");

            migrationBuilder.DropIndex(
                name: "IX_telegram_agent_turns_NextSendAttemptAtUtc",
                table: "telegram_agent_turns");

            migrationBuilder.DropColumn(
                name: "LogicalReplyKey",
                table: "telegram_agent_turns");

            migrationBuilder.DropColumn(
                name: "NextSendAttemptAtUtc",
                table: "telegram_agent_turns");

            migrationBuilder.DropColumn(
                name: "SendLeaseUntilUtc",
                table: "telegram_agent_turns");

            migrationBuilder.DropColumn(
                name: "SendToken",
                table: "telegram_agent_turns");
        }
    }
}
