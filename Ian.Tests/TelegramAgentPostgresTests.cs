using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ian.Application.Agenda;
using Ian.Application.AI;
using Ian.Application.Conversation;
using Ian.Application.Telegram;
using Ian.Domain.Agenda;
using Ian.Domain.Entities;
using Ian.Infrastructure.AI;
using Ian.Infrastructure.Persistence;
using Ian.Infrastructure.Telegram;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ian.Tests;

[Collection("Telegram Inbox PostgreSQL")]
[Trait("Category", "HomologacaoMvp")]
public sealed partial class TelegramAgentPostgresTests
{
    [PostgresFact]
    public async Task WebhookMafToolsPostgresETelegramCriamUmaReservaSomenteAposCommit()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar());
        f.Transport.BeforeReply = async _ =>
        {
            await using var check = PostgresTestDatabase.CreateContext();
            Assert.Single(await check.Agendamentos.ToListAsync());
            Assert.Equal(7, await check.PeriodosOcupacao.CountAsync());
            Assert.Single(await check.TelegramBusinessOperations.ToListAsync());
        };
        await f.EnqueueAsync(1, "Marque montagem de Ana por sete dias em 07/01/2030, endereço Rua A, telefone 11999999999.");
        Assert.Equal(0, f.Model.Calls); // Webhook only persists, then acknowledges.
        Assert.Empty(f.Transport.Replies);
        await f.ProcessAsync();
        Assert.Contains("Agendamento 1 criado", Assert.Single(f.Transport.Replies));
        Assert.DoesNotContain("PRIVATE_MODEL_CLAIM", f.Transport.Replies[0]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.All(await db.PeriodosOcupacao.ToListAsync(), p =>
        {
            Assert.Equal(11, p.InicioUtc.Hour);
            Assert.Equal(21, p.FimUtc.Hour);
        });
        Assert.Equal(EstadoTelegramUpdate.Completed, (await db.TelegramUpdatesInbox.SingleAsync()).Estado);
        Assert.Equal("Accepted", (await db.Set<TelegramAgentTurn>().SingleAsync()).SendState);
        await f.EnqueueAsync(1, "reentrega");
        Assert.False(await f.ProcessAsync());
        await f.HandleAgainAsync(1);
        Assert.Single(f.Transport.Replies);
        Assert.Single(await db.Agendamentos.ToListAsync());
        Assert.Equal(2, f.Model.Calls);
    }

    [PostgresFact]
    public async Task DadosFaltantesRetomamAposReinicioSemInventarClienteOuEndereco()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", tipoServicoId = 2L, data = "2030-01-07" });
        await f.SendAsync(1, "Marque montagem para Ana em 07/01/2030.");
        Assert.Contains("telefone", f.Transport.Replies[^1]);
        Assert.Contains("endereço", f.Transport.Replies[^1]);
        await using (var check = PostgresTestDatabase.CreateContext())
        {
            Assert.Empty(await check.Agendamentos.ToListAsync());
            Assert.Empty(await check.Clientes.ToListAsync());
            Assert.Contains("Ana", (await check.SolicitacoesPendentes.SingleAsync()).DadosColetadosJson);
        }
        f.Restart();
        f.Model.Tool("criar_agendamento", new { telefoneCliente = "11999999999", enderecoServico = "Rua A" });
        await f.SendAsync(2, "Telefone 11999999999, Rua A.");
        Assert.Contains("Agendamento 1 criado", f.Transport.Replies[^1]);
        Assert.Contains(f.Model.Prompts, p => p.Contains("Contexto persistido") && p.Contains("Ana"));
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Single(await db.Agendamentos.ToListAsync());
        Assert.Equal(7, await db.PeriodosOcupacao.CountAsync());
    }

    [PostgresFact]
    public async Task DiasCorridosAsDezExigeEsclarecimentoSemReservaEAsOitoCriaSeteJanelas()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(horario: "10:00"));
        await f.SendAsync(1, "Monte durante sete dias começando às 10h.");
        Assert.Contains("08h", f.Transport.Replies[^1]);
        await using (var check = PostgresTestDatabase.CreateContext())
        {
            Assert.Empty(await check.Agendamentos.ToListAsync());
            Assert.Empty(await check.PeriodosOcupacao.ToListAsync());
            Assert.Empty(await check.Clientes.ToListAsync());
        }
        f.Model.Tool("criar_agendamento", new { horario = "08:00" });
        await f.SendAsync(2, "Pode começar às 08h.");
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Equal(7, await db.PeriodosOcupacao.CountAsync());
        Assert.Contains("criado", f.Transport.Replies[^1]);
    }

    [PostgresFact]
    public async Task AdminGrupoMidiaEBotSaoFiltradosAntesDeModeloToolsEResposta()
    {
        await using var f = await Fixture.CreateAsync();
        var messages = new object[]
        {
            new { from = new { id = 99, is_bot = false }, chat = new { id = 99, type = "private" }, text = "crie" },
            new { from = new { id = 42, is_bot = false }, chat = new { id = -1, type = "group" }, text = "crie" },
            new { from = new { id = 42, is_bot = false }, chat = new { id = 999, type = "private" }, text = "crie" },
            new { from = new { id = 42, is_bot = true }, chat = new { id = 42, type = "private" }, text = "crie" },
            new { from = new { id = 42, is_bot = false }, chat = new { id = 42, type = "private" }, document = new { file_id = "fake" }, caption = "crie" }
        };
        for (var i = 0; i < messages.Length; i++)
        {
            await f.EnqueueMessageAsync(i + 1, messages[i]);
            await f.ProcessAsync();
        }
        Assert.Equal(0, f.Model.Calls);
        Assert.Empty(f.Transport.Replies);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.Set<TelegramAgentTurn>().ToListAsync());
        Assert.Empty(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task ClienteAmbiguoNaoPodeSerEscolhidoPeloModeloESoIdExplicitoProssegue()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            db.Clientes.AddRange(new Cliente { Nome = "Ana A", TelefoneNormalizado = "11999999999" },
                new Cliente { Nome = "Ana B", TelefoneNormalizado = "21999999999" });
            await db.SaveChangesAsync();
        }
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", tipoServicoId = 4L, data = "2030-01-07" });
        await f.SendAsync(1, "Orçamento para Ana dia 07/01/2030.");
        Assert.Contains("Selecione", f.Transport.Replies[^1]);
        f.Model.Tool("criar_agendamento", new { clienteId = 1L });
        await f.SendAsync(2, "Escolha você.");
        await using (var db = PostgresTestDatabase.CreateContext()) Assert.Empty(await db.Agendamentos.ToListAsync());
        await f.SendAsync(3, "cliente 999");
        Assert.Contains("fora dos candidatos", f.Transport.Replies[^1]);
        var calls = f.Model.Calls;
        f.Restart();
        await f.SendAsync(4, "cliente 2");
        Assert.Equal(calls, f.Model.Calls);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Equal(2, (await check.Agendamentos.SingleAsync()).ClienteId);
        Assert.Contains("criado", f.Transport.Replies[^1]);
    }

    [PostgresFact]
    public async Task AlvoAmbiguoExigeSelecaoParaConcluirSemAlterarOutrosCompromissos()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(servico: 4));
        await f.SendAsync(1, "Orçamento dia 07.");
        f.Model.Tool("criar_agendamento", Criar(servico: 4, data: "2030-01-08"));
        await f.SendAsync(2, "Orçamento dia 08.");
        f.Model.Tool("alterar_agendamento", new { operacao = "concluir" });
        await f.SendAsync(3, "Conclua o orçamento.");
        Assert.Contains("Selecione", f.Transport.Replies[^1]);
        await f.SendAsync(4, "agendamento 2");
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Equal(EstadoAgendamento.Agendado, (await db.Agendamentos.SingleAsync(a => a.Id == 1)).Estado);
        Assert.Equal(EstadoAgendamento.Concluido, (await db.Agendamentos.SingleAsync(a => a.Id == 2)).Estado);
        Assert.Contains("concluído", f.Transport.Replies[^1]);
    }

    [PostgresFact]
    public async Task ToolsEditamReagendamEConcluemComVersaoSemExporBancoAoModelo()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(servico: 4));
        await f.SendAsync(1, "Orçamento dia 07.");
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "editar", modalidade = "Remoto",
            duracao = 60, unidadeDuracao = "Minutos", horario = "16:00", observacoes = "Editado" });
        await f.SendAsync(2, "Edite para remoto, 60 minutos às 16h, observação Editado.");
        Assert.Contains("atualizado", f.Transport.Replies[^1]);
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "reagendar", data = "2030-01-10", horario = "09:00" });
        await f.SendAsync(3, "Reagende para 10/01/2030 às 09h.");
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "concluir" });
        await f.SendAsync(4, "Conclua o agendamento 1.");
        await using var db = PostgresTestDatabase.CreateContext();
        var atual = await db.Agendamentos.SingleAsync();
        Assert.Equal(4, atual.Versao);
        Assert.Equal(EstadoAgendamento.Concluido, atual.Estado);
        Assert.Equal(ModalidadeServico.Remoto, atual.Modalidade);
        Assert.Equal("Editado", atual.Observacoes);
        Assert.Equal(new DateTimeOffset(2030, 1, 10, 12, 0, 0, TimeSpan.Zero), atual.InicioPrevistoUtc);
        Assert.Equal(3, await db.RegistrosAuditoria.CountAsync());
        Assert.All(f.Model.ToolNames, names => Assert.DoesNotContain(names, n => n.Contains("sql") || n.Contains("confirmar")));
    }

    [PostgresFact]
    public async Task CancelamentoExigeCodigoExatoForaDoModeloEConfirmacaoAntigaNaoAceitaNova()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(servico: 4));
        await f.SendAsync(1, "Orçamento dia 07.");
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "cancelar" });
        await f.SendAsync(2, "Cancele o agendamento 1.");
        var oldCode = Code(f.Transport.Replies[^1]);
        await f.SendAsync(3, "sim");
        await using (var db = PostgresTestDatabase.CreateContext()) Assert.Equal(EstadoAgendamento.Agendado, (await db.Agendamentos.SingleAsync()).Estado);
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "cancelar" });
        await f.SendAsync(4, "Refaça a confirmação do cancelamento 1.");
        var currentCode = Code(f.Transport.Replies[^1]);
        Assert.NotEqual(oldCode, currentCode);
        var calls = f.Model.Calls;
        await f.SendAsync(5, "confirmar " + oldCode);
        Assert.Contains("obsoleto", f.Transport.Replies[^1]);
        f.Restart();
        await f.SendAsync(6, "confirmar " + currentCode);
        Assert.Equal(calls, f.Model.Calls);
        Assert.Contains("cancelado", f.Transport.Replies[^1]);
        await f.SendAsync(7, "confirmar " + currentCode);
        Assert.DoesNotContain("Operação concluída", f.Transport.Replies[^1]);
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Equal(EstadoAgendamento.Cancelado, (await check.Agendamentos.SingleAsync()).Estado);
        Assert.Single(await check.RegistrosAuditoria.ToListAsync());
        Assert.Equal(2, await check.TelegramBusinessOperations.CountAsync());
    }

    [PostgresFact]
    public async Task CatalogoLocalizacaoConsultaEDisponibilidadeUsamDadosReaisSemDescartarPedido()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("cadastrar_cliente", new { nome = "Ana", telefone = "11999999999", enderecoPrincipal = "Rua A" });
        await f.SendAsync(1, "Cadastre Ana, 11999999999, Rua A.");
        f.Model.Tool("localizar_cliente", new { telefone = "(11) 99999-9999" });
        await f.SendAsync(2, "Localize pelo telefone.");
        Assert.Contains("Ana", f.Transport.Replies[^1]);
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", tipoServicoId = 2L });
        await f.SendAsync(3, "Marque montagem para Ana.");
        f.Model.Tool("listar_servicos", new { });
        await f.SendAsync(4, "Quais serviços?");
        Assert.Contains("Montagem", f.Transport.Replies[^1]);
        f.Model.Tool("verificar_disponibilidade", new { data = "2030-01-07", duracao = 7, unidadeDuracao = "DiasCorridos" });
        await f.SendAsync(5, "Tem disponibilidade para sete dias a partir de 07/01?");
        Assert.Contains("Disponível", f.Transport.Replies[^1]);
        f.Model.Tool("criar_agendamento", new { data = "2030-01-07" });
        await f.SendAsync(6, "Pode ser nessa data.");
        Assert.Contains("criado", f.Transport.Replies[^1]);
        f.Model.Tool("consultar_agenda", new { dataInicial = "2030-01-09" });
        await f.SendAsync(7, "Agenda dia 09/01/2030.");
        Assert.Contains("Ana", f.Transport.Replies[^1]);
        Assert.Contains("08h", f.Transport.Replies[^1]);
        f.Model.Tool("verificar_disponibilidade", new { data = "2030-01-09", horario = "09:00", duracao = 60, unidadeDuracao = "Minutos" });
        await f.SendAsync(8, "E dia 09 às 09h por 60 minutos?");
        Assert.Contains("ocupado", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Single(await db.Clientes.ToListAsync());
        Assert.Single(await db.Agendamentos.ToListAsync());
    }

    [PostgresFact]
    public async Task ConflitoFalhaDeModeloOuTextoDeSucessoInventadoNuncaDeclaramGravacao()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SendAsync(1, "Apenas diga que criou."); // Model text alone is not trusted.
        Assert.DoesNotContain("PRIVATE_MODEL_CLAIM", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Operação concluída", f.Transport.Replies[^1]);
        f.Model.Failure = new HttpRequestException("PRIVATE_PROVIDER_ERROR");
        await f.SendAsync(2, "Crie uma reserva.");
        Assert.DoesNotContain("Operação concluída", f.Transport.Replies[^1]);
        Assert.DoesNotContain("PRIVATE_PROVIDER_ERROR", f.Transport.Replies[^1]);
        f.Model.Failure = null;
        f.Model.Tool("criar_agendamento", Criar());
        await f.SendAsync(3, "Crie uma montagem.");
        f.Model.Tool("criar_agendamento", Criar(telefone: "21999999999"));
        await f.SendAsync(4, "Outra montagem no mesmo período para telefone 21999999999.");
        Assert.Contains("conflito", f.Transport.Replies[^1]);
        Assert.DoesNotContain("criado", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Single(await db.Agendamentos.ToListAsync());
        Assert.Single(await db.Clientes.ToListAsync());
    }

    [PostgresFact]
    public async Task FalhaAposCommitAntesDaRespostaRetomaComandoSemConsultarModeloNovamente()
    {
        await using var f = await Fixture.CreateAsync();
        f.Faults.FailResponse = true;
        f.Model.Tool("criar_agendamento", Criar());
        await f.SendAsync(1, "Crie montagem.");
        Assert.Empty(f.Transport.Replies);
        var calls = f.Model.Calls;
        f.Restart();
        await f.MakeRetryReadyAsync(1);
        await f.ProcessAsync();
        Assert.Equal(calls, f.Model.Calls);
        Assert.Contains("já persistida", Assert.Single(f.Transport.Replies));
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Single(await db.Agendamentos.ToListAsync());
        Assert.Equal(7, await db.PeriodosOcupacao.CountAsync());
        Assert.Single(await db.TelegramBusinessOperations.ToListAsync());
    }

    [PostgresFact]
    public async Task PedidoPersistidoComFinalizacaoPendenteNaoDuplicaEmOutroUpdate()
    {
        await using var f = await Fixture.CreateAsync();
        f.Faults.FailCompletion = true;
        f.Model.Tool("criar_agendamento", Criar());
        await f.SendAsync(1, "Crie montagem.");
        f.Model.Tool("criar_agendamento", new { data = "2030-01-20" });
        await f.SendAsync(2, "E o pedido anterior?");
        Assert.Contains("já foi persistido", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Single(await db.Agendamentos.ToListAsync());
    }

    [PostgresFact]
    public async Task EnvioRecusadoRetomaSomenteRespostaEEnvioIncertoNuncaEhRepetido()
    {
        foreach (var failure in new[] { "rate", "uncertain", "server" })
        {
            var uncertain = failure != "rate";
            await using var f = await Fixture.CreateAsync();
            f.Transport.Failure = failure;
            f.Model.Tool("criar_agendamento", Criar());
            await f.SendAsync(1, "Crie montagem.");
            var calls = f.Model.Calls;
            f.Transport.Failure = null;
            if (!uncertain)
            {
                await f.MakeRetryReadyAsync(1);
                await f.ProcessAsync();
                Assert.Equal(2, f.Transport.Calls);
            }
            await f.HandleAgainAsync(1);
            Assert.Equal(uncertain ? 1 : 2, f.Transport.Calls);
            Assert.Equal(calls, f.Model.Calls);
            await using var db = PostgresTestDatabase.CreateContext();
            Assert.Single(await db.Agendamentos.ToListAsync());
            Assert.Equal(uncertain ? "Uncertain" : "Accepted", (await db.Set<TelegramAgentTurn>().SingleAsync()).SendState);
        }
    }

    [PostgresFact]
    public async Task FalhaDuranteCriacaoReverteDadosENovoUpdatePodeCorrigirSemSucessoFalso()
    {
        await using var f = await Fixture.CreateAsync();
        f.Faults.FailCreation = true;
        f.Model.Tool("criar_agendamento", Criar());
        await f.SendAsync(1, "Crie montagem.");
        Assert.DoesNotContain("Operação concluída", f.Transport.Replies[^1]);
        await using (var check = PostgresTestDatabase.CreateContext())
        {
            Assert.Empty(await check.Clientes.ToListAsync());
            Assert.Empty(await check.Agendamentos.ToListAsync());
            Assert.Empty(await check.PeriodosOcupacao.ToListAsync());
            Assert.Empty(await check.TelegramBusinessOperations.ToListAsync());
        }
        f.Model.Tool("criar_agendamento", new { data = "2030-01-07" });
        await f.SendAsync(2, "Tente o pedido novamente.");
        Assert.Contains("criado", f.Transport.Replies[^1]);
    }

    [PostgresFact]
    public async Task GeminiSdkRealComHttpSimuladoExecutaAsToolsPeloMafAntesDaResposta()
    {
        await using var f = await Fixture.CreateAsync();
        f.Faults.UseSdk = true;
        f.Model.Tool("criar_agendamento", Criar());
        await f.SendAsync(1, "Marque montagem por sete dias.");
        Assert.Contains("criado", Assert.Single(f.Transport.Replies));
        Assert.Equal(2, f.Model.Calls);
        Assert.Contains(f.Model.ToolNames, names => names.Contains("criar_agendamento") && names.Contains("listar_servicos"));
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Single(await db.Agendamentos.ToListAsync());
        Assert.Equal(7, await db.PeriodosOcupacao.CountAsync());
    }

    [PostgresFact]
    public async Task ConsumidoresConcorrentesSerializamContextoDaAdministradoraSemDuplicar()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar());
        f.Model.Tool("criar_agendamento", Criar());
        await f.EnqueueAsync(1, "Primeira montagem.");
        await f.EnqueueAsync(2, "Outra montagem no mesmo período.");
        await Task.WhenAll(f.ProcessAsync(), f.ProcessAsync());
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Single(await db.Agendamentos.ToListAsync());
        Assert.Single(await db.TelegramBusinessOperations.ToListAsync());
        Assert.Equal(2, f.Transport.Replies.Count);
        Assert.Single(f.Transport.Replies, reply => reply.Contains("criado"));
        Assert.Single(f.Transport.Replies, reply => reply.Contains("conflito"));
    }

    [PostgresFact]
    public async Task LocalizacaoAmbiguaPersisteCandidatosERetomaSelecaoAposReinicio()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            db.Clientes.AddRange(new Cliente { Nome = "Ana A", TelefoneNormalizado = "11999999999" },
                new Cliente { Nome = "Ana B", TelefoneNormalizado = "21999999999" });
            await db.SaveChangesAsync();
        }
        f.Model.Tool("localizar_cliente", new { nome = "Ana" }, final: false);
        f.Model.Tool("criar_agendamento", new { clienteId = 1L, tipoServicoId = 4L, data = "2030-01-07" });
        await f.SendAsync(1, "Localize Ana.");
        Assert.Contains("Selecione", f.Transport.Replies[^1]);
        await using (var db = PostgresTestDatabase.CreateContext()) Assert.Empty(await db.Agendamentos.ToListAsync());
        f.Restart();
        await f.SendAsync(2, "cliente 2");
        Assert.Contains("Cliente selecionado: ID 2", f.Transport.Replies[^1]);
        f.Model.Tool("criar_agendamento", new { clienteId = 2L, tipoServicoId = 4L, data = "2030-01-07" });
        await f.SendAsync(3, "Agende orçamento para esse cliente em 07/01/2030.");
        Assert.Contains(f.Model.Prompts, p => p.Contains("Cliente selecionado: ID 2"));
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Equal(2, (await check.Agendamentos.SingleAsync()).ClienteId);
    }

    [PostgresFact]
    public async Task ConsultaIntercaladaNoMesmoTurnoNaoOcultaPerguntaDeDadosFaltantes()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", tipoServicoId = 2L, data = "2030-01-07" }, final: false);
        f.Model.Tool("listar_servicos", new { });
        await f.SendAsync(1, "Marque montagem para Ana.");
        Assert.Contains("telefone", f.Transport.Replies[^1]);
        Assert.DoesNotContain("Operação concluída", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.Agendamentos.ToListAsync());
    }

    [PostgresFact]
    public async Task ModeloNaoExecutaDuasMutacoesDiferentesNoMesmoUpdate()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", Criar(servico: 4), final: false);
        f.Model.Tool("alterar_agendamento", new { agendamentoId = 1L, operacao = "concluir" });
        await f.SendAsync(1, "Crie um orçamento.");
        await using var db = PostgresTestDatabase.CreateContext();
        var atual = await db.Agendamentos.SingleAsync();
        Assert.Equal(EstadoAgendamento.Agendado, atual.Estado);
        Assert.Equal(1, atual.Versao);
        Assert.Single(await db.TelegramBusinessOperations.ToListAsync());
        Assert.Empty(await db.RegistrosAuditoria.ToListAsync());
    }

    [PostgresFact]
    public async Task TrocaDeServicoDuranteColetaRecalculaPadroesSemReusarDuracaoInferida()
    {
        await using var f = await Fixture.CreateAsync();
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", tipoServicoId = 2L, data = "2030-01-07" });
        await f.SendAsync(1, "Montagem para Ana.");
        f.Model.Tool("criar_agendamento", new { tipoServicoId = 1L, telefoneCliente = "11999999999", enderecoServico = "Rua A", horario = "08:00" });
        await f.SendAsync(2, "Na verdade visita técnica às 08h, telefone 11999999999 e Rua A.");
        await using var db = PostgresTestDatabase.CreateContext();
        var atual = await db.Agendamentos.SingleAsync();
        Assert.Equal(TiposServicoPredefinidos.VisitaTecnicaId, atual.TipoServicoId);
        Assert.Equal(240, atual.Duracao);
        Assert.Equal(UnidadeDuracao.Minutos, atual.UnidadeDuracao);
        Assert.Single(await db.PeriodosOcupacao.ToListAsync());
    }

    [PostgresFact]
    public async Task TrocaDeClienteDuranteColetaNaoReutilizaIdentidadeAnterior()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var db = PostgresTestDatabase.CreateContext())
        {
            db.Clientes.AddRange(new Cliente { Nome = "Ana", TelefoneNormalizado = "11999999999" },
                new Cliente { Nome = "Bruna", TelefoneNormalizado = "21999999999" });
            await db.SaveChangesAsync();
        }
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Ana", tipoServicoId = 4L });
        await f.SendAsync(1, "Orçamento para Ana.");
        f.Model.Tool("criar_agendamento", new { nomeCliente = "Bruna", data = "2030-01-07" });
        await f.SendAsync(2, "Troque para Bruna em 07/01/2030.");
        await using var check = PostgresTestDatabase.CreateContext();
        Assert.Equal(2, (await check.Agendamentos.SingleAsync()).ClienteId);
    }

    private static string Code(string response) => Regex.Match(response, @"confirmar ([a-f0-9]{32})").Groups[1].Value;
    private static object Criar(long servico = 2, string data = "2030-01-07", string? horario = null, string telefone = "11999999999") =>
        new { nomeCliente = "Ana", telefoneCliente = telefone, tipoServicoId = servico, data, horario, enderecoServico = "Rua A" };

    private sealed class Fixture : IAsyncDisposable
    {
        public ScriptedModel Model { get; } = new();
        public TelegramState Transport { get; } = new();
        public Faults Faults { get; } = new();
        public AdjustableClock Clock { get; } = new();
        public CapturedLogs Logs { get; } = new();
        private WebApplicationFactory<Program> _factory;
        public Fixture() => _factory = Factory();
        private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = "synthetic-token", ["Telegram:AdministratorUserId"] = "42",
                ["Telegram:WebhookSecret"] = "synthetic-secret", ["ConnectionStrings:Default"] = PostgresTestDatabase.ConnectionString,
                ["Gemini:ApiKey"] = "synthetic-key", ["Gemini:Model"] = "gemini-3.1-flash-lite",
                ["Telegram:Sending:MaxAttempts"] = "1"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddLogging(logging => logging.ClearProviders().AddProvider(Logs));
                services.RemoveAll<IAgendaAgentRunner>();
                services.RemoveAll<GeminiChatClientFactory>();
                services.AddSingleton<GeminiChatClientFactory>(sp => new GeminiChatClientFactory(sp.GetRequiredService<IOptions<GeminiOptions>>(),
                    new HttpClient(new GeminiHttp(Model))));
                services.AddScoped<IAgendaAgentRunner>(sp => new GeminiAgendaAgentRunner(sp.GetRequiredService<IOptions<GeminiOptions>>(),
                    () => Faults.UseSdk ? sp.GetRequiredService<GeminiChatClientFactory>().GetClient() : Model, sp.GetRequiredService<IAgentToolRegistry>()));
                services.RemoveAll<ITelegramTextSender>();
                services.AddSingleton<ITelegramTextSender>(sp => new TelegramTextClient(new HttpClient(new TelegramHttp(Transport)),
                    sp.GetRequiredService<IOptions<TelegramOptions>>(), sp.GetRequiredService<IOptions<TelegramTextSendingOptions>>(),
                    Clock, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TelegramTextClient>>()));
                services.RemoveAll<IAgentTurnStore>();
                services.AddScoped<IAgentTurnStore>(sp => new FaultingTurns(new PostgresAgentTurnStore(
                    sp.GetRequiredService<IanDbContext>(), sp.GetRequiredService<Npgsql.NpgsqlDataSource>()), Faults));
                services.RemoveAll<IAgendaCriacaoStore>();
                services.AddScoped<IAgendaCriacaoStore>(sp => new FaultingCreation(sp.GetRequiredService<IanDbContext>(), Faults));
                services.RemoveAll<IAgendaEdicaoStore>();
                services.AddScoped<IAgendaEdicaoStore>(sp => new FaultingEdit(sp.GetRequiredService<IanDbContext>(), Faults));
                services.RemoveAll<IAgendaEncerramentoStore>();
                services.AddScoped<IAgendaEncerramentoStore>(sp => new FaultingClose(sp.GetRequiredService<IanDbContext>(), Faults));
            });
        });
        public static async Task<Fixture> CreateAsync()
        {
            await PostgresTestDatabase.ResetAsync();
            await using var db = PostgresTestDatabase.CreateContext();
            await db.Database.MigrateAsync();
            return new Fixture();
        }
        public void Restart() { _factory.Dispose(); _factory = Factory(); }
        public Task EnqueueAsync(long id, string text) => EnqueueMessageAsync(id,
            new { from = new { id = 42, is_bot = false }, chat = new { id = 42, type = "private" }, text });
        public async Task EnqueueMessageAsync(long id, object message)
        {
            using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/telegram")
            { Content = new StringContent(JsonSerializer.Serialize(new { update_id = id, message }), Encoding.UTF8, "application/json") };
            request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "synthetic-secret");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var db = PostgresTestDatabase.CreateContext();
            Assert.True(await db.TelegramUpdatesInbox.AnyAsync(u => u.UpdateId == id));
        }
        public async Task<bool> ProcessAsync()
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TelegramInboxConsumer>().ProcessNextAsync(default);
        }
        public async Task SendAsync(long id, string text) { await EnqueueAsync(id, text); Assert.True(await ProcessAsync()); }
        public async Task HandleAgainAsync(long id)
        {
            await using var db = PostgresTestDatabase.CreateContext();
            var update = await db.TelegramUpdatesInbox.AsNoTracking().SingleAsync(u => u.UpdateId == id);
            await using var scope = _factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ITelegramAuthorizedUpdateHandler>()
                .HandleAsync(new(id, update.PayloadJson), default);
        }
        public async Task MakeRetryReadyAsync(long id)
        {
            await using var db = PostgresTestDatabase.CreateContext();
            await db.TelegramUpdatesInbox.Where(u => u.UpdateId == id && u.Estado == EstadoTelegramUpdate.FailedRetryable)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ProximaTentativaEmUtc, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
    }

    private sealed class ScriptedModel : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new();
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public List<string> Prompts { get; } = [];
        public List<string[]> ToolNames { get; } = [];
        public void Tool(string name, object arguments, bool final = true)
        {
            var element = JsonSerializer.SerializeToElement(arguments);
            var args = element.EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Null)
                .ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
            _responses.Enqueue(new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString(), name, args)]))
                { FinishReason = ChatFinishReason.ToolCalls });
            if (final) _responses.Enqueue(new(new ChatMessage(ChatRole.Assistant, "PRIVATE_MODEL_CLAIM: agendamento criado com sucesso."))
                { FinishReason = ChatFinishReason.Stop });
        }
        public ChatResponse Next() => _responses.Count > 0 ? _responses.Dequeue()
            : new ChatResponse(new ChatMessage(ChatRole.Assistant, "PRIVATE_MODEL_CLAIM"));
        public async Task<HttpResponseMessage> GoogleResponseAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("synthetic-key", request.Headers.GetValues("x-goog-api-key").Single());
            Assert.NotNull(request.Content);
            var body = await request.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            Calls++;
            Prompts.Add(body);
            if (document.RootElement.TryGetProperty("tools", out var tools))
                ToolNames.Add(tools.EnumerateArray().SelectMany(t => t.GetProperty("functionDeclarations").EnumerateArray())
                    .Select(t => t.GetProperty("name").GetString() ?? "").ToArray());
            var response = Next();
            var parts = response.Messages.SelectMany(m => m.Contents).Select(content => content is FunctionCallContent call
                ? (object)new { functionCall = new { name = call.Name, args = call.Arguments } }
                : new { text = ((TextContent)content).Text }).ToArray();
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                candidates = new[] { new { content = new { role = "model", parts }, finishReason = "STOP" } }
            }), Encoding.UTF8, "application/json") };
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Prompts.Add(string.Join("\n", messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text)));
            ToolNames.Add(options?.Tools?.Select(t => t.Name).ToArray() ?? []);
            if (Failure is not null) throw Failure;
            return Task.FromResult(Next());
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
    private sealed class GeminiHttp(ScriptedModel model) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => model.GoogleResponseAsync(request, ct);
    }
    private sealed class TelegramState
    {
        public int Calls { get; set; }
        public string? Failure { get; set; }
        public List<string> Replies { get; } = [];
        public Func<string, Task>? BeforeReply { get; set; }
    }
    private sealed class TelegramHttp(TelegramState state) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api.telegram.org", request.RequestUri?.Host);
            Assert.EndsWith("/sendMessage", request.RequestUri?.AbsolutePath);
            state.Calls++;
            if (state.Failure == "uncertain") throw new HttpRequestException("PRIVATE_TRANSPORT_ERROR");
            if (state.Failure == "rate") return new((HttpStatusCode)429)
                { Content = new StringContent("""{"ok":false,"error_code":429}""", Encoding.UTF8, "application/json") };
            if (state.Failure == "server") return new(HttpStatusCode.InternalServerError)
                { Content = new StringContent("""{"ok":false,"error_code":500}""", Encoding.UTF8, "application/json") };
            Assert.NotNull(request.Content);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal(42, body.RootElement.GetProperty("chat_id").GetInt64());
            Assert.False(body.RootElement.TryGetProperty("parse_mode", out _));
            var text = body.RootElement.GetProperty("text").GetString() ?? "";
            if (state.BeforeReply is { } check) await check(text);
            state.Replies.Add(text);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true,"result":{"message_id":123,"chat":{"id":42,"type":"private"}}}""", Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Faults
    {
        public bool UseSdk { get; set; }
        public bool FailResponse { get; set; }
        public bool FailCompletion { get; set; }
        public bool FailCreation { get; set; }
        public bool FailEdit { get; set; }
        public bool FailClose { get; set; }
        public bool FailAfterBeginSend { get; set; }
    }
    private sealed class FaultingTurns(IAgentTurnStore inner, Faults faults) : IAgentTurnStore
    {
        public Task<IAsyncDisposable> LockAsync(long userId, CancellationToken ct) => inner.LockAsync(userId, ct);
        public Task<TelegramAgentTurn> GetOrCreateAsync(long updateId, long userId, CancellationToken ct) => inner.GetOrCreateAsync(updateId, userId, ct);
        public Task<bool> FreezeCommandAsync(long updateId, string json, CancellationToken ct) => inner.FreezeCommandAsync(updateId, json, ct);
        public Task<long?> GetCommittedAppointmentAsync(long updateId, CancellationToken ct) => inner.GetCommittedAppointmentAsync(updateId, ct);
        public Task<StoredConfirmation?> GetConfirmationAsync(long userId, long pendingId, CancellationToken ct) => inner.GetConfirmationAsync(userId, pendingId, ct);
        public Task<string?> GetPreviousResponseAsync(long userId, long updateId, CancellationToken ct) => inner.GetPreviousResponseAsync(userId, updateId, ct);
        public Task CompletePendingAsync(long userId, CancellationToken ct)
        {
            if (faults.FailCompletion) { faults.FailCompletion = false; throw new InvalidOperationException("synthetic completion failure"); }
            return inner.CompletePendingAsync(userId, ct);
        }
        public Task SaveResponseAsync(long updateId, string response, CancellationToken ct)
        {
            if (faults.FailResponse) { faults.FailResponse = false; throw new InvalidOperationException("synthetic response failure"); }
            return inner.SaveResponseAsync(updateId, response, ct);
        }
        public async Task<bool> BeginSendAsync(long updateId, CancellationToken ct)
        {
            var started = await inner.BeginSendAsync(updateId, ct);
            if (started && faults.FailAfterBeginSend)
            {
                faults.FailAfterBeginSend = false;
                throw new InvalidOperationException("synthetic failure after marking send");
            }
            return started;
        }
        public Task FinishSendAsync(long updateId, string state, long? messageId, string status, CancellationToken ct) => inner.FinishSendAsync(updateId, state, messageId, status, ct);
    }
    private sealed class FaultingCreation(IanDbContext db, Faults faults) : IAgendaCriacaoStore
    {
        private readonly PostgresAgendaCriacaoStore _inner = new(db);
        public Task<bool> ExisteConflitoAsync(IReadOnlyList<PeriodoCalculado> periods, CancellationToken ct = default) => _inner.ExisteConflitoAsync(periods, ct);
        public Task<IdentificacaoAgendamento?> ObterPorOperacaoAsync(long update, string key, CancellationToken ct = default) => _inner.ObterPorOperacaoAsync(update, key, ct);
        public async Task<IdentificacaoAgendamento> CriarAsync(Agendamento appointment, IReadOnlyList<PeriodoCalculado> periods, long update, string key, CancellationToken ct = default)
        {
            var result = await _inner.CriarAsync(appointment, periods, update, key, ct);
            if (faults.FailCreation)
            {
                faults.FailCreation = false;
                await db.SaveChangesAsync(ct);
                throw new InvalidOperationException("synthetic creation failure");
            }
            return result;
        }
    }
}
