# MVP textual de agenda única — planejamento revisado

Decisão inicial de 10/11/2026, corrigida em 11/11/2026. Uma agenda, uma administradora, comandos textuais privados. DRS 2.1 contém as regras vigentes; o [plano de migração](migracao-agenda-unica.md) distingue o legado implementado do modelo proposto.

## Escopo vigente

- Criar, consultar, editar, reagendar, concluir e cancelar agendamentos individuais.
- DiasCorridos reserva uma janela completa 08h–18h em cada data consecutiva, inclusive na primeira e nos domingos. Sete dias são sete janelas completas.
- Horário informado diferente de 08h em DiasCorridos exige esclarecimento antes de reservar; não assumir início parcial ou corrigir a intenção silenciosamente. Sem horário informado, usar a janela fixa. Minutos mantém seu cálculo próprio.
- Consultas textuais sob demanda e respostas normais no privado da administradora permanecem; #18/#51 tratam o envio e a recuperação de respostas pendentes.
- Sem equipes, grupos, jornadas excepcionais, folgas, bloqueios administrativos, resumo automático, reabertura, prorrogação manual ou dashboards no MVP.
- Nenhuma alteração em Domain, EF Core ou migrations nesta PR. BloqueioAgenda permanece no código legado e só será retirada do modelo ativo na #79, com preservação de registros, intervalos e relações históricas.

## Entregas e estado

PR #77 encerrada sem merge; #17 Not planned. #78 remove a infraestrutura de voz integrada; implementação em feature/remove-voice, Draft PR #80 para dev. #79 acompanha a migração posterior do modelo e a retirada de contratos de grupos, sem execução nesta PR.

A rodada anterior reescreveu 36 issues e sete épicos e anotou a transição em #1/#12/#15/#19 sem reabri-las. Esta revisão corrige as regras e dependências afetadas. As issues #38/#53/#50 foram encerradas como Not planned; #40 continua aberta explicitamente como pós-MVP, fora dos critérios de entrega e das dependências ativas.

## Encerradas como Not planned

| Issue | Escopo retirado |
| --- | --- |
| #17 | Entrada de voz/transcrição. |
| #27 / #28 | Cadastro, renomeação e desativação de equipes. |
| #29 | Vínculo de grupos Telegram. |
| #34 | Transferência entre equipes. |
| #36 / #37 / #39 | Jornadas especiais, compensação de folgas e exceção de expediente. |
| #48 / #49 | Envio e atualização de programação em grupos. |
| #38 | Bloqueios administrativos, globais ou individuais. |
| #53 | Testes de bloqueios/prorrogação que saíram do MVP. |
| #50 | Resumo automático às 07h50. |

Os corpos anteriores das issues encerradas ficam identificados como histórico; seus critérios não são requisitos ativos. Encerrar uma issue não apaga dados do banco.

## Adiada para pós-MVP

#40 permanece aberta para eventual refinamento futuro da operação dedicada de prorrogação por atraso. Não expor essa Tool nem bloquear o MVP por ela. Reabertura também não compõe as seis operações: #35/#45 foram limitadas a concluir/cancelar e às alterações de Agendado. Edição/reagendamento continua sujeito à validação de duração, versão e conflito, sem extensão automática ou silenciosa.

## Issues e épicos mantidos no MVP

