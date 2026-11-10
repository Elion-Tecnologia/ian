
# Ian

Agente de inteligência artificial para gerenciar agendas e serviços de equipes de uma loja de móveis planejados por meio do Telegram.

## Objetivo

Permitir que um administrador consulte e organize agendamentos de clientes, equipes, horários, indisponibilidades e programações, com suporte a mensagens de texto e áudio.

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

1. Selecionar uma Issue disponível.
2. Criar uma branch `feature/*` a partir de `dev`.
3. Implementar a funcionalidade seguindo o escopo e os critérios de aceite.
4. Executar o build e os testes relevantes.
5. Abrir um Pull Request para `dev`.
6. Revisar e integrar as alterações após aprovação.

### 5. Configurar PostgreSQL e executar migrations

Use um servidor PostgreSQL local. No macOS, se ainda precisar instalá-lo, rode:

```bash
brew install postgresql@17
brew services start postgresql@17
export PATH="$(brew --prefix postgresql@17)/bin:$PATH"
```

Crie um banco descartável chamado `ian_t03_tmp` para validar as migrations. Use o nome do seu usuário local do PostgreSQL no lugar de `usuario_local`:

```bash
createdb -h 127.0.0.1 -p 5432 -U usuario_local ian_t03_tmp
```

No Windows, se `createdb` estiver fora do `PATH`, execute `createdb.exe` pelo caminho completo da pasta `bin` da sua instalação.

Em `Development`, cadastre `ConnectionStrings:Default` nos User Secrets do projeto `Ian.Api`. A string abaixo mostra o formato com valores fictícios:

```text
Host=127.0.0.1;Port=5432;Database=ian_t03_tmp;Username=usuario_local;Password=senha_ficticia
```

A variável de ambiente `ConnectionStrings__Default` também pode fornecer a conexão. Guarde as credenciais reais fora do repositório.

Da raiz da solução, restaure a ferramenta local e aplique as migrations:

```bash
dotnet tool restore
dotnet ef database update --project Ian.Infrastructure --startup-project Ian.Api --context IanDbContext -- --environment Development
```

Para testar o rollback até antes da primeira migration, confirme que a conexão aponta para o banco descartável e rode:

```bash
dotnet ef database update 0 --project Ian.Infrastructure --startup-project Ian.Api --context IanDbContext -- --environment Development
```

Depois, execute novamente o comando de aplicação. Para conferir o ciclo, consulte o histórico após cada etapa:

```sql
SELECT "MigrationId", "ProductVersion"
FROM public."__EFMigrationsHistory";
```

Com apenas a migration `InitialDatabase`, o histórico deve mostrar uma linha após aplicar, zero após reverter e uma após reaplicar.

Em caso de falha de conexão, confira se o serviço do PostgreSQL está rodando e verifique host, porta, banco e usuário. Ao compartilhar o diagnóstico, mantenha a string de conexão fora da mensagem.

### 6. Modelo de dados inicial

| Tabela | Chaves, cardinalidade e regras |
| --- | --- |
| `clientes` | Telefone normalizado obrigatório, preenchido e único. Endereço principal opcional. |
| `equipes` | Nome e situação ativa/inativa. Vínculo com grupo Telegram opcional. |
| `tipos_servico` | Duração padrão positiva, com unidade em minutos ou dias corridos, e modalidade padrão interna, externa ou remota. |
| `agendamentos` | FKs obrigatórias para cliente, equipe e tipo de serviço. Período previsto em UTC e estados Agendado, Concluido ou Cancelado. Endereço preservado como snapshot, obrigatório para serviço externo. Índice por equipe e estado. |
| `periodos_ocupacao` | Um agendamento pode ter vários períodos. Cada período tem início e fim em UTC, com índice por agendamento e início. |
| `excecoes_expediente` | Uma exceção por equipe e data local. Representa uma folga integral ou uma janela de jornada no horário local. |
| `bloqueios_agenda` | Intervalo em UTC. A FK de equipe é opcional e sua ausência representa um bloqueio geral. Índice por equipe e início. |
| `solicitacoes_pendentes` | Operação, usuário do Telegram, estado, criação e expiração em UTC. Índice por usuário, estado e expiração. |
| `registros_auditoria` | Operação, resultado, correlação e instante em UTC. Vínculo opcional com agendamento. Índices por correlação e por agendamento e instante. |
| `envios_telegram` | Destino, tipo de mensagem, estado, tentativas e instantes em UTC. Índice por estado e criação. |

