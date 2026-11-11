using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VincularOperacaoAoAgendamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AgendamentoId",
                table: "telegram_business_operations",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_telegram_business_operations_AgendamentoId",
                table: "telegram_business_operations",
                column: "AgendamentoId");

            migrationBuilder.AddForeignKey(
                name: "FK_telegram_business_operations_agendamentos_AgendamentoId",
                table: "telegram_business_operations",
                column: "AgendamentoId",
                principalTable: "agendamentos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "O vínculo idempotente com o agendamento confirmado não permite rollback automático.");
        }
    }
}
