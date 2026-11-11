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

O usuário autorizou uma rodada de áudio com no máximo três gerações.
Dois cenários locais passaram e `short` terminou em `Timeout` após um
envio; a rodada foi interrompida, sem executar `fictional` ou a fala
degradada. A transcrição real ainda não foi validada com sucesso.
Nenhuma credencial ou transcrição completa foi exibida. As seções da
preparação original abaixo são históricas; as evidências atuais estão ao final.

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
Na rodada autorizada houve um envio de geração, sem resposta textual antes
do timeout. Uso de tokens e valor cobrado não foram disponibilizados;
não é possível concluir se o processamento do provedor terminou ou gerou cobrança.

## Smoke de áudio — preparação e execução parcial

`GeminiVoiceSmokeTests` possui cinco casos manuais usando o transcritor
de produção via DI. É uma teoria ignorada por padrão, descoberta como um
único teste ignorado sem opt-in. Nenhuma fixture de áudio foi versionada.

`scripts/Prepare-VoiceSmoke.ps1` prepara arquivos sintéticos fora do Git,
com TTS offline pt-BR do Windows e ffmpeg/libopus já instalado. Não chama
Google nem inicia testes. Após instalação manual de FFmpeg/ffprobe 9.0.2
com `libopus`, foi executado com Microsoft Maria Desktop pt-BR (SAPI 416).
Os arquivos foram gerados em diretório temporário fora do Git. Não instala
ferramentas nem baixa vozes automaticamente; nenhum WAV intermediário restou.

```powershell
./scripts/Prepare-VoiceSmoke.ps1 -OutputDirectory "$env:TEMP/IanVoiceSmoke"
```

| Arquivo/caso | Conteúdo exclusivamente sintético | Critério |
| --- | --- | --- |
| `short.ogg` | TTS: Esta é uma mensagem sintética de teste. | Preservar a frase reconhecível |
| `silence.ogg` | Dois segundos de zeros codificados em Opus | `EmptyTranscription`, sem envio |
| `unintelligible.ogg` | TTS acelerado/filtrado, deliberadamente degradado | Após revisão auditiva do usuário: transcrição fiel ou `NeedsClarification` |
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

### Validação offline das fixtures

SHA-256 dos cinco arquivos foi reconferido contra a preparação original
antes dos smokes; nenhuma fixture foi regenerada ou substituída.
FFprobe reconheceu quatro arquivos OGG/Opus mono a 48 kHz e FFmpeg os
decodificou sem erros. Um helper temporário compilou o código existente
de `OggOpusValidator` com DLLs locais, em restore offline; confirmou
limites de 5 MiB/120 s e limpeza dos buffers emprestados após descarte.

| Arquivo | Bytes | Duração ffprobe | Efetiva após pre-skip | Validador local |
| --- | --- | --- | --- | --- |
| `invalid.ogg` | 23 | Não aplicável | Não aplicável | `InvalidFile` |
| `silence.ogg` | 620 | 2,006500 s | 2,000000 s | `EmptyTranscription` |
| `short.ogg` | 32422 | 4,011313 s | 4,004813 s | Válido e audível |
| `fictional.ogg` | 38638 | 5,028104 s | 5,021604 s | Válido e audível |
| `unintelligible.ogg` | 9342 | 1,143833 s | 1,137333 s | Válido e audível |

Metadados contêm apenas identificação do encoder; todo conteúdo vem de
frases sintéticas fixas, inclusive nome/número fictícios. Os arquivos,
helper e relatórios TRX permanecem fora do repositório.

### Critério da fala degradada

O usuário confirmou em revisão auditiva offline que a frase continua
compreensível. O teste foi corrigido **antes de qualquer envio desse caso**
para aceitar transcrição fiel da frase sintética original (ignorando
pontuação/espaçamento e maiúsculas) ou `NeedsClarification`. Uma resposta
arbitrária ou com palavras diferentes não passa. Não foi usada uma resposta
do Gemini para ajustar a expectativa; esse cenário sequer foi enviado.
O arquivo mantém seu nome para compatibilidade, mas representa fala
degradada, não um oráculo de incompreensibilidade. Um futuro ensaio negativo
confiável exige outra fixture revisada e autorização própria de consumo.

### Rodada autorizada em 10/11/2026 — interrompida no timeout

Início às 21:01:55 UTC (18:01:55 em America/Sao_Paulo), modelo fixo
`gemini-3.1-flash-lite`. Configuração efetiva confirmada pelo runtime:
5242880 bytes, 120 segundos de áudio e timeout de 60 segundos. Somente
as cinco fixtures sintéticas previamente confirmadas foram usadas.

| Ordem/cenário | Resultado do smoke | Classificação | Latência da invocação | Gerações despachadas |
| --- | --- | --- | --- | --- |
| 1. `invalid` | Aprovado; rejeição local sem texto | `InvalidFile` | 29 ms | 0 |
| 2. `silence` | Aprovado; rejeição local sem texto | `EmptyTranscription` | 67 ms | 0 |
| 3. `short` | **Falhou**; sem transcrição antes do deadline | `Timeout` | 60013 ms | 1 |
| 4. `fictional` | Não executado após a falha | Não avaliado | Não medida | 0 |
| 5. Fala degradada | Não executado após a falha | Não avaliado | Não medida | 0 |

