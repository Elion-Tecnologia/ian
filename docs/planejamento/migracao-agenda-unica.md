# Agenda única — proposta de modelo e migração incremental

**Decisão funcional:** aprovada em 10/11/2026.
**Implementação física:** proposta para revisão; nenhuma migration gerada ou executada.
**Issue:** #79. **Base inspecionada:** origin/dev, 207be7a.
**Entrega atual:** #78 remove apenas suporte técnico de mídia; Domain, EF e migrations permanecem intactos.

## 1. Resultado pretendido

Uma agenda para a administradora, sem equipe, grupo, folga ou jornada
excepcional. Manter clientes, serviços, compromissos, estados, confirmação,
idempotência, auditoria e todos os dados anteriores. Não criar uma equipe
artificial nem uma tabela Agenda para esconder o modelo antigo.

### Agendamento final proposto

| Campo | Tipo / regra |
| --- | --- |
| Id | long; PK e identidade existentes preservadas. |
| ClienteId | long obrigatório; FK restritiva para clientes. |
| TipoServicoId | long obrigatório; FK restritiva para tipos_servico. |
| Modalidade | Interno, Externo ou Remoto. |
| Duracao | int positivo; snapshot da duração escolhida. |
| UnidadeDuracao | Minutos ou DiasCorridos. |
| InicioPrevistoUtc / FimPrevistoUtc | DateTimeOffset em UTC; início < fim, cobrindo os períodos efetivos. |
| EnderecoSnapshot | string opcional; obrigatório para Externo, não muda por edição do cliente. |
| Observacoes | string opcional, não interpretada como instrução de sistema. |
| Estado | Agendado, Concluido ou Cancelado; sem exclusão física. |
| Versao | long >=1, padrão 1; token de concorrência incrementado por mutação. |

Retirar EquipeId; nenhuma substituição por AgendaId. Versao é proposta nova
para atender edições concorrentes e confirmações obsoletas já previstas em
#22/#33; não está implementada no modelo atual.

### PeriodoOcupacao e duração

Preservar Id, AgendamentoId, InicioUtc e FimUtc. PeriodoOcupacao já não possui
EquipeId; seu escopo hoje é derivado do pai. Continua 1:N com Agendamento,
intervalos semiabertos [início,fim), início < fim e ON DELETE RESTRICT.
A consulta de disponibilidade considera apenas períodos de pais Agendados;
concluir/cancelar libera reservas sem apagar o histórico de execução.

Jornada fixa proposta: 08h–18h em America/Sao_Paulo, todos os dias, inclusive
domingos. Minutos consomem jornada: 240 minutos a partir de 16h reservam
16h–18h e 08h–10h do dia seguinte. DiasCorridos contam datas consecutivas:
sete dias iniciados segunda às 10h reservam segunda 10h–18h e mais seis
janelas 08h–18h, terminando domingo às 18h. Um dia iniciado às 10h termina
às 18h dessa data. Data inicial parcial conta como dia 1; sem compensação
por folgas, fins de semana, bloqueios ou atraso. Início fora de [08h,18h)
é recusado. Não transformar a duração em reserva contínua de 24 horas.

O cálculo ainda não existe em Domain/Application; #30 o implementará.
Dados já calculados sob regras antigas não serão recalculados pela migration.
Agendados incompatíveis exigem reconciliação aprovada antes da ativação;
Concluídos/Cancelados mantêm sua representação histórica.

BloqueioAgenda passa a ter Id, InicioUtc e FimUtc, sem EquipeId. Todo bloqueio
afeta a agenda única. Bloqueios individuais antigos **não podem ser promovidos
silenciosamente** a bloqueios globais. A administradora deve decidir como
tratá-los; arquivar não significa cancelar automaticamente reservas.

## 2. Inventário de código e esquema afetados

