# T11 — Integração MAF/Gemini

Investigação, validação local e smokes reais em 10/11/2026. Base: `dev` em `7a3d969`,
com a preparação de voz da #17 integrada. Escopo: [#19](https://github.com/Elion-Tecnologia/ian/issues/19).

## Pré-requisitos e divergência

A descrição da #19 usa rótulos T01/#9 e T02/#10, mas os hyperlinks apontam
para #18 e #28. Foram consultadas integralmente as issues reais
[#9](https://github.com/Elion-Tecnologia/ian/issues/9) e
[#10](https://github.com/Elion-Tecnologia/ian/issues/10), ambas concluídas.
A divergência é registrada aqui; nenhuma outra issue foi editada.

## Fontes e verificação de versões

Foram consultados a [documentação MAF](https://learn.microsoft.com/en-us/agent-framework/),
a [integração Gemini](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/google-gemini),
o [repositório MAF](https://github.com/microsoft/agent-framework), o
[SDK oficial .NET Google](https://github.com/googleapis/dotnet-genai) e a
[documentação Gemini](https://ai.google.dev/gemini-api/docs).

Os exemplos de instalação da documentação não foram usados como fonte
de versão. Os índices reais do NuGet foram consultados antes da adição
de dependências definitivas:

| Pacote | Última estável consultada | Decisão |
| --- | --- | --- |
| [Microsoft.Agents.AI](https://api.nuget.org/v3-flatcontainer/microsoft.agents.ai/index.json) | 1.28.0 | Referência direta em Infrastructure |
| [Google.GenAI](https://api.nuget.org/v3-flatcontainer/google.genai/index.json) | 2.0.0 | Referência direta; único provedor Gemini |
| [Microsoft.Extensions.AI](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.ai/index.json) | 10.11.0 | Resolvido transitivamente pelo MAF; sem referência redundante |
| [Mscc.GenerativeAI.Microsoft](https://api.nuget.org/v3-flatcontainer/mscc.generativeai.microsoft/index.json) | 3.1.0 | Avaliado documentalmente, não instalado |

A página resumida de Google.GenAI ainda mostrava 1.26.0; o índice real
informou 2.0.0. A versão 2.0.0 foi efetivamente restaurada e compilada.
Seu nuspec aponta o commit de origem
`f72b7e6a218363abc383a58dc7b97e60235ca04b` no repositório oficial e fornece
assets net8.0/netstandard2.0. O MAF restaurado possui asset net10.0.
A combinação com Microsoft.Extensions.AI 10.11.0 compilou em .NET 10.

O SDK oficial já oferece `Client.AsIChatClient(model)`. Como a ponte
funcionou com MAF nos testes locais, não houve vantagem demonstrada que
justificasse adicionar o provedor comunitário Mscc ou manter dois SDKs.
Não foi alegada compatibilidade compilada de Mscc: ele não foi instalado.

## Spike mínimo antes dos pacotes definitivos

Foi criado um console net10.0 isolado em `artifacts/maf-gemini-spike/`,
fora da solução e ignorado pelo Git. Ele referenciou apenas os candidatos
MAF 1.28.0 e Google.GenAI 2.0.0, compilou a ponte `IChatClient` e executou
MAF contra um `HttpMessageHandler` sintético, sem rede Google.

```powershell
dotnet restore artifacts/maf-gemini-spike/Spike.csproj
dotnet run --project artifacts/maf-gemini-spike/Spike.csproj --configuration Release --no-restore
```

Resultado observado: duas chamadas, argumentos `left=2/right=3`
interpretados, execução local de `sum_demo`, retorno de `functionResponse`
ao fluxo e texto final. Somente após esse resultado os dois pacotes
foram adicionados à solução. O cenário reproduzível e versionado está
nos testes `GeminiSdkTests` e `GeminiFunctionDemo`; os artefatos do console
não fazem parte do commit.

## Implementação e responsabilidades

Application define `IAiModel`, `AiModelResult`, `AiModelFailure` e contagem
de tokens opcional, sem referências a SDKs. O chamador deve autorizar a
solicitação antes de invocar o modelo. Sucesso é texto, sem autorização
para escrever dados. Prompt vazio, NUL ou acima de 16000 caracteres é
rejeitado antes de qualquer chamada externa.

Infrastructure cria o SDK oficial para Google AI Studio (Developer API,
sem Vertex AI), obtém `IChatClient` e executa `ChatClientAgent.RunAsync`.
O cliente/transporte é compartilhado por contêiner DI e descartado com
ele; cada invocação cria um agente/sessão novo, sem histórico entre chamadas.

O caminho de produção solicita somente texto, limita saída a 2048 tokens
e não instala ferramentas nem o loop automático de invocação de funções.
Respostas truncadas, vazias, bloqueadas, com papel incompatível ou com
conteúdo não textual inesperado são inválidas. Raciocínio separado não é
usado como resposta textual nem exposto no contrato.

O transporte não utiliza handlers de logging, redirecionamentos ou
telemetria de conteúdo. Não se registram prompts, chaves, corpos de erro
ou respostas completas. `ToString()` do resultado é sanitizado. Retries
do SDK usam `Attempts=1`, comprovado nos testes HTTP de erro; não há
fallback de modelo, resposta inventada ou repetição automática escondida.

## Configuração e erros

`Gemini:ApiKey` é fornecida por User Secrets em Development ou mecanismo
seguro equivalente. `Gemini:Model` usa ID simples; URLs/caminhos arbitrários
não são aceitos. `Gemini:TimeoutSeconds` aceita 1 a 300 (padrão 60).
O timeout é aplicado ao SDK em milissegundos e ao ciclo completo por um
token vinculado ao cancelamento do chamador. A configuração é validada
na invocação: erros identificam as chaves, sem divulgar os valores.
Não exigir a chave no startup preserva API/Telegram enquanto IA está inativa.

| Condição | Resultado |
| --- | --- |
| Prompt inválido | `InvalidPrompt`, sem rede |
| Configuração ausente/inválida | `InvalidConfiguration`, sem rede |
| HTTP 401/403 | `AuthenticationFailed` |
| HTTP 429 | `QuotaExceeded` |
| Timeout local/SDK ou HTTP 408/504 | `Timeout` |
| Cancelamento do chamador/host/lease | `OperationCanceledException` com o token do chamador |
| HTTP 404 | `ModelUnavailable` |
| HTTP 5xx, erro de rede ou exceção SDK sem status HTTP | `ProviderUnavailable` (504 é timeout) |
| Outros HTTP 4xx | `ProviderRejected`; nenhum corpo do fornecedor é retornado |
| JSON malformado/resposta vazia ou incompleta | `InvalidResponse` |
| Falha inesperada | `UnexpectedFailure`, sem propagar a exceção original |

HTTP 400 não é interpretado como prova de chave inválida: pode representar
payload ou pré-condição recusados. A classificação usa os códigos
estruturados do SDK/HTTP, sem heurística baseada em mensagens privadas.
Referências: [erros do SDK](https://github.com/googleapis/dotnet-genai/blob/main/Google.GenAI/ApiException.cs)
e [tratamento de falhas Gemini](https://ai.google.dev/gemini-api/docs/troubleshooting).

## Modelo, quota e limites

O modelo escolhido explicitamente pelo usuário para o IA.N é
`gemini-3.1-flash-lite`. O appsettings, os exemplos e os User Secrets foram
atualizados para esse ID, preservando a credencial existente. O Google o
publica como estável, com entrada textual e Function Calling, voltado a
baixa latência e custo. Continua configurável e não ativa áudio.

O suporte é declarado pelo [fornecedor](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite),
mas não comprova acesso pelo projeto Google, comportamento real, região,
precisão ou cotas. Os limites ativos de RPM/TPM/RPD variam por projeto/modelo
e devem ser verificados no AI Studio. Ver [quotas](https://ai.google.dev/gemini-api/docs/rate-limits),
[preços](https://ai.google.dev/gemini-api/docs/pricing#gemini-3.1-flash-lite) e
[Function Calling](https://ai.google.dev/gemini-api/docs/function-calling).
Os smokes autorizados consumiram tokens reais, registrados abaixo.
O plano de faturamento, o saldo de quota e o valor efetivamente cobrado
não foram consultados; não se presume execução gratuita.

## Evidências de Function Calling

`GeminiSdkTests.FunctionCallingRoundTripUsesRealMafSdkAndLocalHttpOnly`
executa MAF e SDK reais contra Kestrel local. A reescrita de URL e a
chave sintética existem somente nos testes. O teste verifica o schema da
função enviado, os argumentos interpretados, uma execução de soma sem
efeitos colaterais, `functionResponse` na segunda requisição e texto final.

Esse teste local comprova o encadeamento técnico, incluindo serialização
e HTTP, com respostas sintéticas. A solicitação da função pelo modelo real
foi validada separadamente no smoke abaixo. A função vive somente em
Ian.Tests; não há ferramentas de
agenda, clientes ou equipes em produção. Chamadas de função não solicitadas
são rejeitadas pelo adaptador em um teste específico, sem loop de execução.

## Smokes reais autorizados e aprovados

Após a preparação inicial sem consumo, o usuário autorizou explicitamente
os dois smokes, inclusive quota e eventual cobrança, com
`gemini-3.1-flash-lite`. A execução começou em 10/11/2026 às
20:07:23 UTC (17:07:23 em America/Sao_Paulo), com timeout de 60 segundos.
A presença da propriedade da chave foi verificada sem acessar ou exibir
seu valor; somente o runtime carregou a credencial para autenticação.
Nenhum dado real de cliente, áudio, mensagem Telegram ou banco foi usado.

| Teste real | Resultado | Latência da invocação | Tokens de entrada | Tokens de saída | Total informado |
| --- | --- | --- | --- | --- | --- |
| `RealGeminiReturnsTextThroughProductionContract` | Aprovado; `IAiModel` retornou sucesso e texto não vazio | 1710 ms | 10 | 2 | 12 |
| `RealGeminiCallsOnlyTheIsolatedDemoFunction` | Aprovado; uma chamada de `sum_demo`, argumentos 2 e 3 corretos, resultado integrado ao texto final | 21475 ms | 188 | 19 | 207 |

Os 219 tokens são a soma do uso informado pelo SDK/MAF nos dois resultados,
sem estimativa de cobrança nem comprovação independente do faturamento.
Não foram registrados chave, respostas completas ou corpos privados.
Nenhuma falha de autenticação, quota, modelo ou timeout ocorreu; não houve
nova tentativa. A conclusão dentro de 60 segundos valida o caminho de
sucesso com esse limite, sem simular um timeout real no Google.

`GeminiSmokeTests.RealGeminiReturnsTextThroughProductionContract` usa
User Secrets de Ian.Api/variáveis de ambiente, o registro DI de produção
e o prompt sintético solicitado. Não exige uma redação exata da resposta;
registra somente modelo, timeout, latência, resultado e uso agregado
se disponível.
`RealGeminiCallsOnlyTheIsolatedDemoFunction` usa a mesma fábrica de cliente
e uma ferramenta de demonstração restrita aos testes. O segundo smoke
verificou que o modelo solicitou a função, seus argumentos foram
interpretados, a soma local retornou ao fluxo e o texto final continha
o resultado. O adaptador textual de produção continua sem ferramentas.

Os testes foram executados em sequência, uma vez cada, com filtro
`Category=GeminiSmoke` combinado ao nome de cada teste, para interromper
antes do segundo em caso de falha no primeiro:

```powershell
$env:IAN_RUN_GEMINI_SMOKE = '1'
$env:IAN_ALLOW_GEMINI_QUOTA = '1'
try {
    dotnet test Ian.slnx --configuration Release --no-build --no-restore --filter 'Category=GeminiSmoke&FullyQualifiedName~RealGeminiReturnsTextThroughProductionContract' --logger 'console;verbosity=detailed'
    if ($LASTEXITCODE -ne 0) { throw 'Interromper para diagnóstico.' }
    dotnet test Ian.slnx --configuration Release --no-build --no-restore --filter 'Category=GeminiSmoke&FullyQualifiedName~RealGeminiCallsOnlyTheIsolatedDemoFunction' --logger 'console;verbosity=detailed'
    if ($LASTEXITCODE -ne 0) { throw 'Interromper para diagnóstico.' }
} finally {
    Remove-Item Env:IAN_RUN_GEMINI_SMOKE, Env:IAN_ALLOW_GEMINI_QUOTA -ErrorAction SilentlyContinue
}
```

Ambos os processos encerraram com código zero. As variáveis temporárias
foram removidas em `finally`. Os testes reais continuam ignorados por
padrão; qualquer execução futura exige nova autorização de consumo.
As chamadas previstas são uma geração textual e duas gerações no ciclo
de função; a contagem HTTP efetiva não foi instrumentada no smoke.

O [README](../../README.md#integração-mafgemini-t11) contém entrada oculta da
chave via JSON/stdin para User Secrets e os comandos de execução manual.
O smoke deve ocorrer apenas após autorização de quota, nunca como parte
dos testes comuns ou como substituto de validação real por fake.

A chave funcional, o acesso ao modelo, o retorno pelo contrato e o
Function Calling básico foram validados nesta conta. Não foram provocadas
falhas reais de autenticação, 429 ou timeout para consumir novas chamadas;
sua classificação permanece coberta por testes locais. Limites efetivos
da conta, qualidade em cenários de negócio e transcrição de áudio não
foram validados. A PR #76 permanece Draft com `Refs #19`, para revisão do
usuário e enquanto os sete testes PostgreSQL seguem sem execução.

## MVP textual — revisão de escopo

A #19 foi integrada em dev. O MVP aprovado em 10/11/2026 usa agenda única
e texto privado da administradora. A #17 foi encerrada como Not planned,
a PR #77 fechada sem merge e a #78 remove a preparação antiga de mídia.
A integração textual IAiModel/GeminiAiModel/GeminiChatClientFactory é
preservada integralmente. As evidências desta página são históricas;
não autorizam novos envios ao provedor.

## Validação e revisão — registro histórico da #19

Comandos finais executados na raiz:

```powershell
dotnet restore Ian.slnx
dotnet build Ian.slnx --configuration Release --no-restore
dotnet test Ian.slnx --configuration Release --no-build --no-restore
```

Resultado observado: restore concluído; build com zero erros e zero avisos;
**167 testes aprovados, 9 ignorados, 0 falhas; total 176**. Os testes
existentes de Telegram e voz foram preservados. As verificações de referências
confirmaram Domain/Application independentes de MAF/Gemini/Infrastructure/Api.

Sete testes de `TelegramInboxPostgresTests` foram ignorados por ausência de
`IAN_TEST_POSTGRES_CONNECTION_STRING`; exigem banco descartável e não foram
validados:

- `DuplicateWebhookStorageKeepsSingleRow`
- `FailedBusinessTransactionDoesNotConsumeIdempotencyKey`
- `ConcurrentBusinessOperationCommitsOnlyOnce`
- `DatabaseUnavailableDoesNotLosePendingUpdate`
- `ConcurrentConsumersClaimUpdateOnlyOnce`
- `ExpiredLeaseIsRecoveredByAnotherConsumer`
- `RetryRespectsScheduledTime`

Os outros dois ignorados na suíte comum são os smokes acima: seus opt-ins
foram removidos. Eles foram aprovados na execução real separada, uma vez
cada, e não são contabilizados como aprovados pela execução comum.

A revisão local não encontrou segredos reais, áudios, dados de clientes
ou artefatos temporários nos arquivos da alteração. Não há mudanças em
Domain, contratos de webhook, handlers Telegram, migrations ou regras de
negócio. A Draft PR #76 reúne as evidências reais para revisão; os testes
PostgreSQL continuam pendentes e a issue #19 não foi fechada.
