using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TelegramUpdateInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telegram_updates_inbox",
                columns: table => new
                {
                    UpdateId = table.Column<long>(type: "bigint", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    RecebidoEmUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_updates_inbox", x => x.UpdateId);
                    table.CheckConstraint("ck_telegram_inbox_estado", "\"Estado\" IN ('Received')");
                    table.CheckConstraint("ck_telegram_inbox_update_id", "\"UpdateId\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_telegram_updates_inbox_Estado_RecebidoEmUtc",
                table: "telegram_updates_inbox",
                columns: new[] { "Estado", "RecebidoEmUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_updates_inbox");
        }
    }
}
