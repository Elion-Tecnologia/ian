# IA.N — Documento de Requisitos de Software (DRS)

**Versão:** 2.1 — MVP com agenda única
**Decisão inicial:** 10/11/2026
**Revisão de escopo aprovada:** 11/11/2026
**Substitui:** escopo funcional da versão 1.0 para o MVP
**Estado:** requisitos abaixo aprovados; modelo físico da #79 aplicado à origem de desenvolvimento; catálogo fixo implementado na #24
**Repositório:** Elion-Tecnologia/ian

## 1. Objetivo e escopo

O IA.N auxilia uma única administradora de uma loja de móveis planejados
no cadastro de clientes e na gestão de serviços e agendamentos de uma
única agenda. A interação ocorre exclusivamente por texto em conversa
privada com o bot Telegram. O backend valida autorização, dados,
confirmações, disponibilidade, transações e auditoria.

O MVP inclui criar, consultar, editar, reagendar, concluir e cancelar
individualmente agendamentos; consultar disponibilidade; manter clientes
reutilizáveis e catálogo fixo de serviços; recuperar solicitações incompletas
e enviar respostas textuais normais no privado da administradora.

Estão fora do MVP: equipes, múltiplas agendas, grupos Telegram, transferência
entre equipes, jornadas excepcionais, folgas, dashboards/painéis web,
comandos por mídia, atendimento de clientes finais, WhatsApp, múltiplas
administradoras, exclusão definitiva de agendamentos, mutações em lote,
CRUD livre do catálogo, alertas automáticos de atraso e hospedagem contratada
nesta etapa. A frente #17 foi encerrada como Not planned, a PR #77 foi
encerrada sem merge e a #78 remove sua infraestrutura integrada anteriormente.

Bloqueios administrativos e resumo automático às 07h50 foram retirados do
MVP; #38, #53 e #50 foram encerradas como Not planned. A prorrogação manual
#40 fica aberta como pós-MVP, sem bloquear a entrega. Reabertura também
fica fora das seis operações do MVP. Consultas sob demanda e o envio das
respostas correspondentes permanecem. BloqueioAgenda foi retirada do modelo ativo na #79; as novas migrations arquivam seus dados históricos.

## 2. Atores e canais

- Administradora: única pessoa com Telegram User ID autorizado; comandos
  somente em chat privado, de remetente humano, com texto não vazio.
- Cliente: cadastro reutilizável e histórico de serviços, sem acesso ao bot.
- Webhook HTTPS: entrada autenticada de updates; autenticação do transporte
  não substitui autorização por usuário e chat.
- Telegram Bot API: respostas textuais apenas no privado da administradora.

Mídia com legenda não vira comando. Atualizações sem comando podem ser
persistidas e concluídas tecnicamente sem chamar Gemini ou Tools.

## 3. Arquitetura e requisitos técnicos preservados

| ID | Requisito |
| --- | --- |
| RT-001 | ASP.NET Core Web API .NET 10 em monólito modular. |
| RT-002 | Telegram exclusivamente por webhook HTTPS, sem Long Polling. |
| RT-003 | Validar X-Telegram-Bot-Api-Secret-Token antes de aceitar o update. |
| RT-004 | Persistir o update no PostgreSQL antes de HTTP 2xx. |
| RT-005 / RT-006 | Processar fora da requisição HTTP em BackgroundService hospedado em Ian.Api; sem Worker separado. |
| RT-007 / RT-009 | Deduplicar por update_id, recuperar leases expirados e impedir efeitos de negócio duplicados. |
| RT-008 | PostgreSQL com EF Core; migrations incrementais e transações. |
| RT-010 | Backend valida autorização, confirmações e negócio; o LLM não altera dados diretamente. |
| RT-011 | Credenciais fora do código/logs; minimização de conteúdo privado e dados pessoais. |
| RT-012 | Execução inicial no macOS com túnel HTTPS; sem garantia 24/7 com host offline. |
| RT-014 | Impedir reservas conflitantes na agenda única, inclusive sob concorrência. |
| RT-015 | Testes de regras críticas, persistência, webhook, autorização e idempotência. |

