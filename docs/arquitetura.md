
# IA.N — Arquitetura de Software

**Projeto:** IA.N — Agente de IA para Gestão de Agendas  
**Referência de requisitos:** `docs/requisitos/DRS.md`  
**Stack:** .NET 10, PostgreSQL, EF Core, Telegram, MAF e Gemini

## 1. Objetivo

O IA.N gerencia uma única agenda de uma loja de móveis planejados,
operada exclusivamente por uma administradora em conversa privada no
Telegram, somente por texto. O MVP não inclui equipes, grupos, jornadas
excepcionais, folgas, bloqueios administrativos, resumo automático,
prorrogação manual, reabertura ou dashboards.

A aplicação deve interpretar solicitações em linguagem
natural, executar operações autorizadas e preservar a
consistência dos dados e das agendas.

Este documento descreve a arquitetura aprovada, os
componentes existentes e as responsabilidades técnicas.

Base: dev 700c220 (PR #81). A #79 implementa o modelo único e migrations incrementais. Após validação em bancos isolados, as duas migrations foram aplicadas à origem de desenvolvimento ian_t03_tmp com autorização explícita.

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
- `IClientes` / `ClientesApplication`
- `ICriacaoAgendamento` / `CriacaoAgendamentoApplication`
- `IConsultaAgenda` / `ConsultaAgendaApplication`
- `IDisponibilidadeAgenda` / `DisponibilidadeAgendaApplication`
- `FormatadorAgenda`

A camada Application pode depender de Domain.

Não deve depender diretamente dos projetos Api
ou Infrastructure.

### 3.3 Ian.Domain

Responsável por:

- Entidades de negócio.
- Estados e conceitos do domínio.
- Invariantes e validações de negócio.
- Comportamentos independentes de tecnologia.

Entidades existentes em `Ian.Domain/Entities/` (modelo ativo da #79):

- Cliente
- TipoServico
- Agendamento
- PeriodoOcupacao
- SolicitacaoPendente
- RegistroAuditoria
- EnvioTelegram
- TelegramUpdateInbox
- TelegramBusinessOperation

`SolicitacaoPendente` armazena, por usuário Telegram autorizado, apenas a
intenção, os campos coletados/faltantes e IDs candidatos necessários para
retomar uma mutação. Uma restrição única permite no máximo uma solicitação
Pendente por usuário. Consultas leem o estado sem substituí-lo; trocar intenção
exige confirmação, seleção ambígua exige um ID explícito, e solicitações expiram
após 30 minutos. O conteúdo original da conversa e prompts não são persistidos.
Na migration inicial, pendências placeholder sem campos recuperáveis são marcadas
Expirada, mantendo seus registros históricos.

A #22 acrescenta `IOperationConfirmation`: apenas cancelamento exige desafio
persistido com ID aleatório, ator, alvo, argumentos, versão e validade de até
cinco minutos, limitada à expiração da solicitação. A aceitação explícita deve
ser vinculada ao desafio pela orquestração confiável; um texto "sim" isolado ou
um argumento produzido pelo modelo não autoriza a escrita. Novo desafio,
atualização/seleção do contexto, substituição ou cancelamento invalidam o anterior.
O backend relê alvo/versão/estado sob o lock da agenda e consome a confirmação
na mesma transação do callback autorizado. Falhas revertem a mutação e o consumo.
O callback deve revalidar regras/disponibilidade, incrementar a versão e manter
histórico/auditoria; sua implementação funcional pertence à #35 e a integração
textual às Tools à #44.

Componentes de agenda puros em `Ian.Domain/Agenda/`:

- `CalculadoraOcupacao`: gera janelas em UTC para Minutos e DiasCorridos.
- `ResultadoCalculoOcupacao`: distingue cálculo, esclarecimento e entrada inválida.
- `PeriodoCalculado`: intervalo UTC semiaberto e teste de sobreposição.

`Ian.Domain/Clientes/NormalizadorTelefoneBrasileiro` concentra a regra pura de
normalização para 10/11 dígitos nacionais, removendo máscara e o prefixo `55`.

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
- `Persistence/PostgresTipoServicoCatalogo.cs`
- `Persistence/PostgresClienteStore.cs`
- `Persistence/PostgresAgendaCriacaoStore.cs`
- `Persistence/PostgresConsultaAgendaStore.cs`
- `Telegram/TelegramInboxBackgroundService.cs`
- `Telegram/TelegramInboxConsumer.cs`
- `DependencyInjection.cs`

A camada Infrastructure pode depender de
Application e Domain.

O catálogo fixo de `TipoServico` é populado por migration EF com IDs estáveis.
`ITipoServicoCatalogo` expõe somente projeções imutáveis; sua implementação
PostgreSQL usa consulta sem rastreamento. Duração e modalidade copiadas para
um agendamento continuam independentes dos padrões do catálogo.
A idempotência do seed é garantida pelo histórico de migrations do EF Core:
a migration executa inserts normais uma única vez e falha diante de conflitos,
sem UPSERT silencioso. Ela também preserva uma sequence já mais adiantada.

O cadastro de clientes é coordenado por `ClientesApplication`. A preparação
valida e adiciona um novo `Cliente` ao `IanDbContext`, mas não chama
`SaveChanges`, permitindo que a #31 use o mesmo contexto e a mesma transação do
primeiro agendamento. `PostgresClienteStore` mantém as consultas sem tracking e
trata como duplicidade somente a `UniqueViolation` da constraint de telefone;
outras falhas de banco são propagadas. A busca por nome usa `ILIKE` parametrizado,
escapa curingas, limita a resposta e mantém ordenação estável.

`CriacaoAgendamentoApplication` aplica catálogo e cálculo no Application e entra
no protocolo `IAgendaMutationExecutor` antes de preparar o cliente e consultar a
disponibilidade definitiva. `PostgresAgendaCriacaoStore` persiste o agendamento e
seus períodos e vincula a barreira idempotente ao ID confirmado. O cliente novo
pode receber ID por `SaveChanges` intermediário, mas permanece dentro da mesma
transação ainda não confirmada; conflito ou falha reverte todas as escritas.

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
Eventos de grupos são negados, inclusive payloads históricos de associação. Candidatos, handlers exclusivos, projeções de associação e DI de grupos foram removidos na #79. A inbox histórica não é reescrita.

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

A #44 registra `AgendaTelegramHandler` no dispatcher autorizado. O fluxo real é
webhook/inbox → handler → MAF/Gemini → Tools → Application → PostgreSQL →
resposta privada. `DeferredTelegramHandlers` não é mais o handler registrado.
Registros antigos pausados com HandlerUnavailable continuam preservados e
exigem reativação controlada; a #44 não os reativa automaticamente.
A #59 homologa o fluxo textual com PostgreSQL descartável e Telegram/Gemini
simulados. Os passos locais e o checklist estão em
[homologacao-local.md](homologacao-local.md); serviços reais exigem autorização
explícita, sem implantação nesta etapa.

### 5.5 Respostas textuais privadas

`ITelegramTextSender` (#18) usa a projeção da inbox e a autorização existente,
exigindo também que o chat de destino seja o privado da administradora. O host
deve chamá-lo após a persistência da inbox e o commit de negócio aplicável.
`TelegramTextClient` envia texto simples de até 4096 unidades UTF-16 ao endpoint
HTTPS fixo `sendMessage`, com previews e redirecionamentos desabilitados.

Por padrão, há timeout de 10 segundos por tentativa e 30 segundos para a operação
completa, incluindo leitura da resposta e esperas. Apenas rejeição explícita 429
com `parameters.retry_after` positivo autoriza repetição: no máximo três tentativas,
com espera de até dez segundos, respeitando o prazo total. 401 e demais rejeições
permanentes encerram o envio. 5xx, timeout, falha de transporte e resposta de sucesso
inválida são resultados incertos e não geram repetição automática.

O resultado informa status, número de tentativas, eventual prazo de espera e ID
da mensagem aceita. Aceitação pela API não comprova entrega/leitura. Logs em nível
Information incluem somente UpdateId, tentativa e status; token, URI autenticada,
texto, destinatário, corpo da resposta e exceções do provedor não são registrados.
O transporte é dedicado, sem handlers de logging do HttpClientFactory. O cliente
não altera negócios nem o estado da inbox; integração textual e recuperação de
saída persistente pertencem, respectivamente, à #44 e à #51.

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
A #44 usa o mesmo transporte na orquestração textual com Tools e substitui o
handler provisório pelo AgendaTelegramHandler.

O contrato retorna texto/uso ou falha tipada, sem tipos dos SDKs.
O chamador deve validar autorização antes de invocá-lo. Cada chamada
usa um agente/sessão novo. Esse adaptador textual permanece sem Tools e rejeita
chamadas de função inesperadas. A orquestração com Tools e contexto mínimo
persistido é responsabilidade de GeminiAgendaAgentRunner/AgendaAgentApplication
na #44; a demonstração isolada da #19 continua somente em Ian.Tests.

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

`IAgentToolRegistry` publica as sete funções de negócio: cadastrar_cliente,
localizar_cliente, listar_servicos, consultar_agenda, verificar_disponibilidade,
criar_agendamento e alterar_agendamento. Esta última conecta editar, reagendar,
concluir e preparar cancelamento às operações #33/#35. Cadastro/localização
reutiliza #25; catálogo #24; cálculo #30; criação transacional #31; consultas e
formatação #32. As unidades são Minutos/DiasCorridos e modalidades
Interno/Externo/Remoto, alinhadas ao Domain. Os contratos anteriores Horas/NaLoja
não correspondiam ao modelo implementado e foram substituídos.

A regressão #96 acrescenta esclarecer_pedido, sem escrita, para saudações ou
intenção desconhecida. A chamada inicial exige uma Tool (RequireAny); pedidos de
agendar usam criar_agendamento com dados parciais, inclusive sem argumentos,
persistindo a intenção desde o início. Não há classificação por palavras exatas.

`GeminiAgendaAgentRunner` usa o mesmo GeminiChatClientFactory e o MAF, com
FunctionInvokingChatClient limitado a 6 iterações, invocações sequenciais,
8 chamadas de Tools, 15 segundos por Tool e 60 segundos por execução. Prompts
limitam-se a 16.000 caracteres e resultados a 8.000; respostas Telegram têm até
4.000. O IAiModel/GeminiAiModel textual da #19 continua disponível sem Tools para
seus consumidores existentes; a orquestração usa IAgendaAgentRunner.

`AgendaAgentApplication` recebe somente contexto confiável do host. O handler e
o registro revalidam administradora, identidade do privado, mensagem textual e
origem humana. O modelo fornece argumentos de funções, nunca SQL, DbContext,
update_id, ator ou aceite de confirmação. Tools invocam casos de uso existentes;
padrões vêm do catálogo real. Consultas incluem IDs/versões para revalidação.

Pedidos incompletos usam IPendingConversation (#21), persistindo o request
normalizado, campos faltantes e candidatos. O campo interno request admite até
8.000 caracteres; o limite total do contexto permanece 12.000. Uma consulta não
descarta a pendência. Outro tipo de pedido é recusado até /descartar explícito.
Durante uma criação pendente, dados de cadastrar_cliente/localizar_cliente
alimentam o request original e continuam a criação. Cliente e agendamento novos
continuam sendo gravados juntos pela #31, sem cadastro intermediário independente.
Horário divergente em DiasCorridos é esclarecido junto com dados faltantes e não
pode ser corrigido por outra Tool no mesmo turno.
Localização ambígua pode manter a intenção LocalizarCliente, que apenas resolve
identidade e não cadastra ou agenda. Candidatos de cliente/agendamento exigem
texto cliente <ID> ou agendamento <ID> (ou ID isolado se houver um único conjunto).
O modelo não escolhe candidatos. O contexto é retomado após reinício e a última
resposta persistida do backend pode acompanhar o prompt como dado.

Cancelamento apenas prepara um desafio da #22. O handler reconhece o texto
exato confirmar <código do desafio>, relê a confirmação persistida e constrói
ConfirmationAcceptance fora do modelo. Um sim isolado e código antigo não
aceitam uma nova confirmação. A #22 revalida ator, alvo, argumentos, versão,
expiração e consumo dentro da transação da mutação. /descartar abandona somente
a intenção, sem cancelar um agendamento.

O texto final do modelo não comprova escrita. A resposta é produzida a partir
dos resultados do backend: mutação confirmada tem prioridade, assim como
perguntas, ambiguidades e recusas de negócio sobre consultas intercaladas.
Erros sanitizados não viram sucesso. No máximo uma tentativa de escrita é
executada por update; chamadas subsequentes não podem executar outra mutação.
Todos os dados de agendamento continuam sujeitos aos casos de uso e lock da #79.
Respostas em português não expõem os códigos de resultado. Resumos de sucesso
releem os dados persistidos (cliente, serviço, datas, duração, horários e endereço).
IDs e padrões continuam nos dados estruturados das Tools; IDs são apresentados
quando necessários à consulta/seleção explícita, sem usá-los em recibos de cadastro
ou criação. Respostas iguais de updates diferentes não são deduplicadas por texto;
reentregas do mesmo update continuam protegidas pelo diário/inbox existentes.

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

### 7.2 Modelo ativo e arquivo histórico

Agendamento preserva identidade, cliente, serviço, modalidade, duração/unidade,
início/fim previstos, endereço snapshot, observações e estado. Não possui EquipeId
ou AgendaId. Versao é long >=1, default 1, token de concorrência EF; o método
IncrementarVersao() deve ser chamado explicitamente nas mutações relevantes.
PeriodoOcupacao mantém Id, AgendamentoId, InicioUtc/FimUtc e relação 1:N.

Equipe, ExcecaoExpediente e BloqueioAgenda não estão no Domain/DbContext ativo.
As migrations PrepararAgendaUnica e ConsolidarAgendaUnica preservam snapshots,
mapa de equipe antiga, tabelas originais, chaves e sequences em legado_mvp.
As quatro migrations históricas não mudaram. Não há DropTable no upgrade.
Os Down novos recusam retorno automático para não inventar equipe nem perder dados.

A origem de desenvolvimento recebeu as duas migrations da #79 após
backup/restauração, inventário e upgrade isolado comprovados. Migrations
posteriores continuam sendo exercitadas primeiro somente em bases isoladas.
Procedimento e limites em [plano de migração](planejamento/migracao-agenda-unica.md).

### 7.3 Concorrência e transações

TelegramBusinessOperationExecutor possui a transação READ COMMITTED e a barreira
(UpdateId, OperationKey); rejeita transações aninhadas e mudanças pendentes.
AgendaMutationExecutor compõe esse executor e adquire PostgresAgendaLock antes
do callback. Ordem: transação → idempotência → lock global (790,1) → leitura
atualizada → validações/mutações/auditoria → commit. Rollback limpa o rastreamento.
O lock tem timeout de 5 segundos; não há retry automático de escrita.

Versão obsoleta e indisponibilidade do lock são falhas tipadas e sanitizadas.
A edição da #33 compara a versão esperada e revalida conflitos; os callbacks de
operações sensíveis devem também revalidar a confirmação
e incrementar Versao também quando apenas ocupações mudarem. O token EF detecta
UPDATE com versão original obsoleta; não implementa confirmação conversacional.

Testes com duas conexões comprovam serialização, releitura após commit,
versão obsoleta, rollback, timeout e idempotência. São testes de infraestrutura.
Não existe constraint de exclusão: todos os caminhos de escrita devem respeitar
o protocolo; SQL manual que o ignore não é protegido.

### 7.4 Limites da persistência e do motor

A #30 implementa o motor determinístico no Domain, com jornada fixa 08h–18h
todos os dias em America/Sao_Paulo e geração de janelas em minutos/dias corridos.
As verificações da migration apenas recusam legado incompatível, sem recalcular ocupações.
A #31 implementa criação com disponibilidade, transação, lock e idempotência.
A #32 implementa consultas 0/1/N, filtros combinados, disponibilidade e
formatação textual sem efeitos de escrita. A #33 implementa `IEdicaoAgendamento`:
releitura sob lock, versão esperada obrigatória, apenas Agendado, cálculo #30,
conflitos excluindo o próprio ID e substituição atômica dos períodos. Identidade,
cliente e estado são preservados; tipo, duração/unidade, modalidade, início,
endereço e observações podem ser editados sem alterar o catálogo. Incrementa
Versao e registra auditoria mínima transacional (ID, operação, update e versão),
sem dados pessoais nem histórico detalhado da #42 retirada. Rollback preserva
os valores e períodos anteriores; erros de banco/cancelamento são propagados.

`EditarAgendamentoSolicitacao` usa null para preservar campos. EnderecoServico e
Observacoes não nulos substituem os valores; AlterarEndereco/AlterarObservacoes
permitem limpá-los explicitamente. Endereço externo vazio é recusado. Nunca se
reconsulta o endereço do cliente para atualizar o snapshot. Data/horário omitidos
preservam o início local atual; converter para DiasCorridos com início diferente
de 08h exige esclarecimento, inclusive quando esse horário vem do compromisso.
Reentrega de `agendamento:edit` retorna o ID original sem executar a edição nem
atribuir uma versão atual ao resultado antigo.

Consultas agora expõem Versao para submissão da edição. A preparação de seleção
`SelecaoEdicaoAgendamentoApplication.BuscarAsync` reutiliza #32: zero não altera
nada, um devolve ID/versão, vários persistem até 20 candidatos via #21, sem escolher
automaticamente. `MaisResultados` exige refinar filtros para alcançar outros IDs.
Use `IPendingConversation.SelectCandidateAsync` com campo `Agendamento` para a
escolha explícita (a #21 persiste `AgendamentoId` nos campos coletados); releia
o alvo selecionado para obter a versão antes de editar.
Uma intenção já pendente não é substituída. A seleção e a edição são casos de uso
internos, integrados ao handler autorizado e às Tools na #44.

A #35 disponibiliza `IEncerramentoAgendamento`. `ConcluirAsync` exige update, ID e
versão esperada; `PrepararCancelamentoAsync` delega o desafio à #22 e
`CancelarAsync` exige `ConfirmationAcceptance` vinculado ao ator, alvo, argumentos,
versão e validade. A intenção pendente inequívoca já deve estar persistida conforme
#22 (campos `agendamentoId` e `operacao=cancelar`, sem faltantes/candidatos). O aceite
é produzido pela orquestração textual confiável, nunca pelo modelo. Conclusão é
operação comum sem confirmação extra; sua autorização de comando privado, como
na criação/edição, é feita pelo handler e pelo registro na #44.

Somente Agendado transita para Concluido ou Cancelado. Ambas as operações releem
sob lock global, incrementam Versao, vinculam a operação técnica ao agendamento e
auditam minimamente ID, operação, update, versão e instante UTC. O cancelamento
usa exclusivamente a transação de `IOperationConfirmation.ExecuteAsync`, inclusive
consumo do desafio e revalidação do prazo antes do commit; não inicia outra
transação. Conclusão usa `agendamento:complete`; cancelamento reutiliza a chave
`confirmation:<solicitacaoId>` da #22. Reentrega da conclusão retorna o resultado
original; a confirmação repetida é recusada conforme #22, sem repetir a escrita.

Períodos planejados são preservados integralmente como histórico, inclusive na
conclusão antecipada: tornam-se inativos com o estado terminal. Criação e
disponibilidade já filtram somente Agendado, liberando imediatamente as reservas
futuras/ativas após o commit. Consulta por ID/data/estado continua encontrando o
compromisso e seu planejamento original. Não são registrados períodos realizados
nem recalculada duração, endereço ou início/fim previstos. Rollback preserva
agenda, períodos, versão, auditoria, vínculo e consumo do desafio. Não há reabertura,
extensão automática, DELETE ou nova migration.

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

A migration `VincularOperacaoAoAgendamento` adiciona `AgendamentoId` nullable e
FK restritiva à operação técnica. Registros anteriores permanecem com vínculo
nulo; novas criações gravam o ID antes do commit. O `Down` automático é recusado
para não apagar a identificação necessária às reentregas.

Os futuros handlers de escrita devem respeitar
esse mecanismo ou adotar proteção equivalente
devidamente justificada.

Integrações externas não devem presumir
garantia absoluta de execução exatamente uma vez.

### 8.1 Diário de recuperação do agente (#44)

PersistirTurnosAgente cria somente telegram_agent_turns, com PK/FK restritiva
UpdateId → inbox, ator, comando congelado em jsonb, resposta textual e estado de
envio (Pending, Sending, Accepted, Failed, Uncertain). Não armazena raciocínio ou
transcrição completa do modelo. A migration foi exercitada apenas em base local
descartável; aplicação a outro ambiente exige autorização específica. Down recusa
remoção se houver turnos, preservando dados de recuperação.

Um advisory lock de sessão por administradora serializa a orquestração sem manter
transação aberta durante Gemini/Telegram. Antes de uma escrita, o comando é
persistido e congelado por update. Após queda antes da resposta, o handler
reexecuta esse comando sob a idempotência existente ou reconhece o vínculo já
confirmado, sem outra interpretação pelo modelo. Criação registra também o update
de execução na intenção pendente; um update seguinte não duplica uma criação já
confirmada cuja finalização da intenção foi interrompida.

A resposta é persistida antes de enviar. Retry de envio recusado por rate limit
retoma somente a resposta, limitado a três tentativas externas à política já
limitada do cliente Telegram. Aceitação é registrada com message_id; nunca prova
leitura. Envio incerto (timeout, transporte, 5xx ou queda após Sending) não é
reenviado automaticamente e não repete negócio. A inbox pode concluir o
processamento com estado de envio Failed/Uncertain para inspeção controlada;
Completed não significa entrega ao usuário.

## 9. Consultas e respostas sob demanda

`ConsultaAgendaApplication` interpreta datas em America/Sao_Paulo e consulta os
períodos efetivos em UTC, portanto serviços iniciados antes do intervalo aparecem
nos dias em que ocupam a agenda. `PostgresConsultaAgendaStore` usa consultas sem
tracking, limites e ordenação determinística. `DisponibilidadeAgendaApplication`
reutiliza o motor da #30 e considera somente agendamentos Agendados.

`FormatadorAgenda` produz texto simples por dia/período e inclui IDs para futura
desambiguação. O envio privado continua responsabilidade de #18/#51 e da Tool de
#44; a #32 não integra Telegram, MAF ou Gemini.

O resumo automático às 07h50 (#50) foi retirado em 11/11/2026. Não haverá
serviço de resumo ou controle de execução desse job no MVP. O BackgroundService
da inbox e a recuperação de respostas permanecem parte da arquitetura.
Prorrogação manual (#40) fica pós-MVP; #35/#45 abrangem somente as operações
de estado incluídas na lista criar, consultar, editar, reagendar, concluir e cancelar.

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
- `Telegram:Sending:RequestTimeoutSeconds` (opcional; padrão 10, faixa 1–30)
- `Telegram:Sending:TotalTimeoutSeconds` (opcional; padrão 30, entre o timeout por tentativa e 60)
- `Telegram:Sending:MaxAttempts` (opcional; padrão 3, faixa 1–3)
- `Telegram:Sending:MaxRetryDelaySeconds` (opcional; padrão 10, faixa 1–30)
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