O processo `short` começou às 21:02:22 UTC. A latência inclui validação
local e espera pela chamada; não é uma medida isolada do servidor.
**Total: 1 de até 3 gerações autorizadas**, sem repetição. O teto de três
não foi atingido nem usado para tentar novamente depois da falha.
O SDK já tinha retries desabilitados; não foram adicionados retries ou
troca de modelo. Nenhum texto completo ou credencial foi registrado.

O transporte de teste `GeminiSmokeTransport`, aplicado à fábrica/SDK de
produção resolvidos por DI, conta requisições no ponto de despacho HTTP.
Bloqueia todos os envios em `invalid`/`silence` e permite no máximo uma
geração por caso restante, somente no endpoint HTTPS do modelo autorizado.
Demais destinos/novas tentativas são bloqueados antes da rede; nesta rodada
o contador de bloqueios foi zero. Os números são envios HTTP, não uma
confirmação de faturamento. Não altera transporte ou comportamento de produção.
Cinco testes locais com HTTP fake verificam limite zero/um e destinos recusados.

Cada cenário executado foi filtrado separadamente e teve seu resultado TRX conferido.
Os comandos de `fictional` e fala degradada não chegaram a ser executados.
Todos os opt-ins foram removidos em `finally` e a regressão roda com smokes
desabilitados. **Nenhuma nova chamada nesta execução.**

Diagnóstico disponível: deadline aplicado e falha tipada sem texto inventado.
Não há evidência suficiente para atribuir a causa ao modelo, quota,
autenticação ou conectividade. Não foi aumentado o timeout nem alterado
o adaptador para contornar a falha. Investigação com novos envios ou
reexecução depende de diagnóstico e autorização explícita adicional.

**Os comandos abaixo são referência para uma futura rodada, após nova autorização.**
Depois de preparar e revisar fixtures sintéticas e ter a configuração
segura da #19, executar em sequência e interromper na primeira falha:

```powershell
$env:IAN_RUN_VOICE_SMOKE = '1'
$env:IAN_ALLOW_GEMINI_QUOTA = '1'
$env:IAN_VOICE_SMOKE_DIRECTORY = "$env:TEMP/IanVoiceSmoke"
try {
    foreach ($scenario in @('invalid', 'silence', 'short', 'fictional', 'unintelligible')) {
        dotnet test Ian.slnx --configuration Release --filter "Category=GeminiVoiceSmoke&DisplayName~$scenario" --logger 'console;verbosity=detailed'
        if ($LASTEXITCODE -ne 0) { throw 'Interromper para diagnóstico; não repetir automaticamente.' }
    }
} finally {
    Remove-Item Env:IAN_RUN_VOICE_SMOKE, Env:IAN_ALLOW_GEMINI_QUOTA, Env:IAN_VOICE_SMOKE_DIRECTORY -ErrorAction SilentlyContinue
}
```

Os testes registram apenas cenário, modelo, limites, latência, classificação,
presença de texto e contadores de despacho/bloqueio. O contrato de voz
não expõe uso de tokens, portanto
não há contagem de consumo disponível nesse smoke. Não imprime transcrição
ou paths privados. Timeout foi observado no serviço real; não houve
transcrição real bem-sucedida, teste real de 429 ou confirmação de precisão.

## Validação local da continuação

Restore, build Release e testes locais sem Google. Resultado:
**209 aprovados, 10 ignorados, 0 falhas; total 219**, build sem avisos/erros.
Esse resultado é da regressão local, separada da rodada de smokes acima,
na qual houve dois aprovados e uma falha real que não é contabilizada
como aprovada nem ocultada pelo resultado da suíte comum.
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

Os demais ignorados são os dois smokes textuais da #19 e a teoria de áudio
da #17, desabilitados na regressão após remover as flags. Os resultados
da rodada parcial real estão registrados separadamente, incluindo a falha.

## Diagnóstico local do timeout, sem novo envio ao Google

O checkout desta análise é `feature/voice-transcription` no commit
`6ed3a107`. O CI desse commit concluiu com sucesso. O resumo histórico da
rodada e seus contadores está nesta página e na Draft PR #77; os arquivos
TRX brutos, guardados fora do Git no ambiente anterior, não estavam
disponíveis neste checkout macOS. Portanto, não há marcos internos da
requisição real além dos 60013 ms totais e de um despacho HTTP contado.

Fluxo confirmado: `GeminiVoiceTranscriber` cria um deadline vinculado de
60 s, valida OGG/Opus e silêncio, cria um `ChatClientAgent` sem tools nem
histórico e chama `RunAsync`. `GeminiChatClientFactory` entrega ao MAF o
`GoogleGenAIChatClient` do Google.GenAI 2.0.0, com `HttpOptions.Timeout`
de 60 s, `RetryOptions.Attempts = 1` e `HttpClient.Timeout` infinito.
O `GeminiSmokeTransport` de teste só valida método/destino, incrementa
contadores antes de `base.SendAsync` e impõe uma geração por cenário.
Ele não lê nem altera o corpo. A contagem de um envio não prova que a
rede entregou o corpo ou que o provedor o processou.

