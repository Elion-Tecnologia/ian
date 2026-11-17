using Ian.Application.AI;
using Ian.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Net;

namespace Ian.Tests;

public sealed partial class TelegramAgentPostgresTests
{
    [PostgresFact]
    public async Task ReadinessRejectsOldSchemaAndRecoversAfterExplicitMigration()
    {
        await using var f = await Fixture.CreateAsync();
        Assert.Equal(HttpStatusCode.OK, await f.ReadinessAsync());
        await PostgresTestDatabase.ResetAsync();
        await using var db = PostgresTestDatabase.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync("20261112045401_PersistirRetentativasRespostas");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await f.ReadinessAsync());
        await f.EnqueueAsync(1, "Consulte a agenda");
        await f.WaitForSchemaGuardAsync();
        var queued = await db.TelegramUpdatesInbox.AsNoTracking().SingleAsync();
        Assert.Equal(EstadoTelegramUpdate.Received, queued.Estado);
        Assert.Equal(0, queued.Tentativas);
        Assert.Equal(0, f.Model.Calls);
        Assert.Single(await db.Database.GetPendingMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.Equal(HttpStatusCode.OK, await f.ReadinessAsync());
    }

    [PostgresFact]
    public async Task CustomerQueryDoesNotReplacePendingCustomerOrAuthorizeModelWrite()
    {
        await using var f = await Fixture.CreateAsync();
        await using (var seed = PostgresTestDatabase.CreateContext())
        {
            seed.Clientes.Add(new Cliente { Nome = "Bruna", TelefoneNormalizado = "21999999999" });
            await seed.SaveChangesAsync();
        }
        f.Model.Tool("criar_agendamento", new { });
        await f.SendAsync(1, "Quero agendar um serviço");
        await f.SendAsync(2, "Ana Souza");
        f.Restart();
        f.Model.InterpretationOverride = new(DialogueGoal.Consulta, DialogueReply.Resultado);
        f.Model.Tool("localizar_cliente", new { nome = "Bruna" });
        await f.SendAsync(3, "Consulte o cadastro de Bruna");
        Assert.Contains("Bruna", f.Transport.Replies[^1]);
        Assert.Contains("21999999999", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        var pending = await db.SolicitacoesPendentes.SingleAsync(p => p.Estado == EstadoSolicitacaoPendente.Pendente);
        Assert.Contains("Ana Souza", pending.DadosColetadosJson);
        Assert.DoesNotContain("Bruna", pending.DadosColetadosJson);
        f.Model.Tool("criar_agendamento", Criar());
        await f.SendAsync(4, "Consulte a agenda");
        Assert.Empty(await db.Agendamentos.ToListAsync());
        Assert.Single(await db.Clientes.ToListAsync());
        f.Model.InterpretationOverride = new(DialogueGoal.CriarAgendamento, DialogueReply.Coletar);
        f.Model.Tool("criar_agendamento", new { usarContexto = true });
        await f.SendAsync(5, "Pode continuar o pedido");
        var continued = await db.SolicitacoesPendentes.AsNoTracking().SingleAsync(p => p.Estado == EstadoSolicitacaoPendente.Pendente);
        Assert.Contains("Ana Souza", continued.DadosColetadosJson);
        Assert.DoesNotContain("Bruna", continued.DadosColetadosJson);
        Assert.Contains("telefone", f.Transport.Replies[^1]);
        db.Clientes.Add(new Cliente { Nome = "Bruna Silva", TelefoneNormalizado = "31999999999" });
        await db.SaveChangesAsync();
        f.Model.InterpretationOverride = new(DialogueGoal.Consulta, DialogueReply.Resultado);
        f.Model.Tool("localizar_cliente", new { nome = "Bruna" });
        await f.SendAsync(6, "Consulte o cadastro de Bruna");
        Assert.Contains("Refine", f.Transport.Replies[^1]);
        var preserved = await db.SolicitacoesPendentes.AsNoTracking().SingleAsync(p => p.Estado == EstadoSolicitacaoPendente.Pendente);
        Assert.Equal(continued.DadosColetadosJson, preserved.DadosColetadosJson);
        Assert.Equal(continued.CandidatosJson, preserved.CandidatosJson);
    }

    [PostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AvailabilityQueryAnswersBackendResultWithoutLosingPendingCreation(bool sdk)
    {
        await using var f = await Fixture.CreateAsync();
        f.Faults.UseSdk = sdk;
        f.Restart();
        f.Model.Tool("criar_agendamento", new { });
        await f.SendAsync(1, "Quero agendar um serviço");
        Assert.Contains("nome", f.Transport.Replies[^1]);
        f.Restart();
        f.Model.InterpretationOverride = new(DialogueGoal.Consulta, DialogueReply.Resultado);
        f.Model.Tool("verificar_disponibilidade", new { data = "2030-01-20", duracao = 60, unidadeDuracao = "Minutos", horario = "09:00" });
        await f.SendAsync(2, "Consulte a disponibilidade do dia 20 às nove por uma hora");
        Assert.Contains("Disponível:", f.Transport.Replies[^1]);
        Assert.Contains("09:00", f.Transport.Replies[^1]);
        Assert.DoesNotContain("nome do cliente", f.Transport.Replies[^1]);
        await using var db = PostgresTestDatabase.CreateContext();
        Assert.Empty(await db.Agendamentos.ToListAsync());
        Assert.Single(await db.SolicitacoesPendentes.Where(p => p.Estado == EstadoSolicitacaoPendente.Pendente).ToListAsync());
        f.Restart();
        await f.SendAsync(3, "Ana Souza");
        Assert.Contains("telefone", f.Transport.Replies[^1]);
    }
}
