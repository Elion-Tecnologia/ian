> **Histórico encerrado em 10/11/2026.** Fora do MVP textual. A #17 foi encerrada como Not planned e a PR #77 fechada sem merge. A #78 remove a infraestrutura que chegou à dev. Comandos, pendências e estimativas abaixo pertencem ao escopo anterior e não autorizam ativação ou novos smokes.

# T09 — Entrada de voz: avaliação e evidências

Data da consulta: 10/11/2026. Escopo: issue
[#17](https://github.com/Elion-Tecnologia/ian/issues/17).

## Estado e decisão

Preparação local implementada. O usuário decidiu aguardar a entrega da
[#19](https://github.com/Elion-Tecnologia/ian/issues/19) para integrar o
pipeline autorizado. Essa issue permanece aberta e não há MAF/Gemini no
código consultado. Não foi escolhido nem ativado um provedor de transcrição.

Não foi realizado smoke de transcrição real. Não há credencial de provedor
disponível nas variáveis do processo; o arquivo de User Secrets não pôde
ser consultado por restrição de acesso. Isso não comprova ausência de
credenciais no ambiente do usuário. Não incluir chaves ou áudios privados
neste documento nem em resultados de teste.

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

## Pendências para concluir a #17

1. Entregar a #19 e identificar seu handler textual autorizado.
2. Executar o spike real com áudio sintético OGG/Opus em português, incluindo
   silêncio, fala incompreensível, nomes/números e áudio inválido. Verificar
   duração efetiva, limites, custo/usage e a política de privacidade da conta.
3. Escolher versão/API/modelo e implementar `IVoiceTranscriber`. Propagar
   cancelamento e falhas tipadas, sem ferramentas, retenção de áudio ou
   logs de transcrição. Não tratar autodeclaração de confiança de um LLM
   como medida calibrada de qualidade.
4. Compor `TelegramVoiceUpdateHandler` em DI com o transcritor validado,
   o cliente de arquivo/feedback, `TelegramVoiceOptions.ToLimits()`, o ID
   autorizado e o handler textual da #19. O handler textual deve ser a
   implementação interna, evitando resolução circular do decorator.
5. Validar o fluxo pelo webhook/inbox com bot de teste e verificar que
   ambiguidades semânticas e operações sensíveis respeitam os controles
   do backend antes de qualquer mutação. A transcrição não substitui isso.
6. Documentar resultados reais e revisar segredos, logs e dados pessoais.

Revisão local: mudanças restritas à entrada de voz, configuração e testes;
sem alterações de Domain, banco, migrations ou ferramentas de negócio.
**A #17 ainda não atende todos os critérios de aceite e permanece pendente.**