| Área / arquivo | Alteração posterior proposta |
| --- | --- |
| Domain/Entities/Agendamento.cs | Retirar EquipeId e adicionar Versao. |
| Domain/Entities/Equipe.cs | Retirar do modelo ativo após preservação dos registros e vínculos. |
| Domain/Entities/ExcecaoExpediente.cs | Retirar do modelo ativo; preservar calendário e folgas legadas. |
| Domain/Entities/BloqueioAgenda.cs | Retirar EquipeId após decisão sobre cada escopo antigo. |
| Domain/Entities/PeriodoOcupacao.cs | Manter campos e IDs; sem mudança de cardinalidade. |
| Persistence/IanDbContext.cs | Retirar DbSets Equipes/ExcecoesExpediente; manter demais DbSets. |
| Configurations/CadastrosConfiguration.cs | Retirar EquipeConfiguration; preservar Cliente/TipoServico. |
| Configurations/AgendaConfiguration.cs | Adaptar Agendamento/Bloqueio; retirar ExcecaoExpedienteConfiguration; manter PeriodoOcupacao. |
| Migrations/IanDbContextModelSnapshot.cs | Atualizar somente ao gerar novas migrations aprovadas. |
| Contratos/handlers Telegram de grupos | Remover TryGetGroupCandidate, candidatos/handler e projeção de associação; revalidar grupo sempre não administrativo. |
| Casos de uso/Tools futuros | Parâmetros sem EquipeId, lock global e versão; nenhuma Tool de grupo/equipe/exceção. |

Migrations existentes **preservadas sem edição**:

- 20261109134948_InitialDatabase
- 20261109145017_DomainModel
- 20261109194028_TelegramUpdateInbox
- 20261110155523_TelegramInboxProcessing

Não remover/regerar o histórico inicial ou zerar o banco. `DomainModel` registra
o legado já integrado, não deve ser reescrita para parecer agenda única.

### Chaves, índices e constraints

| Objeto existente | Tratamento proposto |
| --- | --- |
| FK_agendamentos_equipes_EquipeId | Retirar após arquivar a correspondência agendamento/equipe. |
| FK_excecoes_expediente_equipes_EquipeId | Preservar na área legada; sai do modelo ativo com as tabelas. |
| FK_bloqueios_agenda_equipes_EquipeId | Retirar após arquivar/reconciliar o escopo antigo. |
| IX_agendamentos_EquipeId_Estado | Substituir por IX_agendamentos_Estado_InicioPrevistoUtc. |
| IX_excecoes_expediente_EquipeId_DataLocal (único) | Preservar com tabela legada; sem novo equivalente ativo. |
| IX_bloqueios_agenda_EquipeId_InicioUtc | Substituir por IX_bloqueios_agenda_InicioUtc. |
| IX_periodos_ocupacao_AgendamentoId_InicioUtc | Preservar; avaliar adicional IX_periodos_ocupacao_InicioUtc_FimUtc para sobreposição. |
| PKs de todas as entidades preservadas | Manter identidades/valores; mover equipes/exceções para arquivo legado, sem recriar IDs. |
| FKs Agendamento→Cliente/TipoServico, Periodo→Agendamento, Auditoria→Agendamento | Preservar integralmente, ON DELETE RESTRICT. |
| Checks de duração, período, estado, modalidade, endereço e intervalo | Preservar; adicionar ck_agendamentos_versao (Versao >= 1). |
| UNIQUE de telefone, índices de auditoria/solicitação/envio/inbox/operação idempotente | Sem alteração nesta migração. |

Os nomes acima vêm da migration e dos mapeamentos, não de um catálogo de banco
consultado. Conferir nomes reais e outras views/triggers/FKs/dependências de
cada instalação antes de gerar o SQL. Índices de intervalo não são constraints
de exclusão nem garantem ausência de conflito por si sós.

## 3. Concorrência proposta para a agenda única

O esquema atual não impõe exclusão de períodos sobrepostos. Para o MVP,
propor um único advisory lock de transação, com chave constante documentada,
compartilhado por criar, editar, reagendar, cancelar, concluir, reabrir,
prorrogar e criar/remover bloqueio. Dentro de uma transação READ COMMITTED:

1. Validar autorização, chave idempotente e parâmetros iniciais.
2. Adquirir o lock global antes de ler disponibilidade.
3. Executar nova consulta após obter o lock, considerando ocupações de pais
   Agendados, bloqueios, alvo/Versao e confirmação ainda válida.
4. Persistir compromisso, ocupações, versão, auditoria e chave idempotente.
5. Confirmar a transação; somente então responder sucesso. O lock é liberado
   pela conclusão da transação, inclusive no rollback/cancelamento.

Adotar uma ordem única para locks de idempotência/agenda/linhas em todos os
casos de uso para evitar inversões; timeout de lock gera falha controlada,
sem retry de escrita desprotegida. A chave idempotente deve continuar atômica
com a mutação. O executor atual não substitui a proteção entre updates diferentes.

Esse desenho serializa os escritores da aplicação e atende a uma agenda
com uma administradora. Não protege SQL manual que ignore o protocolo;
contas e caminhos de escrita devem respeitá-lo. Constraint de exclusão com
predicado do estado do pai exigiria outro desenho físico/duplicação de estado;
não está implementada nem será alegada como proteção existente.