RT-013 (resumo programado) foi retirado nesta revisão. O BackgroundService
da inbox e a recuperação de respostas pendentes permanecem necessários.

Projetos: Ian.Api hospeda endpoints/composição; Ian.Application coordena casos
de uso e contratos; Ian.Domain contém entidades/regras sem dependência de Api
ou Infrastructure; Ian.Infrastructure implementa EF Core/integrações;
Ian.Tests valida os comportamentos.

MAF e Gemini são preservados. `IAiModel`, `GeminiAiModel` e
`GeminiChatClientFactory` permanecem intactos. O modelo configurado é
`gemini-3.1-flash-lite`; versões e evidências da integração textual estão
em `docs/spikes/maf-gemini.md`. Novas chamadas reais exigem autorização
específica para quota/eventual cobrança.

### 3.1 Recebimento, recuperação e idempotência

O endpoint POST /webhooks/telegram valida o segredo e a estrutura, persiste
uma projeção mínima e só então retorna 2xx. Esse 2xx confirma recebimento
durável, não interpretação, mutação, envio ou leitura humana.

A inbox implementada usa Received, Processing, Completed, FailedRetryable e
FailedPaused, com lease/token, heartbeat e tentativas limitadas. Updates
negados ou sem comando não executam handler administrativo. Os handlers
textuais de negócio ainda não existem; o provisório pausa comandos
administrativos com HandlerUnavailable para recuperação controlada.

A barreira `(UpdateId, OperationKey)` de TelegramBusinessOperationExecutor
deve ser confirmada na mesma transação da escrita. Intenções equivalentes
em updates diferentes exigem solicitação/confirmação persistente própria.
Integrações externas não oferecem garantia absoluta de exactly once.

## 4. Catálogo de serviços

| Serviço | Duração padrão | Modalidade padrão |
| --- | --- | --- |
| Visita técnica | 240 minutos (4 horas) | Externo |
| Montagem | 7 dias corridos | Externo |
| Ajuste | 7 dias corridos | Externo |
| Orçamento | 1 dia corrido | Interno |

A duração e a modalidade podem ser substituídas por agendamento sem alterar
catálogo ou compromissos anteriores. Modalidades: Interno, Externo, Remoto.
Endereço snapshot é obrigatório apenas para Externo. A #24 mantém os quatro
itens por seed EF idempotente, com IDs estáveis e consulta somente leitura;
não existe CRUD livre do catálogo.

## 5. Requisitos funcionais do MVP

| IDs preservados | Requisito vigente |
| --- | --- |
| RF-001 / RF-003 | Gerenciar agendamentos individuais na agenda única. |
| RF-002 / RF-006 / RF-007 | Catálogo fixo com quatro serviços e padrões de duração/modalidade. |
| RF-004 | Consultar por período, cliente, serviço e estado, combinando filtros; sem filtro de equipe. |
| RF-010 | Interpretar linguagem natural textual, coletar dados ausentes e chamar Tools autorizadas. |
| RF-011 / RF-012 | Receber comandos e responder exclusivamente em texto no privado autorizado. |
| RF-013 / RF-014 | Reutilizar clientes por telefone normalizado único e criar cliente/primeiro agendamento atomicamente. |
| RF-015 / RF-016 | Manter endereço principal e editar cliente sem alterar snapshots históricos. |
| RF-017 / RF-018 | Editar modalidade/dados de Agendado, validando impacto e preservando auditoria. |

RF-005 (resumo automático), RF-008 (programação de grupo) e RF-009
(equipes/vínculos) foram retirados.
Identificadores retirados não serão reutilizados para funcionalidades diferentes.

## 6. Regras de negócio vigentes

### 6.1 Identidade, estados e confirmações

- RN-001: somente a administradora autorizada executa comandos textuais privados.
- RN-003: cada agendamento registra cliente, serviço, modalidade, duração/unidade,
  início/fim previstos, endereço conforme modalidade, observações e estado.
  Telefone é obtido do cliente; não há EquipeId ou seleção de responsável.
