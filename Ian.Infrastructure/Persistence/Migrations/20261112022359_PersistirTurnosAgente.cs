using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistirTurnosAgente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telegram_agent_turns",
                columns: table => new
                {
                    UpdateId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    CommandJson = table.Column<string>(type: "jsonb", nullable: true),
                    ResponseText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SendState = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SendAttempts = table.Column<int>(type: "integer", nullable: false),
                    TelegramMessageId = table.Column<long>(type: "bigint", nullable: true),
                    SendStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_agent_turns", x => x.UpdateId);
                    table.CheckConstraint("ck_agent_turn_send_attempts", "\"SendAttempts\" >= 0 AND \"SendAttempts\" <= 3");
                    table.CheckConstraint("ck_agent_turn_send_state", "\"SendState\" IN ('Pending','Sending','Accepted','Failed','Uncertain')");
                    table.ForeignKey(
                        name: "FK_telegram_agent_turns_telegram_updates_inbox_UpdateId",
                        column: x => x.UpdateId,
                        principalTable: "telegram_updates_inbox",
                        principalColumn: "UpdateId",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM telegram_agent_turns) THEN
                        RAISE EXCEPTION 'Rollback recusado: existem turnos persistidos para recuperação.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "telegram_agent_turns");
        }
    }
}