As dez tabelas usam chaves primárias `bigint`. As chaves estrangeiras usam `ON DELETE RESTRICT` para preservar registros vinculados. A migration `DomainModel` é incremental sobre `InitialDatabase`.

### Integração contínua

O fluxo do repositório é `feature/*` → `dev` → `main`. As tarefas entram em `dev` por Pull Request. A `main` recebe versões após homologação.

O GitHub Actions executa restore, build e testes em PRs para `dev` e em pushes nessa branch. Antes de integrar uma PR, confira se o check **Build and test** passou. O relatório `.trx` fica no artefato `ian-test-results` da execução.

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

Para criação de agendamentos, a chave estável prevista é `agendamento:create`. Os futuros handlers administrativos devem executar suas escritas por meio desse executor.

Essa abordagem impede repetir a mesma operação de banco para o mesmo update e chave.

Ela não impede automaticamente duplicidades causadas por updates diferentes que representem a mesma intenção. Esses casos exigirão uma chave de solicitação ou confirmação de negócio persistente.

### Limites de garantia

O recebimento durável e a idempotência transacional não significam execução `exactly once` de todo o sistema.

Se uma operação de banco for confirmada e o processo cair antes de marcar o update como `Completed`, o update poderá ser reexecutado. Nesse cenário, a chave de idempotência impede repetir a alteração protegida.

Chamadas externas, como envios pela Telegram Bot API, não participam da transação PostgreSQL e não recebem garantia de `exactly once`. A política de envio e de repetição será implementada nas tarefas específicas de outbound.

### Handlers ainda não implementados

Enquanto não houver processamento real de comandos, atualizações administrativas e candidatos a vínculo de grupo são preservados em `FailedPaused` com `HandlerUnavailable`.

Nenhum comando é executado por um handler provisório.

Após a implementação do handler correspondente, os registros devem ser reativados de maneira controlada.

### Observabilidade

O processamento utiliza `update_id` e o identificador de correlação `tg-{update_id}`.

Logs não incluem payloads, conteúdo de mensagens, telefones, endereços, tokens ou strings de conexão.

### Testes

Testes unitários verificam consumo, duplicidade, expiração do lease, retry, autorização e preservação das mensagens pendentes.

Testes de integração PostgreSQL usam exclusivamente um banco descartável e a variável de ambiente `IAN_TEST_POSTGRES_CONNECTION_STRING`.

Sem essa variável, os testes de integração são ignorados, não considerados validados.

## Entrada de voz (T09 — transcritor disponível, fluxo ainda desativado)

O suporte de voz usa `GeminiVoiceTranscriber`, MAF e o SDK oficial da #19,
com `gemini-3.1-flash-lite`, mas ainda não está ativado no dispatcher da API.
A integração final aguarda sucesso no smoke real de áudio e o handler textual
autorizado, que não foi implementado na #19. `DeferredTelegramHandlers`
continua preservando os comandos em
`FailedPaused`; a existência dos componentes abaixo não significa que o bot
já transcreve áudios em produção.

`TelegramVoiceFileClient` implementa `getFile`, download limitado em memória e
respostas textuais de erro/esclarecimento. `TelegramVoiceUpdateHandler` pode
envolver o handler textual autorizado, recebendo um `IVoiceTranscriber` real.
O decorator reaplica o mesmo filtro de autorização, preserva a identidade do
update e só encaminha a transcrição aceita ao handler textual. O payload
persistido da inbox não é substituído pela transcrição.

As configurações não secretas estão em `TelegramVoice`:

| Chave | Padrão | Intervalo aceito |
| --- | --- | --- |
| `MaxBytes` | 5242880 (5 MiB) | 1 a 20000000 bytes |
| `MaxDurationSeconds` | 120 | 1 a 3600 segundos |
| `TimeoutSeconds` | 60 | 1 a 300 segundos |

Também podem ser fornecidas por `TelegramVoice__MaxBytes`,
`TelegramVoice__MaxDurationSeconds` e `TelegramVoice__TimeoutSeconds`.
Valores inválidos impedem a inicialização. O timeout do decorator cobre
download e transcrição; o cliente HTTP também limita cada operação de forma
independente quando utilizado diretamente.

