# IA.N — Instruções para agentes de desenvolvimento

## Projeto
O IA.N é um agente interno para gestão de uma única agenda de uma loja
de móveis planejados, operado exclusivamente por uma administradora
em conversa privada no Telegram, somente por texto.

## Fontes de contexto
- Use a issue atribuída para determinar o escopo e os critérios de aceite.
- Consulte docs/requisitos/DRS.md para as regras de negócio pertinentes.
- Consulte docs/arquitetura.md para as responsabilidades técnicas.
- Verifique o código existente antes de propor novos componentes.
- Não presuma que uma funcionalidade descrita no DRS já esteja implementada.
- Quando houver conflito entre a issue, a documentação e o código,
  apresente a divergência antes de decidir.
- Consulte docs/convencoes.md para os padrões de implementação, nomenclatura, persistência, segurança e testes.

## Arquitetura aprovada
- .NET 10 com ASP.NET Core Web API.
- Monólito modular: Ian.Api, Ian.Application, Ian.Domain e Ian.Infrastructure.
- PostgreSQL e Entity Framework Core.
- Microsoft Agent Framework com Gemini.
- Telegram Bot API por webhook HTTPS, sem Long Polling.
- Processamento assíncrono por BackgroundService hospedado na API.
- A camada Domain não deve depender de Infrastructure ou Api.

## Invariantes
- Apenas a administradora autorizada pode executar comandos administrativos.
- O webhook deve persistir cada atualização antes de confirmar recebimento HTTP 2xx.
- O processamento de atualizações deve ser recuperável e idempotente.
- O modelo de IA não pode alterar dados diretamente nem ignorar regras de negócio.
- Agendamentos ativos da agenda única não podem possuir períodos
  de ocupação conflitantes.
- Apenas mensagens textuais privadas da administradora podem iniciar comandos.
- Equipes, grupos Telegram, jornadas excepcionais, folgas, bloqueios
  administrativos, resumo automático, prorrogação manual, reabertura e
  dashboards estão fora do MVP.
- DiasCorridos reserva 08h–18h em cada data, inclusive a primeira. Horário
  divergente exige esclarecimento antes de reservar; não assumir início parcial.
- Operações sensíveis exigem confirmação, conforme o DRS.
- Preservar histórico e auditoria das alterações.

## Fluxo de trabalho
1. Identifique a issue, seu escopo, dependências e critérios de aceite.
2. Leia somente os documentos e arquivos pertinentes à implementação.
3. Apresente um plano breve para alterações não triviais.
4. Implemente a menor alteração coerente com a arquitetura existente.
5. Adicione ou ajuste testes dos comportamentos afetados.
6. Execute build e testes aplicáveis.
7. Informe arquivos alterados, verificações executadas,
   resultados e pendências.

## Restrições
- Não ampliar o escopo da issue sem autorização.
- Não adicionar dependências ou alterar contratos públicos sem justificativa.
- Não inserir credenciais no código, nos logs ou na documentação.
- Não executar commits, push ou merge sem solicitação explícita.
- Não declarar testes como aprovados sem executá-los.
- Não modificar regras de negócio aprovadas silenciosamente.

## Git
- main: branch estável.
- dev: branch de integração.
- feature/*: desenvolvimento de funcionalidades.
- Pull Requests para dev.
- Merge, inclusive Squash and merge, somente com autorização explícita.
- Não alterar main durante tarefas de desenvolvimento em feature/*.

## Documentação
Quando uma implementação modificar contratos, decisões técnicas
ou comportamentos documentados, identifique os documentos e
exemplos de API que precisam ser atualizados.
## Transição para o MVP
A #79 implementa Agendamento sem EquipeId, Versao e migrations incrementais
que preservam equipes, exceções, bloqueios e vínculos antigos em legado_mvp.
As quatro migrations históricas devem permanecer intactas. As migrations da
#79 e do catálogo #24 foram aplicadas em ian_t03_tmp após autorização e
validação; qualquer migration posterior continua exigindo autorização específica.
#30 implementa o motor de ocupações no Domain; #31 integra a criação transacional;
#32/#33/#35 integrarão os demais casos de uso.
Testes de infraestrutura não comprovam esses fluxos funcionais.
Testes PostgreSQL exigem conexão descartável local ian_t<numero>_tests e
IAN_TEST_POSTGRES_DATABASE com o mesmo nome; nunca apontar para a origem
ou a cópia de validação da restauração.
