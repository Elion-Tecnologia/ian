# T09 — Entrada de voz: avaliação e evidências

Data da consulta: 10/11/2026. Escopo: issue
[#17](https://github.com/Elion-Tecnologia/ian/issues/17).

## Estado e decisão

A preparação original de download/autorização foi integrada à `dev`.
A #19 foi concluída na PR #76, squash `207be7a`, fornecendo MAF/Gemini.
Esta continuação implementa `GeminiVoiceTranscriber` na branch
`feature/voice-transcription`, sem ativar o dispatcher de produção.
O handler textual real ainda não existe: `DeferredTelegramHandlers`
continua instalado. A #17 permanece aberta e sem aceite integral.

Nenhum smoke de áudio real foi executado ou autorizado nesta continuação.
A autorização anterior da #19 não se aplica. Nenhuma credencial foi
inspecionada e nenhum áudio foi enviado ao Google. As seções da preparação
original abaixo são históricas; as evidências atuais estão ao final.

## Avaliação documental do candidato Gemini

As informações abaixo são publicadas pelo fornecedor, não resultados de
uma chamada real. O modelo abaixo é referência para estimativa, sem
nome de modelo ou pacote fixado no código.

| Item | Resultado da consulta |
| --- | --- |
| Candidato | Gemini Developer API / Google AI Studio |
| Capacidade declarada | Transcrição e entrada OGG/Opus; validar com voz Telegram em português no smoke |
| Exemplo de modelo na consulta original da #17 | `gemini-3.8-flash`; disponibilidade na conta não verificada naquela etapa |
| Versão de SDK/API testada | Nenhuma; não foram adicionados pacotes de IA |
| Custo publicado, Standard pago | US$ 0,75 por milhão de tokens de entrada e US$ 3,75 por milhão de tokens de saída, incluindo thinking, até 31/12/2026; US$ 1,50 e US$ 7,50 a partir de 01/01/2027 |
| Custo medido | Não medido; o custo final também inclui prompt, saída e possíveis tentativas |
| Quotas | Por projeto/modelo; consultar RPM, TPM e RPD ativos da conta. Não houve inspeção de quota nem teste real de 429 |
| Privacidade | Os níveis Free e Paid têm políticas diferentes de uso de conteúdo; confirmar o nível e os termos antes de enviar dados de clientes |

Fontes oficiais: [áudio e formatos](https://ai.google.dev/gemini-api/docs/audio),
[preços do candidato original](https://ai.google.dev/gemini-api/docs/pricing#gemini-3.8-flash)
e [quotas](https://ai.google.dev/gemini-api/docs/rate-limits).

Atualização da #19 em 10/11/2026: o usuário escolheu
`gemini-3.1-flash-lite` para o IA.N. Os dois smokes de texto e Function
Calling passaram com MAF e SDK oficial; ver [evidências](maf-gemini.md).
Esta tabela preserva a consulta histórica da #17; seus preços não devem
ser aplicados ao modelo agora escolhido. Áudio não foi enviado nem
validado, e o fluxo de voz permanece desativado.

## Contrato Telegram avaliado

Usar `getFile` para resolver `file_id` em `file_path` e baixar do host fixo
`api.telegram.org`. O limite publicado para download no serviço público é
20 MB. URLs de arquivo expiram, portanto cada tentativa resolve novamente
`file_id`; não há cache de URL com token. Redirecionamentos não são seguidos.
`file_size`, MIME e duração declarados podem não estar presentes ou não
comprovar o conteúdo; apenas `file_size` e MIME são opcionais no fluxo local.

Fonte: [Telegram Bot API — getFile](https://core.telegram.org/bots/api#getfile)
e [Voice](https://core.telegram.org/bots/api#voice).

## Evidências locais

Os testes usam somente dados sintéticos. Fakes exercitam a autorização,
transcrição aceita, vazia, inválida, incerta, indisponibilidade, quota,
timeout e cancelamento. Nenhuma dessas respostas fake comprova a precisão
de um modelo real.

O cliente real `TelegramVoiceFileClient` também foi exercitado contra um
servidor Kestrel local, com serialização HTTP e leitura do corpo reais.
A reescrita de URL para loopback existe somente nos testes. Cenários:
download permitido, limite real excedido sem tamanho declarado, arquivo
vazio e timeout durante leitura do corpo. Os testes verificam as requisições
recebidas e o resultado retornado pelo cliente.

Outros testes verificam metadados inválidos, caminhos maliciosos, falhas
HTTP/rede, descarte de streams e limpeza do buffer após uso, inclusive em
falhas. Não há criação de temporários em disco. Não foram enviados comandos,
arquivos ou mensagens a serviços reais durante a validação.

O Postman CLI não está instalado; não houve execução de `postman simulation`
nem uso de mocks publicados. A simulação HTTP local usa os contratos oficiais
acima. Não se reivindica validação por Postman nem pela API Telegram real.

Comandos executados na raiz:

```powershell
dotnet restore Ian.slnx
dotnet build Ian.slnx --configuration Release --no-restore
dotnet test Ian.slnx --configuration Release --no-build --no-restore
```

A restauração exigiu execução fora do sandbox para ler o NuGet.Config do
perfil do usuário. Resultado final: build sem erros ou avisos; 120 testes aprovados,
7 ignorados e nenhuma falha. Os 7 testes PostgreSQL foram ignorados pela
ausência de `IAN_TEST_POSTGRES_CONNECTION_STRING`; não estão validados.

## Continuação após a #19 — adaptador implementado

Consulta técnica em 10/11/2026, sem chamadas ao provedor. Reutiliza:

| Componente | Versão/decisão |
| --- | --- |
| Microsoft.Agents.AI | 1.28.0, existente; agente sem ferramentas |
| Google.GenAI | 2.0.0, existente; único provedor |
| Microsoft.Extensions.AI | 10.11.0 transitivo; `DataContent` e `IChatClient` |
| Modelo | `gemini-3.1-flash-lite`, escolhido pelo usuário |
| Concentus | 2.2.2, novo codec Opus gerenciado, asset net8.0 compatível com .NET 10 |

A versão do codec foi conferida no [índice NuGet](https://api.nuget.org/v3-flatcontainer/concentus/index.json)
e compilada. A dependência é necessária para decodificar pacotes e medir
duração efetiva/silêncio, em vez de confiar em MIME ou duração declarados.
Não é um segundo serviço de transcrição. Usa-se explicitamente o decoder
C# para não depender da descoberta de libopus nativa. Fonte/licença:
[Concentus](https://github.com/lostromb/concentus), BSD-3-Clause.

### Compatibilidade e limites

O [modelo](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite)
declara entrada de áudio. A [documentação de áudio](https://ai.google.dev/gemini-api/docs/audio)
lista OGG/Opus, envio inline, limite total de 20 MB por requisição,
32 tokens/segundo de áudio e duração máxima geral de 9,5 horas por prompt.
Esses limites publicados não são garantia de quota ou acesso da conta.

O adaptador aceita somente um stream OGG/Opus completo, mono ou estéreo,
mapping family 0 e OpusHead versão 1. Rejeita outros formatos, streams
encadeados/multiplexados, corrupção de CRC, sequência/continuação inválida,
headers incompletos, pacotes inválidos e ausência de EOS. Headers/pacotes
maiores que 64 KiB não são suportados neste perfil restrito.
Referências: [Ogg RFC 3533](https://www.rfc-editor.org/rfc/rfc3533) e
[Opus em Ogg RFC 7845](https://www.rfc-editor.org/rfc/rfc7845).

O decoder conta amostras a 48 kHz e verifica granules/pre-skip/trim final.
Granule forjado não permite esconder áudio longo. Os limites efetivos são
`TelegramVoice.MaxDurationSeconds` e o argumento do contrato, o menor;
`MaxBytes` é reaplicado sobre o buffer real, com teto inline de 12 MiB
para acomodar base64 e schema sob 20 MB. Os padrões continuam 5 MiB/120 s.
O timeout cobre validação + chamada e usa o menor entre
`TelegramVoice.TimeoutSeconds` e `Gemini.TimeoutSeconds` (padrões 60 s).
O decorator continua limitando download + transcrição como antes.

Não há conversão, Files API, upload persistente ou retenção de áudio na
aplicação. `DataContent` referencia o buffer emprestado de `TelegramVoiceFile`;
o SDK serializa `inlineData` com `audio/ogg`. Um teste de transporte local
verificou o base64, MIME e schema do SDK instalado sem enviar dados ao Google.
O descarte do arquivo continua responsabilidade do chamador/decorator,
que zera o buffer inclusive em falhas. Scratch de pacote/PCM é zerado
em `finally` e o decoder é reinicializado. Strings/cópias internas do SDK,
JSON/base64 e runtime gerenciado não têm garantia de apagamento físico;
não se promete eliminar toda cópia da memória ou retenção no fornecedor.

### Transcrição e esclarecimento

`GeminiVoiceTranscriber` implementa `IVoiceTranscriber` sem alterar
`IAiModel`. Usa a fábrica da #19 e MAF `ChatClientAgent.RunAsync` com
`UseProvidedChatClientAsIs`, `ToolMode.None`, resposta JSON estruturada
e saída limitada a 4096 tokens. Não existem tools ou operações de negócio.
O prompt fixo pede transcrição literal em português e proíbe completar
nomes, números e comandos ou obedecer instruções contidas no áudio.

A saída contém `status`, `text`, `uncertainSegments`, sem score de confiança.
Silêncio digital/nível muito baixo é detectado por amostras decodificadas
(amplitude absoluta não excede 32/32768); não é um detector de fala nem
uma medida de precisão. Fala baixa pode ser recusada conservadoramente.
Respostas vazias produzem `EmptyTranscription`; status ambíguo/inaudível,
segmentos incertos, JSON inválido, truncamento, conteúdo de ferramenta,
marcadores de inaudível e texto inválido produzem `NeedsClarification`.
Falhas nunca retornam texto para encaminhamento.

Um status positivo do LLM não comprova precisão. Os filtros são barreiras
conservadoras para sinais observáveis, e não detectam todo erro acústico
ou ambiguidade semântica. A qualidade com português, nomes e números
depende do smoke real; regras de negócio/esclarecimento continuam no
futuro pipeline textual. Esta limitação impede ativação automática.

| Falha | Resultado no contrato existente |
| --- | --- |
| Arquivo vazio, corrompido, formato/perfil não suportado | `InvalidFile` |
| Bytes/duração efetivos excedidos | `SizeLimitExceeded` / `DurationLimitExceeded` |
| Silêncio ou texto vazio | `EmptyTranscription` |
| Incompreensível, ambíguo ou saída inválida | `NeedsClarification` |
| HTTP 429 | `RateLimited` |
| Deadline, HTTP 408/504 | `Timeout` |
| Autenticação, modelo, rede/5xx, configuração ou falha inesperada | `TranscriptionUnavailable` |
| Cancelamento do chamador | `OperationCanceledException`, sem fallback externo |

O contrato de voz mantém sua classificação existente, mais agregada que
`AiModelFailure`. Não expõe mensagem/corpo de erro do SDK. Sem retries ou
fallback de modelo; nenhuma transcrição, áudio ou credencial é registrada.

### DI e ativação pendente

`IVoiceTranscriber` e o decorator concreto resolvem por DI, sem chamada
externa na resolução. O decorator recebe explicitamente
`DeferredTelegramHandlers`, evitando ciclo com
`ITelegramAuthorizedUpdateHandler`. O dispatcher continua resolvendo
o handler provisório; não baixa nem transcreve voz em produção.
O update original/identificadores são preservados no encaminhamento
transitório, sem substituir o payload persistido ou burlar autorização.

### Custo, quota e privacidade

Na [tabela Standard paga](https://ai.google.dev/gemini-api/docs/pricing#gemini-3.1-flash-lite)
consultada: US$ 0,50/milhão de tokens de entrada de áudio,
US$ 0,25/milhão para texto e US$ 1,50/milhão de saída incluindo thinking.
É referência publicada, não medição nem garantia de gratuidade; Free/Paid
e RPM/TPM/RPD dependem do projeto/modelo. Não foram consultados saldos,
faturamento ou chaves da conta. Ver [quotas](https://ai.google.dev/gemini-api/docs/rate-limits).

Os [termos do Google](https://ai.google.dev/gemini-api/terms) diferenciam
uso de conteúdo em serviços gratuitos e pagos, com exceções regionais.
Mesmo no pago há retenção limitada para segurança/abuso. Não enviar dados
de clientes nesta validação; confirmar o regime da conta antes de produção.
Não houve consumo nem cobrança de áudio provocados por esta implementação.

## Smoke de áudio preparado — não executado

`GeminiVoiceSmokeTests` possui cinco casos manuais usando o transcritor
de produção via DI. É uma teoria ignorada por padrão, descoberta como um
único teste ignorado sem opt-in. Nenhuma fixture de áudio foi versionada.

`scripts/Prepare-VoiceSmoke.ps1` prepara arquivos sintéticos fora do Git,
com TTS offline pt-BR do Windows e ffmpeg/libopus já instalado. Não chama
Google nem inicia testes. Sua sintaxe foi validada, mas não foi executado:
ffmpeg não está disponível neste ambiente. Não instala ferramentas nem
baixa vozes automaticamente. Use PowerShell no Windows com voz pt-BR offline.

```powershell
./scripts/Prepare-VoiceSmoke.ps1 -OutputDirectory "$env:TEMP/IanVoiceSmoke"
```

| Arquivo/caso | Conteúdo exclusivamente sintético | Critério |
| --- | --- | --- |
| `short.ogg` | TTS: Esta é uma mensagem sintética de teste. | Preservar a frase reconhecível |
| `silence.ogg` | Dois segundos de zeros codificados em Opus | `EmptyTranscription`, sem envio |
| `unintelligible.ogg` | TTS acelerado/filtrado, deliberadamente degradado | `NeedsClarification`; validar a degradação no ensaio |
| `fictional.ogg` | Nome fictício Aurora e número 42 | Preservar os identificadores ou pedir esclarecimento |
| `invalid.ogg` | Bytes ASCII sem contêiner válido | `InvalidFile`, sem envio |

Antes da autorização: modelo `gemini-3.1-flash-lite`, formato OGG/Opus
mono a 48 kHz, MIME `audio/ogg`. Estimativa: **três chamadas** de geração
(frase, degradado, nome/número); silêncio e inválido são barrados localmente.
Sem contagem remota de tokens, Files API, tools ou retries. Cada envio leva
bytes sintéticos + instruções fixas + schema; pode consumir quota e gerar
cobrança por entrada/saída/thinking. Timeout de até 60 s por caso com os
padrões; até 5 MiB/120 s por áudio, sem promessa de custo zero.
O script não comprova que uma frase ficou incompreensível para o modelo.
Qualquer resultado inesperado deve ser diagnosticado, sem tentativas extras
de consumo automaticamente autorizadas.

**Aguardar autorização explícita específica da #17 antes dos comandos abaixo.**
Depois de preparar e revisar fixtures sintéticas e ter a configuração
segura da #19, executar em sequência e interromper na primeira falha:

```powershell
$env:IAN_RUN_VOICE_SMOKE = '1'
$env:IAN_ALLOW_GEMINI_QUOTA = '1'
$env:IAN_VOICE_SMOKE_DIRECTORY = "$env:TEMP/IanVoiceSmoke"
try {
    foreach ($scenario in @('invalid', 'silence', 'short', 'unintelligible', 'fictional')) {
        dotnet test Ian.slnx --configuration Release --filter "Category=GeminiVoiceSmoke&DisplayName~$scenario" --logger 'console;verbosity=detailed'
        if ($LASTEXITCODE -ne 0) { throw 'Interromper para diagnóstico; não repetir automaticamente.' }
    }
} finally {
    Remove-Item Env:IAN_RUN_VOICE_SMOKE, Env:IAN_ALLOW_GEMINI_QUOTA, Env:IAN_VOICE_SMOKE_DIRECTORY -ErrorAction SilentlyContinue
}
```

Os testes registram apenas cenário, modelo, latência, classificação e
presença de texto. O contrato de voz não expõe uso de tokens, portanto
não há contagem de consumo disponível nesse smoke. Não imprime transcrição
ou paths privados. Falhas reais permanecem sem validação nesta entrega.

## Validação local da continuação

Restore, build Release e testes locais sem Google. Resultado:
**204 aprovados, 10 ignorados, 0 falhas; total 214**, build sem avisos/erros.
Os testes exercitam mono/estéreo, Opus real codificado/decodificado,
CRC/truncamento/codec inválido/stream encadeado, duração forjada/excedida
e no limite, silêncio, resposta vazia/incompreensível/incerta, tool inesperada,
429, timeout, rede, cancelamento, limpeza, autorização, identidade e DI.
SDK instalado serializou áudio e schema no transporte sintético, sem rede
Google; os testes anteriores Telegram, voz e IA foram preservados.

Os sete testes `TelegramInboxPostgresTests` continuam **não executados e
não aprovados**, por ausência de `IAN_TEST_POSTGRES_CONNECTION_STRING`:

- `DuplicateWebhookStorageKeepsSingleRow`
- `FailedBusinessTransactionDoesNotConsumeIdempotencyKey`
- `ConcurrentBusinessOperationCommitsOnlyOnce`
- `DatabaseUnavailableDoesNotLosePendingUpdate`
- `ConcurrentConsumersClaimUpdateOnlyOnce`
- `ExpiredLeaseIsRecoveredByAnotherConsumer`
- `RetryRespectsScheduledTime`

Os demais ignorados são os dois smokes textuais da #19, sem autorização
reutilizada, e a teoria de áudio com cinco cenários da #17, não executada.

## Pendências para concluir a #17

1. Preparar/revisar fixtures sintéticas, autorizar quota e executar os cinco
   casos reais, registrando qualidade, latência e limitações sem conteúdo privado.
2. Entregar o handler textual autorizado e seus gates de esclarecimento,
   confirmação e idempotência. A #19 disponibiliza o modelo, não esse handler.
3. Após as duas validações, trocar explicitamente a composição do dispatcher
   pelo decorator sobre o handler textual real, sem dependência circular.
4. Validar webhook/inbox com bot de teste, sem mutações administrativas
   não autorizadas, e executar PostgreSQL em banco descartável.

Revisão restrita à #17, sem mudanças de Domain, endpoints, download,
autorização, banco, migrations ou ferramentas de negócio.
**Publicar Draft PR com `Refs #17`; não fechar nem declarar aceite integral.**
