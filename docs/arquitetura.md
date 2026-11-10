
# IA.N — Arquitetura de Software

**Projeto:** IA.N — Agente de IA para Gestão de Agendas  
**Referência de requisitos:** `docs/requisitos/DRS.md`  
**Stack:** .NET 10, PostgreSQL, EF Core, Telegram, MAF e Gemini

## 1. Objetivo

O IA.N é um sistema de gestão de agendas para uma loja de
móveis planejados, operado por uma administradora por meio
do Telegram.

A aplicação deve interpretar solicitações em linguagem
natural, executar operações autorizadas e preservar a
consistência dos dados e das agendas.

Este documento descreve a arquitetura aprovada, os
componentes existentes e as responsabilidades técnicas.

O estado de implementação foi consultado na branch `dev`,
commit `1d26bda`, em 10/11/2026.

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

Entidades existentes em `Ian.Domain/Entities/`:

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

Eventos de grupos não concedem autorização
administrativa.

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

A integração ainda está planejada na versão
do código consultada.

O modelo específico do Gemini e a estratégia
de processamento de áudio ainda exigem definição.

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

### 7.2 Modelo de agendas

Agendamento representa o compromisso e seus
dados de negócio.

PeriodoOcupacao representa os intervalos
efetivamente reservados.

Essa separação permite representar serviços
que ocupam múltiplos dias e horários específicos.

ExcecaoExpediente define alterações na jornada
de uma equipe em determinada data.

BloqueioAgenda representa indisponibilidade e
não deve ser confundido com redução de jornada.

As regras detalhadas estão no DRS.

### 7.3 Concorrência

Não basta consultar disponibilidade antes
de inserir um agendamento.

Operações concorrentes devem preservar a
invariante de ausência de sobreposição para
a mesma equipe.

A estratégia física completa de concorrência
para agendamentos permanece uma pendência
técnica do DRS.

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

- Considerar as equipes e serviços do dia.
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

As opções de Telegram são validadas na
inicialização da aplicação.

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
