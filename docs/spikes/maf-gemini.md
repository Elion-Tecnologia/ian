# T11 — Integração MAF/Gemini

Investigação e validação local em 10/11/2026. Base: `dev` em `7a3d969`,
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

O appsettings sugere `gemini-3.8-flash`, ID estável publicado pelo Google
com Function Calling e entrada textual. A escolha inicial acompanha o
modelo atual da família Flash e a avaliação documental da #17; continua
configurável e não ativa áudio. Não foram usados nomes de modelos retirados
de exemplos antigos como garantia de disponibilidade na conta.

O suporte é declarado pelo [fornecedor](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash),
mas não comprova acesso pelo projeto Google, comportamento real, região,
precisão ou cotas. Os limites ativos de RPM/TPM/RPD variam por projeto/modelo
e devem ser verificados no AI Studio. Ver [quotas](https://ai.google.dev/gemini-api/docs/rate-limits),
[preços](https://ai.google.dev/gemini-api/docs/pricing#gemini-3.8-flash) e
[Function Calling](https://ai.google.dev/gemini-api/docs/function-calling).
Não houve consumo medido nem cobrança provocada por esta tarefa.

## Evidências de Function Calling

`GeminiSdkTests.FunctionCallingRoundTripUsesRealMafSdkAndLocalHttpOnly`
executa MAF e SDK reais contra Kestrel local. A reescrita de URL e a
chave sintética existem somente nos testes. O teste verifica o schema da
função enviado, os argumentos interpretados, uma execução de soma sem
efeitos colaterais, `functionResponse` na segunda requisição e texto final.

Isso comprova o encadeamento técnico local, incluindo serialização e
HTTP, com respostas sintéticas. Não comprova que o modelo real solicitará
a função. A função vive somente em Ian.Tests; não há ferramentas de
agenda, clientes ou equipes em produção. Chamadas de função não solicitadas
são rejeitadas pelo adaptador em um teste específico, sem loop de execução.

## Smoke real preparado, não executado

O usuário escolheu explicitamente preparar o smoke sem executá-lo nesta
tarefa. Não houve leitura, descoberta ou exibição de segredos locais para
essa finalidade. Os testes reais ficam ignorados por padrão e exigem
os dois opt-ins `IAN_RUN_GEMINI_SMOKE=1` e `IAN_ALLOW_GEMINI_QUOTA=1`.

`GeminiSmokeTests.RealGeminiReturnsTextThroughProductionContract` usa
User Secrets de Ian.Api/variáveis de ambiente, o registro DI de produção
e o prompt sintético solicitado. Não exige uma redação exata da resposta;
registra somente modelo, timeout, resultado e uso agregado se disponível.
`RealGeminiCallsOnlyTheIsolatedDemoFunction` usa a mesma fábrica de cliente
e uma ferramenta de demonstração restrita aos testes.

O [README](../../README.md#integração-mafgemini-t11) contém entrada oculta da
chave via JSON/stdin para User Secrets e os comandos de execução manual.
O smoke deve ocorrer apenas após autorização de quota, nunca como parte
dos testes comuns ou como substituto de validação real por fake.

Permanecem sem validação: chave funcional, disponibilidade do modelo na
conta, latência/timeout no serviço real, consumo real e Function Calling
com respostas geradas pelo Google. Portanto a publicação deve usar
`Refs #19` e Draft PR; a issue não está concluída.

## Relação com a #17

Não foram alterados o webhook, autorização, handlers provisórios ou o
fluxo de voz. O futuro handler textual autorizado poderá consumir `IAiModel`
depois do gate backend. A futura transcrição deverá usar o contrato
próprio `IVoiceTranscriber` e validar áudio/qualidade antes de encaminhar
texto ao mesmo fluxo autorizado. Esta interface textual não aceita
áudio, não implementa o transcritor e não garante validações de negócio.
Ver [spike de voz](voz.md).

## Validação e revisão

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

Os outros dois ignorados são `GeminiSmokeTests.RealGeminiReturnsTextThroughProductionContract`
e `GeminiSmokeTests.RealGeminiCallsOnlyTheIsolatedDemoFunction`: o usuário
optou por não consumir quota agora. Eles não são considerados aprovados.

A revisão local não encontrou segredos reais, áudios, dados de clientes
ou artefatos temporários nos arquivos da alteração. Não há mudanças em
Domain, contratos de webhook, handlers Telegram, migrations ou regras de
negócio. A implementação está preparada para Draft PR, com aceite final
pendente de smoke e evidência real de Function Calling.
