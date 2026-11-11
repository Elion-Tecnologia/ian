# Agenda única — migração incremental e operação (#79)

**Revisão:** 11/11/2026. Implementação autorizada após inventário e ensaio de restauração.
**Base implementada:** dev, 700c220 (PR #81).
**Origem ian_t03_tmp:** migrations da #79 aplicadas em 11/11/2026 após autorização; migrations posteriores exigem nova autorização específica.

## 1. Escopo e divisão de responsabilidades

A #79 entrega modelo/persistência, preservação do legado, retirada dos contratos de
grupos e infraestrutura básica de concorrência. Agendamento não possui EquipeId;
Versao é long, inicia em 1, tem check >=1 e é token de concorrência EF.
PeriodoOcupacao conserva Id, AgendamentoId, InicioUtc e FimUtc, cardinalidade 1:N
e intervalos semiabertos. Cliente, TipoServico, auditoria, solicitações pendentes,
envios, inbox e barreira idempotente permanecem. Não existe entidade Agenda.

A #30 implementará o motor determinístico: America/Sao_Paulo, todos os dias,
08h–18h; DiasCorridos usa janelas completas inclusive na primeira data e domingos,
1 dia = 1 janela, 7 dias = 7 datas consecutivas. Horário informado diferente de 08h
exige esclarecimento sem reserva. Minutos consome expediente e continua no dia
seguinte; 240 minutos às 16h produz 16h–18h e 08h–10h no dia seguinte.
Nenhum desses cálculos de criação foi implementado na #79.

As verificações SQL da migração apenas rejeitam dados ativos incompatíveis;
não são motor de geração de períodos. Os casos de uso #31–#33/#35, autorizações,
confirmações persistentes e tools deverão integrar o protocolo antes da ativação
funcional. Testes de callbacks sintéticos não comprovam esses casos de uso.

## 2. Inventário e backup comprovados

O usuário confirmou que ian_t03_tmp em 127.0.0.1:5432 é desenvolvimento/testes
e o único banco relevante. PostgreSQL 17.11; quatro migrations com EF 10.0.14:

- 20261109134948_InitialDatabase
- 20261109145017_DomainModel
- 20261109194028_TelegramUpdateInbox
- 20261110155523_TelegramInboxProcessing

Todas permanecem intactas. Inventário somente leitura encontrou 14 tabelas:
__EFMigrationsHistory (4), telegram_updates_inbox (1, Completed),
ian_t08_test_effects (1), e zero registros nas outras onze tabelas
(clientes, tipos_servico, equipes, agendamentos, periodos_ocupacao,
excecoes_expediente, bloqueios_agenda, registros_auditoria,
solicitacoes_pendentes, envios_telegram e telegram_business_operations).

Não havia sobreposições, períodos parciais, FKs órfãs ou compromissos afetados por
bloqueios/exceções. Isso não prova prevenção de conflitos. Existem oito FKs e dez
sequences de identidade; algumas avançaram apesar das tabelas vazias.
Não foram encontradas views, triggers próprias, funções próprias ou exclusão de
sobreposições. A tabela auxiliar de teste não pertence ao modelo EF e é preservada.

Backup completo custom de pg_dump foi salvo fora do Git em diretório privado
sob %LOCALAPPDATA%/Ian/Backups, com ACL restrita ao usuário executor.
SHA-256: DF6C46B2B3AF8105B026AD51CBCC5BC39D5A2EDE6FF7C305FA9EA827607A362C.
Restaurado com pg_restore --single-transaction --exit-on-error em
ian_t79_restore_validation, banco novo. Comparação das 14 tabelas, hashes de
registros, colunas, chaves, índices, migrations, sequences/last_value/is_called:
aprovada; os dois registros técnicos foram preservados.

A comparação textual inicial acusou oito checks de enumeração: pg_restore moveu
casts de varchar para text do array aos elementos. A verificação estrutural e
23 entradas sintéticas por check (válidas, inválidas e NULL) comprovaram equivalência.
Não houve diferença de dados. Evidências e manifestos completos ficam no diretório
privado do backup. Nunca publicar dump, payloads ou credenciais.

Backup de banco não substitui inventário de roles/objetos globais para outro
cluster. O ensaio atual ocorreu no mesmo cluster. Backup diário/retencão é #58.

## 3. PrepararAgendaUnica

Migration 20261111132422_PrepararAgendaUnica:

1. Adquire pg_advisory_xact_lock(790,1) e locks de tabelas na transação EF.
2. Cria legado_mvp sem acesso PUBLIC.
3. Copia integralmente agendamentos, equipes, exceções e bloqueios para
   registros_preparacao (Tabela, Id, Dados JSONB). A cópia é anterior a Versao.
4. Cria agendamentos_equipes com PK AgendamentoId, FK restritiva para agendamento
   e equipe, e instante de arquivamento. Preserva o vínculo original.
5. Registra contagens e hashes de integridade no manifesto. Não são assinatura
   de segurança nem substituto de backup.
6. Adiciona Versao com default 1 e check >=1. Tabelas antigas continuam ativas.

O arquivo não é mapeado pelo DbContext nem exposto às tools. Manter privilégios
de leitura restritos; eventual acesso de auditoria exige concessão explícita.
As tabelas e registros técnicos fora desse conjunto não são reescritos.

## 4. Reconciliar e ConsolidarAgendaUnica

Migration 20261111132554_ConsolidarAgendaUnica, transacional:

1. Adquire a mesma chave global e bloqueia tabelas para excluir escritores legados.
2. Aborta se o snapshot de preparação estiver incompleto ou com hashes divergentes.
3. Aborta em sobreposição ativa, ausência/incompatibilidade de ocupações, primeiras
   datas parciais, duração incompatível ou efeitos de bloqueios/exceções em ativos.
4. Atualiza o mapa AgendamentoId→EquipeId a partir da origem sob lock, capturando
   também mudanças entre fases; confere igualdade dos pares nos dois sentidos.
   O snapshot de preparação permanece intacto.
5. Registra contagens finais. Remove apenas a FK/índice/coluna EquipeId ativa.
6. Move as tabelas originais equipes, excecoes_expediente e bloqueios_agenda para
   legado_mvp com ALTER TABLE SET SCHEMA. Mantém registros, PKs, FKs, índices e
   sequences de identidade vinculadas. Não usa DropTable nem recria identidades.
7. Cria IX_agendamentos_Estado_InicioPrevistoUtc. Mantém os índices de ocupações,
   cliente, serviço, auditoria e controles técnicos.

Nenhuma reserva é cancelada, ampliada, recalculada, transferida ou excluída pela
migração. Concluídos/cancelados conservam períodos antigos inclusive parciais.
Uma falha faz rollback da fase; preparação já aplicada permanece disponível.

Antes da consolidação real: backup atualizado, ensaio, revisão de SQL, decisão
auditada sobre incompatibilidades e janela de manutenção. Parar novos claims e
escritas de negócio, drenar transações em voo e repetir inventário. O webhook
continua persistindo antes de 2xx, ou deve retornar falha para reentrega caso não
consiga persistir; nunca confirmar apenas em memória. Inventariar leases e
pendências; não reativar comandos pausados nem reenviar grupos por inferência.
Restrições técnicas não substituem a decisão da administradora sobre legado.

## 5. Concorrência implementada e limites

Ordem única: transação READ COMMITTED → chave idempotente
(UpdateId, OperationKey) → advisory lock global (790,1) → nova leitura
de disponibilidade/estado/versão/confirmação → escrita, ocupações e auditoria →
commit. Não inverter locks nem abrir transações independentes.

TelegramBusinessOperationExecutor é o proprietário da transação, recusa transação
aninhada ou mudanças pendentes, limpa snapshots rastreados anteriores e confirma
a chave idempotente junto com o callback. Rollback limpa o ChangeTracker.
AgendaMutationExecutor compõe esse executor e adquire PostgresAgendaLock antes
do callback. Lock exige transação READ COMMITTED; timeout de lock é 5 segundos.
Versão obsoleta vira AgendaVersionConflictException; indisponibilidade do lock
vira AgendaLockUnavailableException. Sem retry automático de escrita.

IncrementarVersao() é explícito, com overflow verificado. Os futuros casos de uso
devem chamá-lo em cada mutação, inclusive alteração somente de PeriodoOcupacao,
e comparar a versão esperada da confirmação com a versão atual. O EF inclui a
versão original no UPDATE; não incrementa automaticamente nem valida sozinho
a intenção conversacional. O callback deve consultar ocupações de pais Agendados
usando inícioA < fimB e inícioB < fimA, após adquirir o lock.

Todos os escritores precisam aderir ao protocolo. Não existe constraint de
exclusão; SQL manual que ignore o lock não recebe essa garantia. Índices não são
proteção contra corrida. As estruturas não implementam os casos de uso completos.

Referências: [PostgreSQL locks](https://www.postgresql.org/docs/17/explicit-locking.html#ADVISORY-LOCKS),
[READ COMMITTED](https://www.postgresql.org/docs/17/transaction-iso.html#XACT-READ-COMMITTED),
[EF concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

## 6. Bancos e testes

- ian_t03_tmp: origem de desenvolvimento, com as seis migrations até a #79; não usar em testes destrutivos.
- ian_t79_restore_validation: cópia do backup para comprovar restauração.
- ian_t79_tests: exclusivo e descartável; testes podem apagar seus schemas/dados.
- ian_t79_upgrade_validation: segunda restauração do backup; upgrade revisado para seis migrations preservou todos os dados originais e dez sequences.
- Cópias adicionais de upgrade devem ter nome distinto e ser criadas novas,
  nunca sobrescrever origem ou cópia de restauração.

IAN_TEST_POSTGRES_CONNECTION_STRING só é aceita nos testes para host loopback,
banco com padrão ian_t<numero>_tests e nome idêntico a IAN_TEST_POSTGRES_DATABASE.
A proteção vale também para os sete testes existentes da inbox.
Ausência da conexão ignora testes; destino inseguro falha antes de conectar.

Cobertura #79: upgrade vazio; dados sintéticos com equipes e estados variados;
hashes dos registros antes/depois; snapshot e mapa; sequences inclusive is_called;
FKs; registros técnicos; recusa e rollback de conflitos, períodos parciais,
bloqueios, exceções e arquivo alterado; duas conexões reais e espera observada
em pg_stat_activity; conflito reconsultado depois do commit; adjacência;
versão obsoleta; mutação de ocupação com incremento do pai; timeout, rollback e
idempotência; autorização textual e recuperação da inbox.

Fakes de Gemini/Telegram; smokes Gemini explicitamente desativados.
Resultados executados são registrados no relatório de validação da entrega.
#30 cobre geração de janelas e seus testes funcionais completos. #31–#33/#35
cobrem integração das mutações, confirmações e tools; não estão comprovadas aqui.

## 7. Retorno

Ambas as novas migrations recusam Down automaticamente. Antes de escritas novas,
retorno exige restauração coordenada em base nova com aplicação compatível e
preservação dos updates recebidos após o backup. Não restaurar sobre a origem
nem presumir que um backup antigo contém a inbox mais recente.

Após escritas sem equipe, não existe maneira automática de inferir equipe antiga.
Preferir correção adiante; retorno exige exportar/reconciliar os dados novos,
decisão explícita e ensaio. O teste comprova que Down recusa sem apagar a escrita
nova; não comprova um retorno funcional completo após operação real.
Nunca usar database update 0, DROP DATABASE ou TRUNCATE em bases existentes.