- RN-004 / RN-005: existe uma única agenda; períodos efetivos de agendamentos
  Agendados não podem se sobrepor. Não há capacidade paralela por equipe.
- RN-006: alteração individual de duração não muda o catálogo.
- RN-007: estados Agendado, Concluido e Cancelado. Reagendamento é operação,
  não estado; vencer o prazo não muda o estado automaticamente.
- RN-008: cancelar exige confirmação e preserva identidade e histórico.
- RN-024: operações comuns inequívocas dispensam confirmação extra;
  cancelamento exige confirmação vinculada a ator, alvo, argumentos/versão
  e validade. Revalidar no commit.
- RN-027: múltiplos candidatos exigem seleção explícita por identidade.
- RN-028: somente mutações individuais; consultas podem retornar vários registros.
- RN-034: auditar alterações e resultados de forma transacional; rollback não
  deixa registro de sucesso. Minimizar dados pessoais em registros técnicos.
- RN-039: conclusão antecipada libera reservas futuras preservando histórico.
  Atraso não gera extensão; a operação dedicada de prorrogação foi adiada.
  Concluido/Cancelado não retornam a Agendado por edição no MVP.
- RN-040: atrasos são consultados sob demanda; não há alertas automáticos.

### 6.2 Tempo e ocupação

- RN-017 / RN-018: duração em DiasCorridos reserva uma janela completa
  08h–18h por data civil consecutiva, inclusive a primeira e os domingos.
  Uma semana são sete janelas completas; não é uma reserva contínua de 24h.
- RN-019: expediente fixo 08h–18h todos os dias no fuso America/Sao_Paulo.
  No MVP não há autorização excepcional fora dessa janela.
- RN-041: em DiasCorridos, sete dias a partir de segunda reservam
  segunda–domingo, sempre 08h–18h. Solicitação com horário diferente de 08h
  exige esclarecimento antes de gerar reserva; não assumir início parcial
  nem corrigir silenciosamente o horário. Sem horário, a regra fixa é 08h.
- Duração em Minutos consome minutos dentro do expediente: 240 minutos às
  16h ocupam 16h–18h e 08h–10h da data seguinte. Início deve ser >=08h e <18h;
  duração deve ser positiva. Não inventar exceção para início fora da janela.
- Todos os intervalos são semiabertos [início,fim); adjacência não é conflito.
  Persistir instantes em UTC e converter explicitamente as datas de negócio.
- RN-031 / RN-032: endereço obrigatório para Externo; herdar e permitir
  substituir modalidade/duração por agendamento.

### 6.3 Clientes, conversa e resiliência

- Telefone normalizado é obrigatório e único no banco; busca 0/1/N trata
  ausência e ambiguidade. Cadastro automático reutiliza identidade inequívoca.
- Editar o cliente não altera endereço snapshot de agendamentos anteriores.
- RN-025 / RN-026: persistir estado mínimo da solicitação, com uma mutação
  pendente por administradora. Consulta não descarta a pendência; substituí-la
  exige confirmação. Recuperar após reinício e tratar expiração.
- RN-029: tentativas de recuperação são limitadas; verificar idempotência
  antes de repetir escrita de resultado incerto.
- RN-030: coletar informação ausente e esclarecer ambiguidade antes de mutar.
  Um erro de Tool, banco ou integração nunca vira sucesso inventado.

### 6.4 Comunicação privada

- RN-036 / RN-038: consulta textual sob demanda da agenda única é organizada
  por dia e período, com horários efetivos, cliente, serviço, modalidade,
  endereço snapshot quando necessário e observações. Minimizar exposição.
- RN-037: período ausente em consulta de programação deve ser esclarecido.
- RN-046: registrar tentativas e aceitação pela API Telegram; aceitação não
  comprova leitura. Resultado incerto não deve virar sucesso ou retry cego.
- Respostas às solicitações da administradora são enviadas normalmente no
  privado; sua persistência e recuperação não dependem de job automático.

