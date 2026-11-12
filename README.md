
# Ian

Agente de inteligência artificial para gerenciar uma única agenda de uma loja de móveis planejados, operado por uma administradora no Telegram, somente por texto.

## Objetivo

Permitir que a administradora consulte e organize clientes, serviços, agendamentos e disponibilidade em uma agenda única. O MVP utiliza exclusivamente mensagens textuais privadas; equipes, grupos Telegram, jornadas excepcionais, folgas, bloqueios administrativos, resumo automático, prorrogação manual, reabertura e dashboards estão fora do escopo.

Serviços em dias corridos reservam 08h–18h em todas as datas, inclusive a primeira.
Horário informado diferente de 08h exige esclarecimento, sem reserva parcial
automática. Consultas sob demanda e respostas textuais normais permanecem.

## Tecnologias planejadas

- C# / .NET 10 — ASP.NET Core Web API
- Microsoft Agent Framework (MAF) com Gemini via Google AI Studio
- Telegram Bot API com Webhook HTTPS
- PostgreSQL e Entity Framework Core
- BackgroundService para execução de tarefas em segundo plano
- Execução inicial local no macOS com túnel HTTPS para o Telegram

## Estrutura da solução

- `Ian.Api`: aplicação principal, endpoints HTTP e inicialização dos serviços.
- `Ian.Application`: casos de uso, contratos e orquestração das operações.
- `Ian.Domain`: entidades e regras de negócio.
- `Ian.Infrastructure`: persistência de dados e integrações externas.
- `Ian.Tests`: testes automatizados.

## Executando o projeto localmente

Para banco descartável, homologação automatizada do fluxo textual e checklist
local de API/túnel/webhook, siga [Homologação local do MVP](docs/homologacao-local.md).

### Pré-requisitos

- .NET SDK 10
- Git
- macOS, Linux ou Windows com ambiente .NET configurado

### 1. Restaurar as dependências

Na raiz da solução, execute:

```bash
dotnet restore
```

Restaura os pacotes NuGet necessários para os projetos.

### 2. Compilar a solução

```bash
dotnet build
```

Compila a solução e verifica possíveis erros de compilação e referências entre projetos.

### 3. Executar os testes

```bash
dotnet test
```

Executa os testes automatizados disponíveis na solução.

### 4. Configurar o Telegram e iniciar a API

Em `Development`, use o User Secrets da API. Os valores abaixo são fictícios e permitem testar a inicialização e o endpoint de saúde localmente.

```bash
dotnet user-secrets set "Telegram:BotToken" "token-ficticio" --project Ian.Api
dotnet user-secrets set "Telegram:AdministratorUserId" "42" --project Ian.Api
dotnet user-secrets set "Telegram:WebhookSecret" "segredo_ficticio-123" --project Ian.Api
dotnet run --project Ian.Api
```

O token deve ser preenchido, o ID deve ser um inteiro positivo de 64 bits e o segredo deve ter de 1 a 256 caracteres ASCII entre letras, dígitos, `_` e `-`. `Telegram:PublicBaseUrl` é opcional e, se definida, deve ser uma URL HTTPS absoluta, como `https://ian.example.test`. Uma configuração inválida impede a inicialização e indica a chave a corrigir.

Da menor para a maior prioridade, a configuração da aplicação vem de `appsettings.json`, `appsettings.<Ambiente>.json`, User Secrets (em `Development`), variáveis de ambiente e argumentos de linha de comando. Para configurar por ambiente, use `Telegram__BotToken`, `Telegram__AdministratorUserId`, `Telegram__WebhookSecret` e, se necessário, `Telegram__PublicBaseUrl`. Mantenha credenciais reais fora do Git e use User Secrets no ambiente local ou variáveis de ambiente fornecidas pelo ambiente de execução.

Os endereços HTTP/HTTPS disponíveis aparecem no terminal. O console registra status, duração e `TraceId` W3C das requisições em JSON.

## Organização do desenvolvimento

- `main`: versão estável.
- `dev`: integração das funcionalidades.
- `feature/*`: desenvolvimento de funcionalidades, integradas à `dev` por Pull Requests.

