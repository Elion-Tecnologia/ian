using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PopularCatalogoTiposServico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "tipos_servico",
                columns: new[] { "Id", "DuracaoPadrao", "ModalidadePadrao", "Nome", "UnidadeDuracaoPadrao" },
                values: new object[,]
                {
                    { 1L, 240, "Externo", "Visita técnica", "Minutos" },
                    { 2L, 7, "Externo", "Montagem", "DiasCorridos" },
                    { 3L, 7, "Externo", "Ajuste", "DiasCorridos" },
                    { 4L, 1, "Interno", "Orçamento", "DiasCorridos" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_tipos_servico_Nome",
                table: "tipos_servico",
                column: "Nome",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "O catálogo fixo pode estar referenciado por agendamentos e não permite rollback automático.");
        }
    }
}
