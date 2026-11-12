# Validação conversacional real controlada (#98)

Não executada nesta entrega. Simulações comprovam os contratos e a orquestração;
não comprovam a qualidade semântica do Gemini real, entrega/leitura no Telegram
ou disponibilidade do túnel. Requer autorização explícita para chamadas reais,
quota Gemini, bot de teste e PostgreSQL **descartável**, sem dados de clientes.

1. Após autorização, use os passos de [API, banco, túnel e webhook](homologacao-local.md#2-api-túnel-https-e-webhook-execução-manual-opcional).
   Não use bancos existentes. Esta correção não acrescenta migrations.
2. Mantenha as categorias de diagnóstico de `appsettings.json` em Information.
   Envie uma mensagem por vez e anote apenas seu `update_id`, encontrado na linha
   `stage=webhook_persisted_or_duplicate`, e o resultado observado.
3. Envie “Queria agendar um serviço”. Deve iniciar coleta; eventual catálogo deve
   acompanhar perguntas específicas, sem encerrar o objetivo.
4. Envie “O que é isto?”. Deve explicar a resposta anterior e manter o pedido.
   Reinicie a API e repita a pergunta: o histórico/contexto devem ser recuperados.
5. Use dados fictícios: montagem para Ana em data futura explícita, Rua Fictícia,
   início às 10h. Deve pedir telefone faltante e esclarecer janelas 08h–18h.
   Informe telefone fictício com DDD: ainda não deve reservar às 10h. Autorize
   explicitamente 08h; só então deve confirmar sete janelas completas.
6. Consulte a agenda no período. Compare o resumo e a consulta com o banco
   descartável. Não aceite afirmação de sucesso sem registro persistido.
7. Registre aprovação/reprovação e os update_id. Não copie tokens, chaves,
   payloads, prompts completos ou dados pessoais para logs/issues. Interrompa
   API/túnel e remova o banco de teste ao concluir.

## Se não houver resposta visível

Correlacione o mesmo `update_id` nos logs; não presuma que ausência visual seja
falha do modelo. O caminho esperado é:

`webhook_persisted_or_duplicate → inbox_claimed → context_loaded → model_interpret_started → model_interpreted → tool_started/tool_completed (quando necessário) → model_dialogue_completed → response_saved → send_started → send_finished → inbox_completed`.

- Sem recebimento persistido: verificar API/túnel/webhook, sem concluir que o
  modelo falhou. `webhook_failed` indica falha antes do HTTP 2xx.
- `model_failed`/`model_interpret_invalid`: provedor/configuração/contrato;
  a resposta de erro deve seguir até o envio. `InvalidActFallback` indica que o
  modelo não gerou um ato final válido; o backend usa o plano/fatos disponíveis.
- `inbox_failed`/`inbox_cancelled`: processamento/lease/reinício; conferir retry
  ou pausa da inbox. Não reexecutar escritas manualmente sem verificar o diário.
- `send_finished state=Failed`: envio recusado; conferir resultado e tentativas.
  Rate limit pode retomar somente a resposta persistida.
- `send_finished state=Uncertain` ou `send_skipped`: entrega incerta, já iniciada,
  concluída ou limite de tentativas. Não reenviar cegamente; não há exactly once
  no Telegram. `Accepted` comprova aceitação pela API, não leitura pela pessoa.

O Gemini faz uma interpretação estruturada antes das Tools e escolhe o ato de
diálogo final. Isso acrescenta uma chamada por turno dentro do limite total de
60s. Texto livre não atesta mutações: o backend compõe perguntas, explicações e
recibos usando contexto e fatos confirmados, com prioridade para recusas,
ambiguidades, confirmações e persistência.
