
# IA.N — Arquitetura de Software

**Projeto:** IA.N — Agente de IA para Gestão de Agendas  
**Referência de requisitos:** `docs/requisitos/DRS.md`  
**Stack:** .NET 10, PostgreSQL, EF Core, Telegram, MAF e Gemini

## 1. Objetivo

O IA.N gerencia uma única agenda de uma loja de móveis planejados,
operada exclusivamente por uma administradora em conversa privada no
Telegram, somente por texto. O MVP não inclui equipes, grupos, jornadas
excepcionais, folgas ou dashboards.

A aplicação deve interpretar solicitações em linguagem
natural, executar operações autorizadas e preservar a
consistência dos dados e das agendas.

Este documento descreve a arquitetura aprovada, os
componentes existentes e as responsabilidades técnicas.

A base consultada é `origin/dev`, commit `207be7a`, em 10/11/2026.
A #78 remove a preparação de voz dessa base; a #79 acompanha a futura
migração do modelo legado. Nenhuma alteração de esquema integra a #78.

As funcionalidades descritas como planejadas não devem
ser consideradas implementadas sem verificação no código.

## 2. Estilo arquitetural

O sistema utiliza um monólito modular com separação
em camadas e responsabilidades.

Existe uma única aplicação principal, `Ian.Api`,
responsável por hospedar os endpoints HTTP e os
serviços internos em segundo plano.

Não haverá um processo `Ian.Worker` separado no MVP.

A separação em projetos não significa a existência
de microsserviços independentes.

### 2.1 Princípios

- Responsabilidade bem definida para cada camada.
- Regras de negócio independentes de infraestrutura.
- Casos de uso como pontos de entrada das operações.
- Dependências externas abstraídas quando apropriado.
- Persistência transacional e recuperação de falhas.
- Autorização validada no backend.
- Testes automatizados para comportamentos críticos.
- Complexidade proporcional às necessidades do MVP.

Não introduzir novas camadas, projetos, mediadores,
barramentos ou padrões arquiteturais sem necessidade
demonstrada pela issue.

## 3. Estrutura da solução

```text
Ian.slnx
|
|-- Ian.Api/
|-- Ian.Application/
|-- Ian.Domain/
|-- Ian.Infrastructure/
|-- Ian.Tests/
```

### 3.1 Ian.Api

Responsável por:

- Inicialização e hospedagem da aplicação.
- Exposição dos endpoints HTTP.
- Recebimento dos webhooks Telegram.
- Configuração do pipeline HTTP.
- Composição das dependências.
- Hospedagem dos serviços internos.

Arquivos existentes de referência:

- `Ian.Api/Program.cs`
- `Ian.Api/ApiHost.cs`
- `Ian.Api/Endpoints/TelegramWebhookEndpoint.cs`

Rotas existentes:

- `GET /app/v1/health`
- `POST /webhooks/telegram`

A API não deve concentrar regras de negócio dos
agendamentos.

### 3.2 Ian.Application

Responsável por:

- Casos de uso da aplicação.
- Contratos necessários às integrações.
- Orquestração de operações.
- Coordenação de autorização e fluxos.
- Comunicação com serviços abstraídos.

Componentes existentes:

- `TelegramUpdateParser`
- `TelegramUpdateAuthorization`
- `TelegramUpdateDispatcher`
- `ITelegramUpdateInboxWriter`
- `ITelegramInboxProcessingStore`

A camada Application pode depender de Domain.

Não deve depender diretamente dos projetos Api
ou Infrastructure.

### 3.3 Ian.Domain

Responsável por:

- Entidades de negócio.
- Estados e conceitos do domínio.
- Invariantes e validações de negócio.
- Comportamentos independentes de tecnologia.

Entidades existentes em `Ian.Domain/Entities/` (modelo legado, ainda sem a migração do MVP):

- Cliente
- Equipe
- TipoServico
- Agendamento
- PeriodoOcupacao
- ExcecaoExpediente
- BloqueioAgenda
- SolicitacaoPendente
- RegistroAuditoria
- EnvioTelegram
- TelegramUpdateInbox
- TelegramBusinessOperation

A existência de uma entidade não significa que todos
os casos de uso associados estejam implementados.

Domain não deve depender das outras camadas nem
de EF Core, Telegram ou Gemini.

### 3.4 Ian.Infrastructure

Responsável por:

- Persistência com Entity Framework Core.
- Comunicação com PostgreSQL.
- Implementações dos contratos da aplicação.
- Integrações com serviços externos.
- Processamento técnico da inbox Telegram.
- Registro de serviços na injeção de dependências.