| Issue | Escopo vigente |
| --- | --- |
| #2 | [ÉPICO 02] Entrada textual privada e processamento durável Telegram |
| #3 | [ÉPICO 03] MAF, Gemini e conversa textual da agenda única |
| #4 | [ÉPICO 04] Clientes e catálogo de serviços |
| #5 | [ÉPICO 05] Agenda única e ocupação de agendamentos |
| #6 | [ÉPICO 06] Consultas sob demanda e respostas textuais privadas |
| #7 | [ÉPICO 07] Segurança, auditoria e testes do MVP textual |
| #8 | [ÉPICO 08] Operação, backup e release do MVP textual |
| #18 | [T10][P1] Tratar falhas e indisponibilidade de respostas textuais Telegram |
| #20 | [T12][P0] Criar registro de Tools e contrato de resultados |
| #21 | [T13][P0] Persistir contexto conversacional e operação pendente |
| #22 | [T14][P1] Implementar confirmações e ambiguidade no backend |
| #23 | [T15][P2] Endurecer prompts e limites de execução de Tools |
| #24 | [T16][P0] Popular catálogo TipoServico predefinido |
| #25 | [T17][P0] Cadastrar e localizar clientes por telefone único |
| #26 | [T18][P1] Editar cadastro de cliente preservando histórico |
| #30 | [T22][P0] Calcular ocupação da agenda única em minutos e dias corridos |
| #31 | [T23][P0] Criar agendamento na agenda única com proteção concorrente |
| #32 | [T24][P0] Consultar agenda única e disponibilidade por filtros |
| #33 | [T25][P1] Editar e reagendar na agenda única preservando histórico |
| #35 | [T27][P1] Concluir e cancelar agendamentos preservando histórico |
| #41 | [T33][P1] Implementar busca única e desambiguação de agendamentos |
| #42 | [T34][P1] Registrar auditoria de mudanças e ações das Tools |
| #43 | [T35][P0] Implementar Tools de consulta de clientes, serviços e agenda única |
| #44 | [T36][P0] Criar primeiro agendamento textual ponta a ponta na agenda única |
| #45 | [T37][P1] Implementar Tools de edição e estado da agenda única |
| #46 | [T38][P1] Implementar Tools de cadastro e edição de clientes |
| #47 | [T39][P1] Formatar consultas textuais sob demanda da agenda única |
| #51 | [T43][P1] Persistir respostas textuais privadas com retentativas controladas |
| #52 | [T44][P0] Testar duração e conflitos da agenda única |
| #54 | [T46][P1] Testar PostgreSQL e Telegram textual ponta a ponta |
| #55 | [T47][P1] Revisar segredos, autorização textual e privacidade do MVP |
| #56 | [T48][P1] Implementar tratamento de falhas, limites e observabilidade mínima |
| #57 | [T49][P1] Documentar operação local textual e webhook no macOS |
| #58 | [T50][P1] Implementar backup diário e procedimento de restauração |
| #59 | [T51][P1] Homologar o MVP textual de agenda única |
| #60 | [T52][P1] Preparar release local do MVP e retorno seguro |
| #61 | [T53][P3] Avaliar hospedagem contínua da API e webhook |
| #62 | [T54][P2] Validar datas e fuso da agenda única |
| #63 | [T55][P2] Executar testes de diálogos incompletos e retomada de contexto |
| #78 | [MVP] Remover infraestrutura de mensagens de voz integrada à dev |
| #79 | [MVP][P0] Migrar modelo legado para agenda única com preservação de dados |

## Sequência e dependências principais

1. Revisar #78 e o plano #79; sem merge implícito.
2. #57/#58: identificar ambientes e ensaiar backup/restauração; inventário somente leitura antes da #79. O usuário confirmou que ainda não configurou os secrets; isso não prova banco vazio.
3. #79: migrar somente após autorização e reconciliação explícita de conflitos entre equipes antigas, primeiras datas parciais, exceções e efeitos dos bloqueios legados. Arquivar bloqueios integralmente, sem convertê-los em funcionalidade global.
4. #20–#22, #24–#26 e #42: contratos de Tools, pendência/confirmação, catálogo/clientes e auditoria avançam pelas dependências próprias.
5. #30–#33/#35/#41: cálculo com janelas completas, criar/consultar/editar/reagendar/concluir/cancelar e desambiguar; sem dependências de #38/#40/#50/#53.
6. #43/#44: Tools de leitura e criação textual ponta a ponta; depois #45/#46 para edição/estado e clientes.
7. #47 e #18/#51: formatação de consultas sob demanda e envio normal de respostas privadas, sem scheduler de resumo.
8. #52/#54–#56/#62/#63: regressão de domínio, calendário, concorrência, segurança e conversa. #59/#60: homologação/release; #61 avalia hospedagem futura.

As dependências completas estão nos corpos das issues. #9–#16 e #19 permanecem concluídas; a modelagem histórica #12 não significa agenda única implementada. #2–#8 permanecem abertos; #1 mantém a conclusão histórica da fundação.

## Preservação de dados e verificação

O código ainda contém Equipe, ExcecaoExpediente, BloqueioAgenda, EquipeId e o candidato técnico de grupo. Esses objetos serão tratados na #79 após inventário/backup, com dados históricos preservados. Não ampliar períodos antigos silenciosamente para satisfazer as janelas novas nem excluir conflitos entre equipes.

A limpeza técnica #78 removeu 13 arquivos de voz, DI/options/configuração e projeção voice/audio, preservando identificação, texto, integração IA, webhook, inbox, idempotência e auditoria. Sua regressão Release registrou 111 aprovados, nove ignorados e zero falhas. Os sete PostgreSQL continuam não executados, sem banco descartável; dois smokes Gemini permanecem sem opt-in. Esta correção de planejamento não implementa regras novas nem autoriza SQL ou chamadas Google.

Revisão de 11/11/2026: build Release novamente concluído com zero erros/avisos; regressão com 111 aprovados, nove ignorados e zero falhas (total 120). O diff desta continuação contém somente documentação; Domain, EF Core e migrations permanecem intactos. A conferência das dependências ativas não encontrou referências a #38/#40/#50/#53 como pré-requisitos do MVP.