### 6.5 Regras retiradas nesta revisão

| IDs da versão 1.0 | Motivo |
| --- | --- |
| RN-009 | Exceção fora de expediente retirada. |
| RN-010 / RN-011 / RN-023 | Bloqueios administrativos retirados; registros legados preservados na #79. |
| RN-012 / RN-013 / RN-043 | Resumo automático retirado. |
| RN-033 | Reabertura fora das operações do MVP; histórico preservado. |
| RN-015 / RN-016 / RN-042 / RN-045 | Escolha, transferência, desativação e capacidade por equipe retiradas. |
| RN-020 / RN-021 / RN-022 | Exceções de jornada e compensação de folgas retiradas. |
| RN-035 / RN-044 | Atualização de programação de grupos e vínculo de grupo retirados. |

A duração em dias corridos exige janelas completas; prorrogação da RN-039
fica pós-MVP. O histórico do DRS anterior permanece no Git, sem funcionar
como requisito ativo.

## 7. Modelo e estado implementado

| Componente | Proposta final do MVP | Estado do código nesta entrega |
| --- | --- | --- |
| Cliente / TipoServico | Preservar cadastro, unicidade e catálogo | Entidades/mapeamentos presentes; catálogo fixo sem CRUD, seed e consulta read-only implementados na #24. |
| Agendamento | Sem EquipeId; campos abaixo | Sem EquipeId; Versao >=1 como token EF, incremento explícito por mutação. |
| PeriodoOcupacao | Intervalos da agenda única, relação 1:N | Entidade persistente preservada; motor puro de geração e sobreposição implementado na #30. |
| BloqueioAgenda | Fora do modelo ativo na #79; arquivar dados e relações históricas | Sem entidade/DbSet ativo; tabela original movida para legado_mvp pela consolidação. |
| Equipe / ExcecaoExpediente | Fora do modelo ativo; preservar dados legados | Fora do EF ativo; registros, IDs, FKs e sequences preservados em legado_mvp. |
| SolicitacaoPendente | Estado mínimo durável e versão da operação | Entidade parcial; ciclo conversacional pendente. |
| RegistroAuditoria / EnvioTelegram | Preservar histórico e saída privada | Entidades presentes; casos de uso pendentes. |
| TelegramUpdateInbox / TelegramBusinessOperation | Preservar controles técnicos | Recebimento, recuperação e executor idempotente implementados. |

Agendamento implementado: `Id: long`, `ClienteId: long`, `TipoServicoId: long`,
`Modalidade: ModalidadeServico`, `Duracao: int > 0`,
`UnidadeDuracao: Minutos | DiasCorridos`, `InicioPrevistoUtc: DateTimeOffset`,
`FimPrevistoUtc: DateTimeOffset > início`, `EnderecoSnapshot: string?`,
`Observacoes: string?`, `Estado: EstadoAgendamento` e `Versao: long >= 1`
para controle concorrente e invalidação de confirmações antigas.

Não haverá AgendaId nem tabela Agenda para uma agenda única. Cliente e serviço
continuam obrigatórios, com ON DELETE RESTRICT. A #79 implementa preparação e consolidação incrementais, validadas em bancos isolados e posteriormente aplicadas à origem de desenvolvimento com autorização explícita.
Ver [plano de migração](../planejamento/migracao-agenda-unica.md).

A #79 entrega persistência, arquivo legado e mecanismos básicos de concorrência.
A #30 implementa o motor de períodos e os testes funcionais de 1/7 dias e minutos.
#31–#33/#35 devem integrar disponibilidade, versões, confirmações e mutações.
Testes de callbacks sintéticos não comprovam os casos de uso completos.

## 8. Critérios de aceite e testes