Componentes existentes:

- `Persistence/IanDbContext.cs`
- `Persistence/Configurations/`
- `Persistence/Migrations/`
- `Persistence/TelegramUpdateInboxWriter.cs`
- `Persistence/PostgresTelegramInboxProcessingStore.cs`
- `Persistence/TelegramBusinessOperationExecutor.cs`
- `Telegram/TelegramInboxBackgroundService.cs`
- `Telegram/TelegramInboxConsumer.cs`
- `DependencyInjection.cs`

A camada Infrastructure pode depender de
Application e Domain.

### 3.5 Ian.Tests

Responsável por testes automatizados:

- Testes unitários.
- Testes de integração.
- Testes de endpoints.
- Testes de autorização.
- Testes de persistência.
- Testes de concorrência e idempotência.

O projeto utiliza xUnit e ferramentas de testes .NET.

Os testes devem cobrir os critérios de aceite e
os riscos relevantes de cada alteração.

## 4. Dependências entre projetos

Dependências atualmente estabelecidas:

```text
Ian.Api
  |-- Ian.Application
  |-- Ian.Infrastructure

Ian.Infrastructure
  |-- Ian.Application
  |-- Ian.Domain

Ian.Application
  |-- Ian.Domain

Ian.Domain
  |-- nenhuma dependência de projeto

Ian.Tests
  |-- referências aos projetos testados
```

As dependências devem permanecer direcionadas
para as abstrações e conceitos apropriados.

Evitar referências circulares.

## 5. Integração com Telegram

### 5.1 Recebimento

A comunicação de entrada utiliza webhook HTTPS.

Rota:

`POST /webhooks/telegram`

O endpoint valida o cabeçalho:

`X-Telegram-Bot-Api-Secret-Token`

O recebimento segue o fluxo:

1. Telegram envia uma atualização HTTP.
2. API valida o segredo do webhook.
3. API valida a estrutura da atualização.
4. API persiste o evento no PostgreSQL.
5. API confirma o recebimento com HTTP 2xx.
6. O serviço interno processa o evento armazenado.
7. O dispatcher avalia a autorização e encaminha
   a atualização para o handler apropriado.

O endpoint não deve executar Gemini nem operações
administrativas demoradas antes de responder.

### 5.2 Segurança

A validação do segredo do webhook e a autorização
da administradora são controles independentes.

Somente o Telegram User ID autorizado pode executar
operações administrativas.

Somente mensagens com texto não vazio em chat privado, de remetente humano
com o ID autorizado, chegam ao handler administrativo. Mídia e legendas
não são comandos. Os metadados voice/audio não são projetados pelo parser.
Eventos de grupos não concedem autorização administrativa; a descoberta
legada de candidatos ainda existe tecnicamente, sem vínculo ou envio, e
será removida na #79.

### 5.3 Processamento existente

O processamento utiliza:

- `TelegramInboxBackgroundService`
- `TelegramInboxConsumer`
- `TelegramUpdateDispatcher`
- `PostgresTelegramInboxProcessingStore`

Os eventos são reivindicados no PostgreSQL usando
controle de concorrência, lease e token de posse.

Os estados de processamento existentes incluem:

- Received
- Processing
- Completed
- FailedRetryable
- FailedPaused

O processamento deve permitir retomada após
interrupções e limitar novas tentativas.

### 5.4 Limitações atuais

Os handlers administrativos definitivos ainda
não estão implementados na versão consultada.

`DeferredTelegramHandlers` sinaliza que esses
handlers estão indisponíveis.

Portanto, a infraestrutura de recebimento e
processamento existe, mas o fluxo funcional completo
do agente ainda não está concluído.

## 6. Integração com inteligência artificial

### 6.1 Tecnologias aprovadas

- Microsoft Agent Framework (MAF).
- Gemini via Google AI Studio.

A implementação da #19 disponibiliza `IAiModel` em Application e
`GeminiAiModel` em Infrastructure. O adaptador invoca efetivamente
`ChatClientAgent.RunAsync` do MAF, sobre `IChatClient` fornecido pelo
SDK oficial `Google.GenAI`. As versões verificadas são MAF 1.28.0 e
Google.GenAI 2.0.0, com Microsoft.Extensions.AI 10.11.0 transitivo.

O modelo escolhido pelo usuário é `gemini-3.1-flash-lite`. O acesso e o
Function Calling básico na conta foram validados nos dois smokes reais
autorizados de 10/11/2026, usando somente conteúdo sintético.
A integração é textual e ainda não substitui os handlers Telegram provisórios.

