# MVP textual de agenda única — planejamento revisado

Decisão aprovada em 10/11/2026. Uma agenda, uma administradora, comandos textuais privados. Stack e controles técnicos preservados. DRS 2.0 contém as regras vigentes; o [plano de migração](migracao-agenda-unica.md) distingue o legado implementado do modelo proposto.

## Entregas desta revisão

- PR #77 encerrada sem merge e #17 encerrada como Not planned.
- #78 criada para limpeza da infraestrutura de voz integrada em dev; implementação em feature/remove-voice, PR para dev.
- #79 criada para migração posterior do modelo, preservação de dados e retirada dos contratos legados de grupos. Nenhuma migration gerada/aplicada nesta revisão.
- 36 issues de execução e sete épicos abertos reescritos; notas de transição nas entregas concluídas #1/#12/#15/#19, sem reabri-las ou apagar suas evidências.
- Equipes, grupos, jornadas excepcionais, folgas e dashboards fora do MVP. Não havia uma issue exclusiva de dashboard no backlog consultado; não foi criada uma entrega equivalente.

## Encerradas como Not planned

| Issue | Escopo retirado |
| --- | --- |
| #17 | Entrada de voz/transcrição. |
| #27 | [T19][P0] Cadastrar, consultar e renomear equipes |
| #28 | [T20][P1] Desativar equipe com verificação de agenda futura |
| #29 | [T21][P1] Descobrir e confirmar vínculo de grupo Telegram |
| #34 | [T26][P1] Transferir serviço entre equipes |
| #36 | [T28][P1] Modelar jornadas especiais por equipe |
| #37 | [T29][P1] Prorrogar serviços em folgas integrais |
| #39 | [T31][P2] Permitir exceção explícita fora do expediente |
| #48 | [T40][P1] Enviar programação ao grupo somente sob aprovação |
| #49 | [T41][P2] Detectar mudança posterior e oferecer atualização da programação |

O conteúdo anterior dessas issues foi preservado como histórico, com a decisão de encerramento anexada. Nenhum encerramento removeu registros ou alterou banco.

## Issues e épicos mantidos

| Issue | Escopo vigente |
| --- | --- |
| #2 | [ÉPICO 02] Entrada textual privada e processamento durável Telegram |
| #3 | [ÉPICO 03] MAF, Gemini e conversa textual da agenda única |
| #4 | [ÉPICO 04] Clientes e catálogo de serviços |
| #5 | [ÉPICO 05] Agenda única, ocupação e bloqueios |
| #6 | [ÉPICO 06] Consultas e resumo privado da agenda única |
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
| #35 | [T27][P1] Concluir, cancelar e reabrir agendamentos |
| #38 | [T30][P1] Criar e remover bloqueios da agenda única |
| #40 | [T32][P2] Prorrogação manual de serviço atrasado |
| #41 | [T33][P1] Implementar busca única e desambiguação de agendamentos |
| #42 | [T34][P1] Registrar auditoria de mudanças e ações das Tools |
| #43 | [T35][P0] Implementar Tools de consulta de clientes, serviços e agenda única |
| #44 | [T36][P0] Criar primeiro agendamento textual ponta a ponta na agenda única |
| #45 | [T37][P1] Implementar Tools de edição e estado da agenda única |
| #46 | [T38][P1] Implementar Tools de clientes e bloqueios da agenda única |
| #47 | [T39][P1] Formatar programação textual da agenda única para a administradora |
| #50 | [T42][P1] Enviar resumo privado da agenda única às 07h50 |
| #51 | [T43][P1] Persistir respostas e resumos privados com retentativas controladas |
| #52 | [T44][P0] Testar duração e conflitos da agenda única |
| #53 | [T45][P1] Testar bloqueios e prorrogação manual da agenda única |
| #54 | [T46][P1] Testar PostgreSQL e Telegram textual ponta a ponta |
| #55 | [T47][P1] Revisar segredos, autorização textual e privacidade do MVP |
| #56 | [T48][P1] Implementar tratamento de falhas, limites e observabilidade mínima |
| #57 | [T49][P1] Documentar operação local textual e webhook no macOS |
| #58 | [T50][P1] Implementar backup diário e procedimento de restauração |
| #59 | [T51][P1] Homologar o MVP textual de agenda única |
| #60 | [T52][P1] Preparar release local do MVP e retorno seguro |
| #61 | [T53][P3] Avaliar hospedagem contínua da API e webhook |
| #62 | [T54][P2] Validar datas e fuso da agenda única e do resumo |
| #63 | [T55][P2] Executar testes de diálogos incompletos e retomada de contexto |
| #78 | Limpeza textual já implementada nesta branch, aguardando PR/revisão. |
| #79 | Persistência/contratos da agenda única; proposta aguardando revisão. |

## Sequência e dependências principais

1. Revisar/validar #78 e o plano #79. Sem merge implícito.
2. #57/#58: identificar ambientes e ensaiar backup/restauração; inventário somente leitura antes da migração #79. Ausência de conexão neste ambiente não prova banco vazio.
3. #79: migrar após autorização, reconciliando conflitos legados. Não implementar funcionalidades usando uma equipe fictícia para satisfazer FK antiga.
4. #20–#22, #24–#26 e #42: contratos de Tools, pendência/confirmação, catálogo/clientes e auditoria podem avançar pelas dependências próprias. Casos de uso devem usar contratos finais.
5. #30–#33/#35/#38/#40/#41: duração, escrita/consulta, estado, bloqueio e prorrogação da agenda única; nenhuma referência funcional a equipes.
6. #43/#44: Tools de leitura e criação textual ponta a ponta; depois #45/#46 para alterações, clientes e bloqueios.
7. #18/#51/#47/#50: saída textual resiliente, envio persistente, formatação e resumo privado. #51 não depende do resumo para evitar ciclo; #50 depende do transporte persistente.
8. #52–#56/#62/#63: regressão de domínio, concorrência, segurança e conversa. #59/#60: homologação/release; #61 é avaliação futura de hospedagem.

As dependências completas estão nos corpos atualizados das issues. As tarefas concluídas #9–#16 e #19 permanecem concluídas; a #12 descreve esquema legado e não significa agenda única implementada. Os épicos #2–#8 permanecem abertos com escopo reduzido; #1 mantém a conclusão histórica da fundação.

## Estado técnico verificável

A limpeza removeu 13 arquivos exclusivos de voz, DI/options/configuração e projeção de mídia; mantém os três componentes de IA, webhook, processamento durável, idempotência e auditoria. O parser preserva identificação/texto e não projeta voice/audio. Os testes cobrem mensagem de mídia já armazenada e recém-projetada sem executar comando.

O código ainda possui Equipe/ExcecaoExpediente/EquipeId e o candidato técnico de grupo, sem casos de uso de agenda ou vínculo ativo. Sua remoção integra #79 após revisão, preservando histórico. Não confundir essa presença com requisito vigente.

Verificação local: restore concluído, build Release com zero avisos/erros, 111 aprovados, nove ignorados, zero falhas. Sete ignorados são PostgreSQL sem banco descartável; dois são smokes Gemini sem opt-in. Nenhum SQL PostgreSQL ou envio Google foi executado.