Referências primárias: [advisory locks transacionais](https://www.postgresql.org/docs/17/explicit-locking.html#ADVISORY-LOCKS)
e [READ COMMITTED](https://www.postgresql.org/docs/17/transaction-iso.html#XACT-READ-COMMITTED).
O protocolo é proposta do projeto e exige teste com duas conexões reais.

## 4. Dados existentes — verificação e limitações

Conferência deste ambiente em 10/11/2026, sem revelar valores de configuração:

- ConnectionStrings__Default e IAN_TEST_POSTGRES_CONNECTION_STRING ausentes.
- User Secrets de Ian.Api não disponíveis no caminho padrão deste usuário.
- psql/pg_dump não encontrados no PATH; PostgreSQL Homebrew/Postgres.app e
  socket Docker padrão não identificados.
- Nenhuma conexão foi aberta e nenhum SQL foi executado no PostgreSQL.

**Não foi possível determinar se os bancos existentes contêm dados.** Isso
não comprova ausência de banco remoto ou em outro computador. Inventário de
cada ambiente é pré-condição obrigatória. Os sete testes PostgreSQL continuam
não executados; não usar bancos existentes para torná-los executáveis.

### Inventário obrigatório em modo somente leitura

Para cada conexão autorizada/configurada, sem imprimir segredos nem conteúdo
pessoal, registrar versão PostgreSQL, migrations aplicadas, tabelas,
constraints, contagens e agregados. Identificar bancos de desenvolvimento,
homologação e operação; não supor que todos tenham a mesma versão.

Exemplos de consultas **não executadas**, para banco com DomainModel aplicado:

```sql
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '10s';
SELECT "MigrationId", "ProductVersion" FROM public."__EFMigrationsHistory" ORDER BY 1;
SELECT count(*) AS equipes FROM public.equipes;
SELECT "Estado", count(*) FROM public.agendamentos GROUP BY "Estado";
SELECT count(*) AS periodos FROM public.periodos_ocupacao;
SELECT count(*) AS excecoes FROM public.excecoes_expediente;
SELECT ("EquipeId" IS NULL) AS global, count(*)
FROM public.bloqueios_agenda GROUP BY 1;
SELECT count(*) AS clientes FROM public.clientes;
SELECT count(*) AS auditorias FROM public.registros_auditoria;
SELECT count(*) AS envios FROM public.envios_telegram;
SELECT count(*) AS pendencias FROM public.solicitacoes_pendentes;
SELECT count(*) AS pares_de_ocupacoes_conflitantes
FROM public.periodos_ocupacao p
JOIN public.agendamentos a ON a."Id" = p."AgendamentoId"
JOIN public.periodos_ocupacao q ON p."Id" < q."Id"
JOIN public.agendamentos b ON b."Id" = q."AgendamentoId"
WHERE a."Estado" = 'Agendado' AND b."Estado" = 'Agendado'
  AND p."InicioUtc" < q."FimUtc" AND q."InicioUtc" < p."FimUtc";
ROLLBACK;
```

Se uma tabela/migration estiver ausente, adaptar o inventário à versão real;
não criar objetos para fazer as consultas passarem. Completar com agregados
de FKs órfãs, duração/intervalos inválidos, ocupação ausente, calendário não
conforme, bloqueios versus reservas e destinos de envio não privados. Revisar
pendências/JSON que possam referenciar EquipeId. Apenas contagens em relatório;
detalhes necessários à decisão ficam em canal privado controlado.

## 5. Migração incremental proposta — sem execução nesta etapa

### A. PrepararAgendaUnica

Após inventário e aprovação, ensaiar em cópia populada isolada. Gerar nova
migration, sem tocar nas quatro anteriores, para adicionar Versao com default 1,
check/token EF e índices globais úteis. Preparar área `legado_mvp` com manifesto
de versão/data/contagens e cópias verificadas da relação AgendamentoId→EquipeId
e do escopo BloqueioId→EquipeId. Acesso restrito e sem publicação de PII.
Tabelas ativas e suas relações antigas permanecem funcionais nessa fase.

Capturar também dependências fora do EF, pendências e envios legados. Não
transformar payload histórico de inbox nem reexecutar comandos antigos que
foram pausados sem uma decisão explícita. Conferir hashes/contagens de cópia
antes de avançar, sem tratar a cópia como substituta de backup completo.

### B. Reconciliar e preservar

Produzir backup completo consistente de cada banco e ensaiar restauração em
uma base nova; registrar versões, contagens e integridade, com destino privado.
[pg_dump](https://www.postgresql.org/docs/17/app-pgdump.html) copia um banco;
roles e demais objetos globais exigem inventário próprio.

Serviços simultâneos antes válidos em equipes diferentes passam a conflitar.
Cada conflito, bloqueio individual, exceção aplicada e reserva fora da jornada
fixa precisa de decisão explícita da administradora. Não escolher equipe,
cancelar, mover, fundir bloqueios ou recalcular períodos silenciosamente.
Mudanças corretivas serão operações auditadas próprias; história concluída ou
cancelada não será reescrita para se ajustar às novas regras.

Durante a janela final, parar novos claims/escritas, concluir ou cancelar
transações em voo, manter recebimento durável conforme plano operacional e
repetir o inventário antes de consolidar. Tratar pendências e envios de grupos
como legados bloqueados para revisão; não reenviá-los para o privado por inferência.

### C. ConsolidarAgendaUnica

Somente após conflitos ativos zerados/decididos, arquivo conferido e autorização:

1. Gerar migration incremental de consolidação com SQL revisado na cópia.
2. Remover FK/índice/coluna EquipeId de agendamentos e de bloqueios, preservando
   os mapas correspondentes em legado_mvp. Aplicar índices globais restantes.
3. Mover tabelas equipes e excecoes_expediente e suas dependências necessárias
   para legado_mvp, preservando dados/PKs/FKs internas. Retirar os mapeamentos
   ativos; não emitir DropTable automático sem substituir pelo arquivamento
   revisado. Conferir schema de sequences/identidades e permissões.
4. Publicar aplicação sem EquipeId, com Versao e protocolo global; atualizar
   snapshot EF e metadados da nova migration. Não expor tabelas legadas às Tools.
5. Validar contagens/IDs/FKs/ocupações/auditoria, ausência de conflitos ativos,
   texto autorizado e recuperação da inbox antes de reabrir processamento.

Não remover Cliente, TipoServico, Agendamento, PeriodoOcupacao, Auditoria,
SolicitacaoPendente, EnvioTelegram, inbox ou barreira idempotente. Nenhuma limpeza
de dados legados faz parte do MVP; retenção/descarte exige decisão posterior.

A geração EF precisa de revisão manual para preservar dados em vez de aceitar
operações destrutivas inferidas. Referência: [gerenciamento de migrations EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing).

## 6. Estratégia de retorno e testes exigidos

Antes de novas escritas, retorno pode restaurar backup/cópia anterior com
aplicação compatível e fila preservada, sob procedimento ensaiado. Depois de
novos agendamentos sem equipe, Down não consegue inferir responsável antigo:
**não há rollback automático sem perda garantido**. Planejar correção adiante
ou retorno coordenado com exportação/reconciliação dos dados novos e decisão
explícita. Nunca executar database update 0, DROP DATABASE ou TRUNCATE em bases
existentes como atalho de migração.

Critérios para liberar #79:

- Upgrade de banco vazio e cópia populada com equipes, agendados simultâneos,
  concluídos/cancelados, ocupações, exceções, bloqueios e auditoria.
- Preflight aborta em conflito ou arquivo incompleto; IDs/FKs e contagens
  preservados após consolidação, inclusive histórico e inbox.
- Casos de 240 minutos, 1/7 dias, início parcial e virada de mês/ano.
- Duas conexões disputando reserva, reserva/bloqueio, edição e reabertura;
  uma vence e a outra revalida com a versão atual sem estado parcial.
- Reentrega/restart/replay de confirmação não duplicam efeito.
- Backup/restore e estratégia de retorno após escrita nova ensaiados em cópia.

## 7. Evidências da limpeza #78

Treze arquivos exclusivos de voz foram retirados de Application, Infrastructure
e Tests; removidos options, cliente e feedback de DI/configuração. Parser não
projeta voice/audio e autorização requer texto privado não vazio. Testes cobrem
linhas legadas e projeções novas sem executar comando. IAiModel, GeminiAiModel,
GeminiChatClientFactory, webhook, persistência, executor idempotente, Domain,
configurações EF e migrations existentes não foram modificados.

Validação executada: restore concluído; build Release com 0 avisos/erros;
111 testes aprovados, 9 ignorados, 0 falhas, total 120. Os sete PostgreSQL
permanecem não executados e dois smokes Gemini continuam sem opt-in. Nenhuma
chamada ao Google ou operação PostgreSQL foi executada nesta entrega.
