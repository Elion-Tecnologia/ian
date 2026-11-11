using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PopularCatalogoTiposServico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_tipos_servico_Nome",
                table: "tipos_servico",
                column: "Nome",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO public.tipos_servico
                    ("Id", "DuracaoPadrao", "ModalidadePadrao", "Nome", "UnidadeDuracaoPadrao")
                VALUES
                    (1, 240, 'Externo', 'Visita técnica', 'Minutos'),
                    (2, 7, 'Externo', 'Montagem', 'DiasCorridos'),
                    (3, 7, 'Externo', 'Ajuste', 'DiasCorridos'),
                    (4, 1, 'Interno', 'Orçamento', 'DiasCorridos');

                DO $sequence$
                DECLARE
                    current_value bigint;
                    current_is_called boolean;
                    current_next bigint;
                    minimum_next bigint;
                BEGIN
                    SELECT last_value, is_called
                    INTO current_value, current_is_called
                    FROM public."tipos_servico_Id_seq";

                    SELECT COALESCE(MAX("Id"), 0) + 1
                    INTO minimum_next
                    FROM public.tipos_servico;

                    current_next := CASE
                        WHEN current_is_called THEN current_value + 1
                        ELSE current_value
                    END;

                    IF current_next < minimum_next THEN
                        PERFORM setval(
                            pg_get_serial_sequence('public.tipos_servico', 'Id'),
                            minimum_next,
                            false);
                    END IF;
                END
                $sequence$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "O catálogo fixo pode estar referenciado por agendamentos e não permite rollback automático.");
        }
    }
}
