# Validação da agenda única — issue #79

**Data:** 11/11/2026  
**Branch:** `feature/single-calendar-domain`  
**Base:** `dev` em `da48cfd`  
**PostgreSQL:** 17.11

## Backup e bancos isolados

O banco de origem `ian_t03_tmp` foi inventariado somente em leitura. O backup
completo em formato custom do `pg_dump` foi salvo fora do repositório em diretório
privado com ACL restrita. SHA-256:
`DF6C46B2B3AF8105B026AD51CBCC5BC39D5A2EDE6FF7C305FA9EA827607A362C`.

- `ian_t79_restore_validation`: restauração integral do backup.
- `ian_t79_upgrade_validation`: nova restauração que recebeu as duas migrations.
- `ian_t79_tests`: banco descartável exclusivo da suíte PostgreSQL.

A comparação verificou 14 tabelas, contagens e hashes de registros, colunas,
chaves, constraints, índices, quatro migrations históricas e dez sequences com
`last_value`/`is_called`. A inbox `Completed` e o registro em
`ian_t08_test_effects` foram preservados. O upgrade isolado terminou com seis
migrations e os mesmos dados e estados de sequence. A origem permaneceu com
quatro migrations e sem alteração de manifesto.

O `pg_restore` normalizou a representação textual de oito checks de enumeração.
A estrutura e 23 entradas sintéticas por check, incluindo valores inválidos e
`NULL`, confirmaram comportamento equivalente.

## Migrations revisadas

- `20261111132422_PrepararAgendaUnica`: cria o arquivo restrito, snapshot,
  manifesto, hashes, mapa `AgendamentoId` → `EquipeId` e adiciona `Versao`.
- `20261111132554_ConsolidarAgendaUnica`: executa preflight, atualiza e confere
  o mapa, remove a coluna ativa e move as tabelas originais para `legado_mvp`.

O SQL gerado foi revisado antes da aplicação isolada. Não contém `DropTable`,
`DROP TABLE`, `DELETE`, `TRUNCATE` ou recriação das tabelas históricas. As quatro
migrations anteriores não têm diff. O modelo EF não possui mudanças pendentes.

## Testes executados

Comandos:

```powershell
dotnet restore Ian.slnx
dotnet build Ian.slnx -c Release
dotnet test Ian.slnx -c Release
dotnet ef migrations has-pending-model-changes `
  --project Ian.Infrastructure --startup-project Ian.Api `
  --configuration Release --no-build
```

Resultado final: **137 aprovados, 0 falhas, 2 ignorados, total 139**. Os dois
ignorados são smokes Gemini deliberadamente desativados. Os testes PostgreSQL
foram executados somente em `ian_t79_tests`, confirmado por
`IAN_TEST_POSTGRES_DATABASE`; a proteção recusa origem, cópia de restauração,
host remoto ou nome não confirmado antes de conectar.

Evidências PostgreSQL aprovadas:

- Upgrade vazio e populado com preservação de registros, IDs, FKs e sequences.
- Preservação dos dois registros técnicos e do vínculo de equipe antigo.
- Rollback ao encontrar sobreposição, período parcial, bloqueio, exceção,
  snapshot ausente ou snapshot alterado.
- Duas conexões reais disputando o advisory lock, com espera observada em
  `pg_stat_activity` e releitura do conflito depois do primeiro commit.
- Intervalos adjacentes permitidos na consulta semiaberta de teste.
- Rollback atômico de mutação, auditoria e chave idempotente.
- Versão obsoleta convertida em falha controlada; retry com versão atual.
- Incremento explícito do pai quando apenas uma ocupação é modificada.
- Timeout de lock e rejeição de transação aninhada/isolamento incompatível.
- Autorização textual privada, inbox, lease, retry e deduplicação preservados.

Os callbacks usados nesses testes são sondas sintéticas da infraestrutura. O
motor de períodos é responsabilidade da #30; os casos de uso completos de criar,
consultar, editar, reagendar, concluir e cancelar pertencem às issues posteriores
e não são considerados aprovados por esta validação.