### Fluxo de desenvolvimento

Issue → `feature/*` a partir de `dev` → implementação → testes automatizados pertinentes → build → commit → push → Pull Request para `dev` → checks obrigatórios do GitHub → squash merge → fechamento da issue.

Uma solicitação explícita para executar uma issue completa autoriza commit, push, Pull Request e squash merge, sem aprovações intermediárias, respeitadas as permissões do ambiente. Não faça merge enquanto testes falharem ou checks obrigatórios estiverem pendentes ou reprovados. Investigue somente os arquivos relevantes e evite auditorias ou code reviews separados.

### 5. PostgreSQL, migrations e segurança dos testes

Configure ConnectionStrings:Default em User Secrets de Ian.Api ou na variável
ConnectionStrings__Default, sem publicar credenciais. Antes de aplicar migrations,
identifique o destino, inventarie dados, faça backup completo e ensaie restauração.
Leia o [procedimento da #79](docs/planejamento/migracao-agenda-unica.md).

A origem de desenvolvimento ian_t03_tmp contém registros técnicos e não é
descartável. Não aplicar novas migrations nela sem autorização específica.
A #79 foi validada com PostgreSQL 17.11 e bancos isolados. Seus Down recusam
retorno automático; não utilizar database update 0 como procedimento de rollback.

Para gerar SQL revisável sem aplicar ao banco:

```powershell
dotnet tool restore
dotnet ef migrations script 20261110155523_TelegramInboxProcessing --project Ian.Infrastructure --startup-project Ian.Api --output migration-review.sql
```

Revise o SQL e confirme a conexão de destino antes de qualquer aplicação.
Não iniciar a API de teste apontando para a origem: o consumidor da inbox escreve.

### 6. Modelo de dados implementado — agenda única

| Tabela ativa | Regras |
| --- | --- |
| clientes | Cadastro e busca da #25; telefone brasileiro canônico obrigatório e único; endereço principal opcional; resultados 0/1/N sem seleção ambígua. |
| tipos_servico | Catálogo fixo da #24 com quatro IDs estáveis e duração/unidade/modalidade padrão; consulta somente leitura. |
| agendamentos | Cliente e serviço obrigatórios; sem equipe; criação #31, consultas 0/1/N da #32 e edição/reagendamento #33 e encerramento #35; Versao >=1 como token EF; estado e endereço snapshot preservados. |
| periodos_ocupacao | Relação 1:N; intervalos UTC semiabertos usados para programação e disponibilidade, inclusive serviços prolongados. |
| registros_auditoria | Histórico com FK restritiva para agendamento. |
| solicitacoes_pendentes / envios_telegram | Contexto e histórico técnico preservados. |
| telegram_updates_inbox / telegram_business_operations | Recebimento durável, recuperação e idempotência; criações confirmadas mantêm vínculo com o Agendamento para reentrega. |

As novas migrations PrepararAgendaUnica e ConsolidarAgendaUnica movem equipes,
exceções e bloqueios para legado_mvp, preservando PKs, FKs, dados e sequences.
Um snapshot de preparação e o mapa AgendamentoId→EquipeId mantêm rastreabilidade.
As quatro migrations anteriores permanecem intactas; não há DropTable no upgrade.
A migration incremental da #31 apenas adiciona o vínculo nullable da operação
idempotente ao agendamento, sem reescrever operações técnicas anteriores.

AgendaMutationExecutor integra a transação idempotente com advisory lock global.
A #31 implementa a criação e a #32 consulta programação e disponibilidade sem
efeitos de escrita. A #33 disponibiliza IEdicaoAgendamento: exige ID/versão, relê
Agendado sob lock, revalida conflitos e substitui ocupações atomicamente, com
incremento de versão, idempotência e auditoria mínima. Edição sem endereço
explícito preserva o snapshot. Busca ambígua reutiliza candidatos persistidos da
#21 e consulta da #32; integração com Tools implementada na #44.

A #35 expõe IEncerramentoAgendamento para conclusão por ID/versão e cancelamento
com confirmação obrigatória da #22. Estado, versão, vínculo idempotente e auditoria
mínima são gravados na mesma transação. Períodos planejados são preservados para
consulta histórica; o estado Concluido/Cancelado os torna inativos nas consultas
de disponibilidade e na criação. Conclusão antecipada libera reservas futuras
sem alterar a duração ou o fim previsto. Confirmação repetida/obsoleta é recusada;
reentrega da conclusão não repete a transição. Não há reabertura ou DELETE. A #30 permanece como motor determinístico de períodos no Domain.
### Integração contínua

O fluxo do repositório é Issue → `feature/*` criada a partir de `dev` → Pull Request para `dev` → checks obrigatórios aprovados → squash merge e fechamento da issue. A `main` recebe versões após homologação.

O GitHub Actions executa restore, build e testes em PRs para `dev` e em pushes nessa branch. Faça squash merge somente após todos os checks obrigatórios passarem. O relatório `.trx` fica no artefato `ian-test-results` da execução.

Para reproduzir a verificação localmente, execute da raiz da solução:

```bash
dotnet restore Ian.slnx
dotnet build Ian.slnx --configuration Release --no-restore
dotnet test Ian.slnx --configuration Release --no-build --no-restore --logger "trx;LogFilePrefix=ian-tests"
```

Os testes usam configuração fictícia e isolada. A proteção que torna o check obrigatório é configurada separadamente nas regras da branch `dev`.


## Processamento durável de atualizações Telegram (T08)

As atualizações recebidas pelo webhook são persistidas no PostgreSQL antes da resposta HTTP 2xx. O `TelegramInboxBackgroundService` realiza o consumo posteriormente.

### Estados

- `Received`: atualização recebida e persistida.
- `Processing`: atualização reivindicada com lease e token exclusivos.
- `Completed`: processamento concluído, incluindo atualizações negadas pelo filtro de autorização.
- `FailedRetryable`: falha que poderá ser processada novamente no horário programado.
- `FailedPaused`: processamento pausado por limite de tentativas ou dependência indisponível.

### Concorrência e recuperação

O claim utiliza `FOR UPDATE SKIP LOCKED`, executado atomicamente com a mudança para `Processing`.

Cada claim recebe um novo `LockToken`. As operações de renovação, conclusão e falha verificam esse token e a validade do lease.

O lease padrão é de 2 minutos e o heartbeat ocorre a cada 20 segundos.

Após uma queda, registros `Processing` com lease expirado tornam-se elegíveis para nova reivindicação.

Falhas transitórias utilizam backoff exponencial, com intervalo máximo de 5 minutos e limite de 5 tentativas. Depois disso, o registro é pausado.

Erros persistidos utilizam códigos sanitizados, sem armazenar mensagens de exceção.

### Idempotência de negócio

O `TelegramBusinessOperationExecutor` implementa uma barreira transacional com chave composta:

`(UpdateId, OperationKey)`

O registro da chave e a alteração de negócio devem ocorrer na mesma transação PostgreSQL.

Para criação de agendamentos, a chave estável implementada é `agendamento:create`.
`CriacaoAgendamentoApplication` executa cliente novo, agendamento, períodos e o
vínculo da operação na mesma transação protegida pelo advisory lock. Em uma
reentrega, o vínculo recupera o mesmo ID confirmado sem repetir a escrita.
O handler da #44 invoca esse caso de uso pelas Tools/Application, sem fornecer
acesso direto do modelo ao banco.

Essa abordagem impede repetir a mesma operação de banco para o mesmo update e chave.

A #44 persiste a intenção e seu update de execução para recuperar uma criação
confirmada sem duplicá-la em outro turno. Confirmação de cancelamento é vinculada
a um desafio consumido na transação. Um novo pedido independente continua sendo
uma nova solicitação; não há deduplicação semântica pelo modelo.

### Limites de garantia

O recebimento durável e a idempotência transacional não significam execução `exactly once` de todo o sistema.

Se uma operação de banco for confirmada e o processo cair antes de marcar o update como `Completed`, o update poderá ser reexecutado. Nesse cenário, a chave de idempotência impede repetir a alteração protegida.

Chamadas externas não participam da transação PostgreSQL nem recebem garantia
de exactly once. A #18 limita retries de transporte; a #44 persiste a resposta e
retoma apenas envios com recusa conhecida. Envios incertos não são reenviados.

### Diálogo textual integrado (#44)

O handler ativo conecta Telegram autorizado → MAF/Gemini → sete Tools tipadas →
casos de uso → PostgreSQL → resposta privada. Inclui cadastro/localização de
clientes, serviços, agenda/disponibilidade, criar, editar, reagendar, concluir e
cancelar. Interno/Externo/Remoto e Minutos/DiasCorridos seguem os nomes do Domain.
Padrões vêm do catálogo; endereço externo e horário em Minutos são coletados.
DiasCorridos às 10h exige esclarecimento e não cria reservas parciais.

Pedidos incompletos persistem na #21 e retomam após reinício. Responda
cliente <ID> ou agendamento <ID> para escolher entre candidatos. /descartar
abandona somente a intenção pendente. Cancelar exige o texto confirmar <código>
apresentado pelo backend; um sim isolado não aceita o desafio. O modelo não
recebe uma Tool para confirmar nem para executar SQL. Respostas de sucesso são
produzidas a partir do resultado persistido, nunca do texto livre do modelo.

A migration PersistirTurnosAgente adiciona telegram_agent_turns para congelar o
comando por update, preservar a resposta e registrar aceitação/falha/incerteza de
envio. Foi validada apenas em banco descartável; aplicar em outro ambiente exige
autorização específica. Não foi aplicada à origem de desenvolvimento. Uma queda
após commit recupera o ID confirmado sem reinterpretar o comando. Uma queda após
início do envio é tratada como incerta, sem reenvio automático.

Limites: 6 iterações MAF, 8 Tools, 15s por Tool, 60s por execução, prompt de
16.000 caracteres e resposta privada de até 4.000. Chamadas reais Gemini exigem
autorização para quota/cobrança; testes usam integrações simuladas. A #59 homologa
o MVP com PostgreSQL descartável e Telegram/Gemini simulados; execução com serviços
reais exige autorização explícita. Updates antigos pausados com
HandlerUnavailable exigem reativação controlada; não foram reativados pela #44.
Consultas textuais exibem o estado de cada compromisso, distinguindo histórico
Concluído/Cancelado de reservas Agendadas.

Pedidos como “Quero agendar um serviço” iniciam a coleta mesmo sem outros dados.
Na #96, cadastro/localização de cliente durante essa coleta continuam o objetivo
original, inclusive após reinício. Telefone e horário divergente são esclarecidos
sem perder serviço, endereço ou data; o resumo amigável vem dos dados persistidos.
As Tools mantêm seus metadados internos sem exibi-los como resultado técnico.

A #98 interpreta o objetivo antes da seleção de Tools e usa atos de diálogo
contextuais, com histórico recente persistente. Consultar o catálogo como etapa
intermediária não encerra um pedido de agendar. Diagnóstico por update_id permite
rastrear recebimento, modelo/Tools e envio sem registrar conteúdo privado.
A [validação real controlada](docs/validacao-conversacional-controlada.md) exige
autorização; testes simulados não comprovam a qualidade semântica do Gemini real.

Mensagens sem texto, mídia com legenda, bots e grupos não executam comandos.
O parser não persiste metadados de voz/áudio; contratos exclusivos de grupos
permanecem fora do MVP.

### Observabilidade

O processamento utiliza `update_id` e o identificador de correlação `tg-{update_id}`.

Logs não incluem payloads, conteúdo de mensagens, telefones, endereços, tokens ou strings de conexão.

### Testes

Testes unitários verificam consumo, duplicidade, expiração do lease, retry, autorização e preservação das mensagens pendentes.

Testes PostgreSQL exigem IAN_TEST_POSTGRES_CONNECTION_STRING apontando para host
loopback e banco com nome ian_t<numero>_tests (por exemplo, ian_t79_tests).
IAN_TEST_POSTGRES_DATABASE deve conter exatamente esse nome, confirmando o destino.
A conexão vem de fonte privada; nunca publicar seu valor. A proteção também se
aplica aos testes antigos, que apagam a inbox. A suíte de migração recria schemas
somente nesse destino validado. Nunca usar ian_t03_tmp ou a cópia de restauração.

Sem conexão, testes PostgreSQL são ignorados; com destino inseguro, falham antes
de conectar. Executar restore, build Release e test Release. Deixar
IAN_RUN_GEMINI_SMOKE e IAN_ALLOW_GEMINI_QUOTA desativados para evitar chamadas reais.
Evidências da #79 em docs/planejamento/validacao-agenda-unica.md.

## Integração MAF/Gemini (T11)

`Ian.Application.AI.IAiModel` disponibiliza invocação textual com resultado
tipado. `GeminiAiModel` usa MAF `ChatClientAgent.RunAsync`, `IChatClient` e
o SDK oficial Google.GenAI. O chamador deverá autorizar o prompt antes da
invocação. Esse adaptador textual permanece isolado, enquanto a #44 usa
GeminiAgendaAgentRunner para o handler, Tools e contexto mínimo persistido.

Configuração necessária para invocar o serviço:

| Chave | Configuração |
| --- | --- |
| `Gemini:ApiKey` | Obrigatória; somente User Secrets ou fonte segura equivalente |
| `Gemini:Model` | ID de modelo; o appsettings sugere `gemini-3.1-flash-lite` |
| `Gemini:TimeoutSeconds` | De 1 a 300 segundos; padrão 60 |

Use `Gemini__ApiKey`, `Gemini__Model` e `Gemini__TimeoutSeconds` para
variáveis de ambiente. A validação ocorre na invocação e produz
`InvalidConfiguration` sem expor valores, preservando o startup da API
mesmo sem chave Gemini configurada. O handler ativo responde com falha sanitizada
nessa situação, sem executar negócio.

Para cadastrar a chave em Development pelo PowerShell, sem exibi-la nem
incluí-la em argumentos/histórico, use entrada oculta e JSON por stdin:

```powershell
$geminiSecret = Read-Host 'Chave Gemini' -AsSecureString
try {
    @{ 'Gemini:ApiKey' = [System.Net.NetworkCredential]::new('', $geminiSecret).Password } |
        ConvertTo-Json -Compress |
        dotnet user-secrets set --project Ian.Api
} finally {
    Remove-Variable geminiSecret
}
dotnet user-secrets set 'Gemini:Model' 'gemini-3.1-flash-lite' --project Ian.Api
dotnet user-secrets set 'Gemini:TimeoutSeconds' '60' --project Ian.Api
```

Os testes comuns usam fakes e HTTP local, sem tokens Google. Os dois testes
manuais estão ignorados por padrão. Somente após configurar a credencial
e autorizar o consumo de quota, execute:

```powershell
$env:IAN_RUN_GEMINI_SMOKE = '1'
$env:IAN_ALLOW_GEMINI_QUOTA = '1'
try {
    dotnet test Ian.slnx --configuration Release --filter 'Category=GeminiSmoke' --logger 'console;verbosity=normal'
} finally {
    Remove-Item Env:IAN_RUN_GEMINI_SMOKE, Env:IAN_ALLOW_GEMINI_QUOTA
}
```

O smoke lê User Secrets de `Ian.Api` e variáveis de ambiente, resolve a
infraestrutura de produção, usa apenas um prompt sintético e uma função
de soma isolada em testes. Não imprime chave nem resposta completa.
O teste de Function Calling pode consumir mais de uma chamada ao modelo.
Em 10/11/2026, os dois smokes reais autorizados passaram com
`gemini-3.1-flash-lite`: retorno textual pelo `IAiModel` e ciclo de
`sum_demo` com os argumentos esperados. As latências foram 1710 ms e
21475 ms, e o uso informado totalizou 219 tokens. As variáveis temporárias
foram removidas. Isso valida acesso e Function Calling básico nessa conta;
não valida qualidade de negócio ou limites de quota/faturamento.
A #19 foi integrada em dev. Os testes PostgreSQL foram posteriormente executados em banco descartável na #79; os smokes Gemini não foram repetidos nessa entrega.

Detalhes técnicos, limites e evidências em
[docs/spikes/maf-gemini.md](docs/spikes/maf-gemini.md).