Tamanho e duração declarados são validados antes do download. O cliente
verifica o tamanho retornado por `getFile`, `Content-Length` e os bytes
efetivamente lidos, inclusive em respostas sem tamanho declarado. MIME e
duração declarados não comprovam o conteúdo: o adaptador valida o contêiner
OGG e decodifica Opus com Concentus 2.2.2, medindo duração efetiva e silêncio.
Aceita um stream completo mono/estéreo, sem encadeamento; o limite inline
efetivo é o menor entre `MaxBytes` e 12 MiB. O timeout da transcrição usa o
menor entre `TelegramVoice.TimeoutSeconds` e `Gemini.TimeoutSeconds`.

Não há arquivos temporários em disco. Streams e respostas HTTP são descartados,
e os buffers de áudio são zerados ao terminar a transcrição, inclusive em
exceções e cancelamento. Transcrição vazia ou `NeedsClarification` produz uma
resposta de esclarecimento, sem encaminhamento ao handler textual.

Falhas conhecidas geram fallback textual e exigem nova tentativa da
administradora; o componente não adiciona retries HTTP automáticos. Falha de
aceitação do fallback pelo Telegram lança uma exceção sanitizada, permitindo
a política já existente da inbox. Isso pode repetir uma resposta externa em
caso de resultado incerto; não há garantia `exactly once` para `sendMessage`.
Cancelamento do host/perda do lease é propagado, sem enviar fallback.

A aprovação da transcrição não autoriza mutações: ambiguidade semântica,
confirmações e regras de negócio continuam a cargo do pipeline textual/backend.
A política desses casos ainda depende das funcionalidades futuras do agente.
O adaptador rejeita sinais de incerteza e respostas inválidas; não usa um
score de confiança do LLM nem garante precisão por um status positivo.
O decorator concreto pode ser resolvido por DI para testes, sem substituir
o handler provisório no dispatcher.

Os cinco smokes de áudio usam a categoria `GeminiVoiceSmoke` e exigem
autorização específica da #17, `IAN_RUN_VOICE_SMOKE=1`,
`IAN_ALLOW_GEMINI_QUOTA=1` e fixtures externas exclusivamente sintéticas.
Na rodada autorizada de 10/11/2026, inválido e silêncio foram rejeitados
localmente sem chamadas; `short` falhou com `Timeout` após um envio e
60013 ms. Os dois cenários restantes não foram executados. As flags foram
removidas, sem retry ou alteração do modelo/timeout. A fala degradada foi
considerada compreensível pelo usuário e deve preservar a frase ou solicitar
esclarecimento; não é prova de incompreensibilidade.
O preparador offline, limites de envio e evidências estão em
[docs/spikes/voz.md](docs/spikes/voz.md#smoke-de-áudio--preparação-e-execução-parcial).

Para reproduzir os testes novos:

```powershell
dotnet test Ian.slnx --configuration Release --filter "FullyQualifiedName~TelegramVoice"
```

Consulte [a avaliação e as pendências do provedor](docs/spikes/voz.md) antes
de ativar o fluxo.

## Integração MAF/Gemini (T11)

`Ian.Application.AI.IAiModel` disponibiliza invocação textual com resultado
tipado. `GeminiAiModel` usa MAF `ChatClientAgent.RunAsync`, `IChatClient` e
o SDK oficial Google.GenAI. O chamador deverá autorizar o prompt antes da
invocação. A integração não ativa os handlers Telegram, não transcreve
áudio, não mantém histórico e não instala ferramentas de negócio.

Configuração necessária para invocar o serviço:

| Chave | Configuração |
| --- | --- |
| `Gemini:ApiKey` | Obrigatória; somente User Secrets ou fonte segura equivalente |
| `Gemini:Model` | ID de modelo; o appsettings sugere `gemini-3.1-flash-lite` |
| `Gemini:TimeoutSeconds` | De 1 a 300 segundos; padrão 60 |

Use `Gemini__ApiKey`, `Gemini__Model` e `Gemini__TimeoutSeconds` para
variáveis de ambiente. A validação ocorre na invocação e produz
`InvalidConfiguration` sem expor valores, preservando o startup da API
enquanto a integração ainda não está ativada.

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
não valida áudio, qualidade de negócio ou limites de quota/faturamento.
Os sete testes PostgreSQL continuam sem execução e a PR #76 permanece Draft.

Detalhes técnicos, limites e evidências em
[docs/spikes/maf-gemini.md](docs/spikes/maf-gemini.md).
