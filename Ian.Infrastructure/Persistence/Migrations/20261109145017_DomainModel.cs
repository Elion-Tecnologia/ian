using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DomainModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    TelefoneNormalizado = table.Column<string>(type: "text", nullable: false),
                    EnderecoPrincipal = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientes", x => x.Id);
                    table.CheckConstraint("ck_clientes_telefone_preenchido", "length(btrim(\"TelefoneNormalizado\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "envios_telegram",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DestinoChatId = table.Column<long>(type: "bigint", nullable: false),
                    TipoMensagem = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Estado = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    CriadoEmUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UltimaTentativaEmUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TelegramMessageId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_envios_telegram", x => x.Id);
                    table.CheckConstraint("ck_envios_telegram_estado", " \"Estado\" IN ('Pendente', 'Enviado', 'Falhou') ");
                    table.CheckConstraint("ck_envios_telegram_tentativas", " \"Tentativas\" >= 0 ");
                });

            migrationBuilder.CreateTable(
                name: "equipes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_equipes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "solicitacoes_pendentes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    Operacao = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Estado = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CriadaEmUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiraEmUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_solicitacoes_pendentes", x => x.Id);
                    table.CheckConstraint("ck_solicitacoes_estado", " \"Estado\" IN ('Pendente', 'Confirmada', 'Cancelada', 'Expirada') ");
                    table.CheckConstraint("ck_solicitacoes_expiracao", " \"CriadaEmUtc\" < \"ExpiraEmUtc\" ");
                });

            migrationBuilder.CreateTable(
                name: "tipos_servico",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    ModalidadePadrao = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DuracaoPadrao = table.Column<int>(type: "integer", nullable: false),
                    UnidadeDuracaoPadrao = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tipos_servico", x => x.Id);
                    table.CheckConstraint("ck_tipos_servico_duracao_positiva", " \"DuracaoPadrao\" > 0 ");
                    table.CheckConstraint("ck_tipos_servico_modalidade", " \"ModalidadePadrao\" IN ('Interno', 'Externo', 'Remoto') ");
                    table.CheckConstraint("ck_tipos_servico_unidade_duracao", " \"UnidadeDuracaoPadrao\" IN ('Minutos', 'DiasCorridos') ");
                });

            migrationBuilder.CreateTable(
                name: "bloqueios_agenda",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EquipeId = table.Column<long>(type: "bigint", nullable: true),
                    InicioUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FimUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bloqueios_agenda", x => x.Id);
                    table.CheckConstraint("ck_bloqueios_agenda_intervalo", " \"InicioUtc\" < \"FimUtc\" ");
                    table.ForeignKey(
                        name: "FK_bloqueios_agenda_equipes_EquipeId",
                        column: x => x.EquipeId,
                        principalTable: "equipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "excecoes_expediente",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EquipeId = table.Column<long>(type: "bigint", nullable: false),
                    DataLocal = table.Column<DateOnly>(type: "date", nullable: false),
                    FolgaIntegral = table.Column<bool>(type: "boolean", nullable: false),
                    InicioLocal = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    FimLocal = table.Column<TimeOnly>(type: "time without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_excecoes_expediente", x => x.Id);
                    table.CheckConstraint("ck_excecoes_expediente_jornada", "(\"FolgaIntegral\" AND \"InicioLocal\" IS NULL AND \"FimLocal\" IS NULL)\r\nOR\r\n(NOT \"FolgaIntegral\" AND \"InicioLocal\" IS NOT NULL AND \"FimLocal\" IS NOT NULL\r\n AND \"InicioLocal\" < \"FimLocal\")");
                    table.ForeignKey(
                        name: "FK_excecoes_expediente_equipes_EquipeId",
                        column: x => x.EquipeId,
                        principalTable: "equipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "agendamentos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClienteId = table.Column<long>(type: "bigint", nullable: false),
                    EquipeId = table.Column<long>(type: "bigint", nullable: false),
                    TipoServicoId = table.Column<long>(type: "bigint", nullable: false),
                    Modalidade = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Duracao = table.Column<int>(type: "integer", nullable: false),
                    UnidadeDuracao = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InicioPrevistoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FimPrevistoUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EnderecoSnapshot = table.Column<string>(type: "text", nullable: true),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    Estado = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agendamentos", x => x.Id);
                    table.CheckConstraint("ck_agendamentos_duracao", " \"Duracao\" > 0 ");
                    table.CheckConstraint("ck_agendamentos_endereco_externo", "\"Modalidade\" <> 'Externo'\r\nOR (\"EnderecoSnapshot\" IS NOT NULL AND length(btrim(\"EnderecoSnapshot\")) > 0)");
                    table.CheckConstraint("ck_agendamentos_estado", " \"Estado\" IN ('Agendado', 'Concluido', 'Cancelado') ");
                    table.CheckConstraint("ck_agendamentos_modalidade", " \"Modalidade\" IN ('Interno', 'Externo', 'Remoto') ");
                    table.CheckConstraint("ck_agendamentos_periodo", " \"InicioPrevistoUtc\" < \"FimPrevistoUtc\" ");
                    table.CheckConstraint("ck_agendamentos_unidade", " \"UnidadeDuracao\" IN ('Minutos', 'DiasCorridos') ");
                    table.ForeignKey(
                        name: "FK_agendamentos_clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agendamentos_equipes_EquipeId",
                        column: x => x.EquipeId,
                        principalTable: "equipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_agendamentos_tipos_servico_TipoServicoId",
                        column: x => x.TipoServicoId,
                        principalTable: "tipos_servico",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "periodos_ocupacao",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AgendamentoId = table.Column<long>(type: "bigint", nullable: false),
                    InicioUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FimUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_periodos_ocupacao", x => x.Id);
                    table.CheckConstraint("ck_periodos_ocupacao_intervalo", " \"InicioUtc\" < \"FimUtc\" ");
                    table.ForeignKey(
                        name: "FK_periodos_ocupacao_agendamentos_AgendamentoId",
                        column: x => x.AgendamentoId,
                        principalTable: "agendamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "registros_auditoria",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AgendamentoId = table.Column<long>(type: "bigint", nullable: true),
                    Operacao = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Resultado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CorrelacaoId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RegistradoEmUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registros_auditoria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_registros_auditoria_agendamentos_AgendamentoId",
                        column: x => x.AgendamentoId,
                        principalTable: "agendamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_ClienteId",
                table: "agendamentos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_EquipeId_Estado",
                table: "agendamentos",
                columns: new[] { "EquipeId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_agendamentos_TipoServicoId",
                table: "agendamentos",
                column: "TipoServicoId");

            migrationBuilder.CreateIndex(
                name: "IX_bloqueios_agenda_EquipeId_InicioUtc",
                table: "bloqueios_agenda",
                columns: new[] { "EquipeId", "InicioUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_clientes_TelefoneNormalizado",
                table: "clientes",
                column: "TelefoneNormalizado",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_envios_telegram_Estado_CriadoEmUtc",
                table: "envios_telegram",
                columns: new[] { "Estado", "CriadoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_excecoes_expediente_EquipeId_DataLocal",
                table: "excecoes_expediente",
                columns: new[] { "EquipeId", "DataLocal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_periodos_ocupacao_AgendamentoId_InicioUtc",
                table: "periodos_ocupacao",
                columns: new[] { "AgendamentoId", "InicioUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_registros_auditoria_AgendamentoId_RegistradoEmUtc",
                table: "registros_auditoria",
                columns: new[] { "AgendamentoId", "RegistradoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_registros_auditoria_CorrelacaoId",
                table: "registros_auditoria",
                column: "CorrelacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_solicitacoes_pendentes_TelegramUserId_Estado_ExpiraEmUtc",
                table: "solicitacoes_pendentes",
                columns: new[] { "TelegramUserId", "Estado", "ExpiraEmUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bloqueios_agenda");

            migrationBuilder.DropTable(
                name: "envios_telegram");

            migrationBuilder.DropTable(
                name: "excecoes_expediente");

            migrationBuilder.DropTable(
                name: "periodos_ocupacao");

            migrationBuilder.DropTable(
                name: "registros_auditoria");

            migrationBuilder.DropTable(
                name: "solicitacoes_pendentes");

            migrationBuilder.DropTable(
                name: "agendamentos");

            migrationBuilder.DropTable(
                name: "clientes");

            migrationBuilder.DropTable(
                name: "equipes");

            migrationBuilder.DropTable(
                name: "tipos_servico");
        }
    }
}