O contrato retorna texto/uso ou falha tipada, sem tipos dos SDKs.
O chamador deve validar autorização antes de invocá-lo. Cada chamada
usa um agente/sessão novo; não há persistência de conversas nesta etapa.
Não existem ferramentas registradas em produção, e chamadas de função
inesperadas são rejeitadas como resposta inválida. A demonstração sem
efeitos colaterais vive somente em Ian.Tests.

`GeminiChatClientFactory` gerencia o transporte e o cliente compartilhado
sem histórico, e os descarta com o contêiner DI. Retries do SDK estão
limitados a uma tentativa, sem repetição automática. O timeout inclui
toda a invocação; cancelamento externo é propagado. Logs e resultados
de falha não expõem prompts, respostas completas, chave ou exceções do
fornecedor. Ver `docs/spikes/maf-gemini.md` para a matriz de erros e
as evidências locais/reais e as limitações de validação.

### 6.2 Responsabilidades do agente

O agente poderá:

- Interpretar intenções.
- Identificar informações ausentes.
- Solicitar esclarecimentos.
- Recuperar o contexto persistido.
- Selecionar ferramentas disponíveis.
- Formular respostas em texto.

O agente não deve:

- Alterar diretamente o banco de dados.
- Ignorar validações de negócio.
- Executar comandos administrativos não autorizados.
- Inventar resultados de ferramentas.
- Confirmar operações não persistidas.

### 6.3 Ferramentas do agente

As futuras ferramentas devem invocar operações
controladas pela aplicação.

A autorização, a consistência dos dados,
as transações e as regras de disponibilidade
continuam sendo responsabilidades do backend.

## 7. Persistência

### 7.1 Tecnologias

- PostgreSQL.
- Entity Framework Core.
- Provider Npgsql.

O contexto existente é:

`Ian.Infrastructure/Persistence/IanDbContext.cs`

As configurações de mapeamento utilizam classes
localizadas em `Persistence/Configurations/`.

As migrations ficam em:

`Ian.Infrastructure/Persistence/Migrations/`

### 7.2 Modelo implementado e proposta do MVP

O código ainda mapeia `Agendamento.EquipeId`, `Equipe`,
`ExcecaoExpediente` e `BloqueioAgenda.EquipeId`. Essas estruturas não
representam o novo escopo aprovado. Nenhum serviço de cálculo ou caso de
uso de agendamento está implementado; existem entidades e configurações EF.

A proposta final de `Agendamento` preserva Id, ClienteId, TipoServicoId,
Modalidade, Duracao, UnidadeDuracao, InicioPrevistoUtc, FimPrevistoUtc,
EnderecoSnapshot, Observacoes e Estado; remove EquipeId e acrescenta
Versao (long, padrão 1, token de concorrência). Não haverá entidade Agenda
nem AgendaId para uma única agenda.

`PeriodoOcupacao` já não possui EquipeId: mantém Id, AgendamentoId,
InicioUtc e FimUtc e a cardinalidade 1:N. Cada intervalo representa
ocupação efetiva `[início,fim)` na única agenda. O expediente proposto é
fixo, 08h–18h em America/Sao_Paulo, todos os dias. Dias corridos contam
as datas consecutivas, incluindo a data inicial parcial; não há recálculo
por folgas ou exceções. Bloqueios da agenda única recusam conflitos,
sem prorrogar ou mover serviços automaticamente.

A migração deve preservar equipes antigas, seus vínculos, exceções e o
escopo dos bloqueios em arquivo legado antes da retirada do modelo ativo.
O plano com entidades, FKs, índices, pré-validação e retorno está em
[docs/planejamento/migracao-agenda-unica.md](planejamento/migracao-agenda-unica.md).
**Proposta para revisão; não implementada nem executada nesta entrega.**

### 7.3 Concorrência proposta

O banco atual possui checks de intervalo, FKs e índices, mas não uma
constraint de exclusão de sobreposições. A agenda única exigirá que todos
os casos de uso que alterem reservas ou bloqueios adquiram o mesmo
`pg_advisory_xact_lock` antes de consultar conflitos e gravar, na mesma
transação READ COMMITTED. A consulta após adquirir o lock considera
ocupações de agendamentos Agendados e bloqueios globais. Assim não há
corrida entre duas reservas ou entre reserva e bloqueio no fluxo da aplicação.