| ID | Cenário obrigatório |
| --- | --- |
| CA-01 | Cliente novo e agendamento válido são gravados atomicamente; falha não confirma cadastro parcial. |
| CA-02 | Duas reservas concorrentes sobrepostas na agenda única: uma aceita e outra Conflict. Adjacentes são permitidas. |
| CA-03 | Serviço de sete dias iniciado segunda reserva sete janelas completas 08h–18h até domingo. |
| CA-05 | Cancelar exige confirmação válida e libera ocupações preservando histórico, auditoria e idempotência. |
| CA-09 | Solicitação textual incompleta retoma campos após reinício; mídia não executa comando. |
| CA-10 | Ambiguidade, confirmação obsoleta ou falha de Tool não grava nem inventa sucesso. |
| CA-11 | Segredo inválido rejeita webhook; falha de persistência impede HTTP 2xx. |
| CA-12 / CA-13 | Recuperar update após queda e reentrega sem duplicar efeitos; Gemini fora da requisição HTTP. |
| CA-14 | Envio com resultado incerto permanece rastreável, sem garantia falsa de exactly once externo. |
| CA-15 | Túnel/host indisponíveis não são reportados como operação contínua. |
| CA-17 | Migração de cópia populada preserva IDs, clientes, serviços, agendamentos, ocupações, auditoria e histórico legado, incluindo bloqueios. |
| CA-18 | DiasCorridos com horário informado diferente de 08h exige esclarecimento e não cria reserva parcial. |
| CA-19 | Consulta sob demanda gera resposta textual privada; falhas de envio ficam recuperáveis sem job de resumo. |

CA-04 (folga), CA-06 (grupo), CA-07 (atualização de grupos), CA-08 (resumo)
e CA-16 (bloqueios administrativos) foram retirados.
Testes usam dados sintéticos e fakes de Gemini/Telegram; PostgreSQL usa banco
isolado. Testes ignorados não são aprovados. Os sete testes PostgreSQL existentes e os novos testes de migração/concorrência foram executados em banco descartável. Smokes reais Gemini permanecem desativados.

## 9. Segurança, qualidade e operação

Credenciais ficam em fontes seguras do .NET. Logs usam correlação, sem
conteúdo integral de mensagens, prompts, dados pessoais ou autenticação.
Mutações críticas e auditoria são transacionais. Backups diários devem ter
retenção definida e restauração ensaiada. Não há garantia 24/7 em Mac desligado.
Hospedagem permanente, retenção e detalhes de
normalização de telefones continuam nas issues correspondentes.

## 10. Planejamento e migração

O backlog revisado está em [mvp-agenda-unica.md](../planejamento/mvp-agenda-unica.md).
A #78 limpou a entrada; a #79 implementa o modelo e as migrations incrementais. A dependência de
migração é explícita nas issues de agenda e não deve ser substituída pela
mera existência das entidades antigas da #12.

Em 11/11/2026, o inventário autorizado confirmou PostgreSQL 17.11 e 14 tabelas em ian_t03_tmp: nenhum registro de negócio, uma inbox Completed e um registro auxiliar de teste. Backup completo e restauração em ian_t79_restore_validation preservaram dados, chaves e sequences. Após o ensaio, as duas migrations da #79 foram aplicadas à origem com autorização e preservaram os registros técnicos e dez sequences. Testes destrutivos continuam restritos a cópias isoladas; ver plano e evidências da #79.

Fluxo Git: feature/* para dev por PR; main apenas após homologação e autorização.
Nenhum merge, inclusive squash, está implicitamente autorizado. A meta original
15/11/2026 deve ser reavaliada a partir das dependências, sem promessa de prazo.

## 11. Controle de versões

| Versão | Data | Alteração |
| --- | --- | --- |
| 1.0 | 05/11/2026 | Levantamento original; preservado no histórico Git. |
| 1.0 técnica | 09/11/2026 | Web API, webhook, PostgreSQL/EF, MAF/Gemini e processamento durável. |
| 2.0 MVP | 10/11/2026 | Agenda única, administradora privada por texto; retirada de equipes/grupos/exceções/folgas/dashboards; proposta física separada da implementação. |
| 2.1 MVP | 11/11/2026 | DiasCorridos com janelas completas desde a primeira data e esclarecimento de horário divergente; bloqueios/resumo retirados, prorrogação adiada e operação centrada nas seis ações aprovadas. |