Testes com SDK/MAF instalados e respostas HTTP sintéticas confirmaram
`inlineData` com bytes OGG e MIME `audio/ogg`, instrução de sistema,
schema JSON, `responseMimeType=application/json`, `maxOutputTokens=4096`
e ausência de tools. O transporte de smoke no mesmo caminho permitiu uma
requisição e bloqueou zero. Testes adicionais confirmaram que o token
de deadline e o cancelamento do chamador chegam ao `HttpMessageHandler`,
com uma tentativa; o primeiro vira `Timeout`, o segundo propaga
`OperationCanceledException`. Esses resultados não medem DNS, TLS,
upload, espera do servidor, resposta ou parsing da chamada real.
As [referências oficiais de áudio](https://ai.google.dev/gemini-api/docs/audio)
e do [modelo](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite)
listam entrada OGG, transcrição e saída estruturada. Isso confirma a
compatibilidade declarada, não o sucesso da requisição da rodada.

Hipóteses, em ordem de evidência:

1. A espera ocorreu após a entrada no transporte: resolução/conexão,
   envio, processamento remoto ou leitura da resposta. O contador é
   incrementado antes do envio efetivo e não distingue essas etapas.
2. Latência de processamento remoto é possível, inclusive para áudio;
   não houve cabeçalho, corpo nem usage observados para comprová-la.
3. O prompt, o schema e o limite de 4096 tokens podem afetar geração e
   custo, mas são pequenos/suportados e o limite não obriga geração longa.
   Nenhum `thinkingConfig` foi enviado. Não há evidência de que qualquer
   desses itens explique os 60 s.
4. A instrumentação de teste pode afetar a requisição apenas por sua
   validação síncrona e pelo teto. A integração sintética passou; não há
   sinal de bloqueio na rodada real. Um efeito de transporte/rede no
   ambiente real continua indeterminado.

O SDK documenta timeout por tentativa HTTP e continuação automática se
uma resposta terminar com `CONTINUATION`; cada continuação pode ser
cobrada. O teto do smoke bloquearia uma segunda requisição do mesmo
cenário, e o contador observado foi um. Não há evidência de continuação
na rodada. O deadline externo começa antes da validação e, por isso,
pode vencer antes do timeout interno do SDK.

Próxima instrumentação proposta, somente após revisão: medir com
`Stopwatch` a validação local, a criação do cliente, a entrada/saída do
MAF e, em um `DelegatingHandler` de diagnóstico, entrada no transporte e
chegada de cabeçalhos/erro/cancelamento. Registrar apenas cenário,
etapa, duração, classificação, status HTTP numérico e contadores; sem
URL, headers, áudio, prompt, resposta, token ou chave. O handler HTTP
não separa sozinho DNS, TLS, upload e processamento remoto. Nenhuma
alteração de produção, timeout, modelo ou retry foi feita nesta análise.

Validação futura proposta, **dependente de nova autorização**: no máximo
três gerações adicionais, uma por cenário, em ordem `short`, `fictional`
e fala degradada; parar na primeira falha e não repetir. O áudio degradado
continua audível, então aceita transcrição fiel ou esclarecimento.
Permanece fora dessa rodada um teste negativo de fala realmente
incompreensível. Há possível consumo de quota/cobrança inclusive em
timeout. Preço Standard pago publicado para `gemini-3.1-flash-lite`:
US$ 0,50/milhão de tokens de áudio de entrada, US$ 0,25/milhão de texto
de entrada e US$ 1,50/milhão de saída, incluindo thinking; usage real e
regime da conta permanecem desconhecidos.

Nesta análise, `dotnet test Ian.slnx --configuration Release --no-restore
-m:1 -nodeReuse:false` compilou sem erros e aprovou **211 testes,
ignorou 10 e falhou 0**. Os dois testes
adicionais de cancelamento e asserções de serialização usaram somente
HTTP sintético. Os sete PostgreSQL permanecem não executados; os outros
três ignorados são os smokes de IA/voz sem opt-in.

## Pendências para concluir a #17

1. Localizar a etapa real do timeout; obter nova autorização antes
   de repetir `short` ou executar os dois envios restantes. A validação de
   transcrição real e nomes/números continua pendente. O caso degradado
   não comprova o comportamento com fala de fato incompreensível.
2. Entregar o handler textual autorizado e seus gates de esclarecimento,
   confirmação e idempotência. A #19 disponibiliza o modelo, não esse handler.
3. Após as duas validações, trocar explicitamente a composição do dispatcher
   pelo decorator sobre o handler textual real, sem dependência circular.
4. Validar webhook/inbox com bot de teste, sem mutações administrativas
   não autorizadas, e executar PostgreSQL em banco descartável.

Revisão restrita à #17, sem mudanças de Domain, endpoints, download,
autorização, banco, migrations ou ferramentas de negócio.
**Publicar Draft PR com `Refs #17`; não fechar nem declarar aceite integral.**