Versao evita sobrescrever edições e consumir confirmações obsoletas.
Todas as mutações devem usar o protocolo; SQL administrativo fora dele
não recebe essa garantia. Testes PostgreSQL de duas conexões são obrigatórios.
O protocolo ainda não foi implementado, e uma consulta prévia sem lock
não constitui proteção concorrente.

### 7.4 Transações

Operações de negócio que precisam ser atômicas
devem utilizar transações adequadas.

O sistema não deve confirmar resultados parciais
como operações concluídas.

## 8. Idempotência e recuperação

O recebimento utiliza `update_id` para identificar
atualizações Telegram e evitar inserções duplicadas.

O processamento usa locks, leases e tentativas
controladas para recuperação.

Existe uma barreira de idempotência de operações
de negócio implementada por:

`TelegramBusinessOperationExecutor`

A chave da operação é composta por:

`(UpdateId, OperationKey)`

O registro dessa chave e a escrita correspondente
devem ser confirmados na mesma transação.

Os futuros handlers de escrita devem respeitar
esse mecanismo ou adotar proteção equivalente
devidamente justificada.

Integrações externas não devem presumir
garantia absoluta de execução exatamente uma vez.

## 9. Processamentos periódicos

A arquitetura prevê serviços periódicos
hospedados no processo da API.

O resumo diário deverá ser executado às
07h50, no fuso `America/Sao_Paulo`.

Ele deverá:

- Considerar os serviços do dia na agenda única.
- Enviar somente ao privado da administradora autorizada.
- Incluir serviços prolongados em execução.
- Não enviar mensagem sem serviços Agendados.
- Registrar sua execução.
- Evitar duplicidade após reinicializações.

O agendador e seu controle persistente de
execução ainda não estão implementados na
versão consultada.

## 10. Configurações e credenciais

Não armazenar credenciais reais no repositório.

Utilizar os mecanismos de configuração do .NET,
incluindo User Secrets em Development ou
variáveis de ambiente.

Configurações existentes:

- `Telegram:BotToken`
- `Telegram:AdministratorUserId`
- `Telegram:WebhookSecret`
- `Telegram:PublicBaseUrl` (opcional)
- `ConnectionStrings:Default`
- `Gemini:ApiKey` (secreta)
- `Gemini:Model`
- `Gemini:TimeoutSeconds`

As opções de Telegram são validadas na
inicialização da aplicação.

Gemini é validado na invocação: configuração ausente/inválida resulta
em `InvalidConfiguration`, sem chamada ao provedor. Isso preserva o
startup e os endpoints existentes enquanto a IA não estiver ativada.

Logs não devem expor mensagens privadas,
tokens, telefones, endereços ou segredos.

## 11. Observabilidade

Utilizar logging estruturado e identificadores
de correlação.

A API utiliza TraceId W3C.

O processamento da inbox associa operações
ao identificador da atualização Telegram.

Falhas devem produzir informações suficientes
para diagnóstico, sem revelar dados sensíveis.

Não registrar sucesso antes da confirmação
da operação correspondente.

## 12. Validação técnica

Executar na raiz da solução:

```powershell
dotnet restore Ian.slnx
dotnet build Ian.slnx --configuration Release --no-restore
dotnet test Ian.slnx --configuration Release --no-build --no-restore
```

A integração contínua existente usa GitHub Actions,
executando restauração, compilação e testes.

Os Pull Requests de funcionalidades devem
ser direcionados à branch `dev`.

Os resultados de testes só devem ser considerados
válidos quando efetivamente executados.

## 13. Evolução da arquitetura

A evolução deve ser guiada pelas issues e
pelos critérios de aceite.

Antes de adicionar um componente, avaliar:

1. Qual problema concreto ele resolve.
2. Em qual camada deve ser implementado.
3. Se já existe responsabilidade equivalente.
4. Quais invariantes podem ser afetadas.
5. Quais testes e documentos precisam mudar.

Não introduzir novos microsserviços, bancos de dados,
filas externas ou frameworks sem justificativa
e aprovação quando alterarem a arquitetura.

Quando uma decisão técnica alterar o desenho
aprovado, documentá-la e manter o DRS coerente.

## 14. Referências

- `docs/requisitos/DRS.md`: regras e requisitos aprovados.
- `AGENTS.md`: instruções operacionais do Codex.
- `README.md`: comandos de execução e configuração.
- GitHub Issues: escopo e critérios de cada tarefa.

Em caso de divergência entre documentação e código,
não presumir que a implementação esteja correta
ou que o documento esteja desatualizado.

Investigar a divergência antes de modificar
o comportamento aprovado.
