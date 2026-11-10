# IA.N — Documento de Requisitos de Software (DRS)

**Projeto:** IA.N — Agente de IA para Gestão de Agendas  
**Versão:** 1.0 — Revisão técnica  
**Data original:** 05/11/2026  
**Última revisão:** 09/11/2026  
**Situação:** Requisitos de negócio aprovados; arquitetura principal definida; detalhes técnicos identificados como pendências quando aplicável  
**Repositório:** Elion-Tecnologia/ian  
**Meta original de entrega:** 15/11/2026  
**Equipe:** Um desenvolvedor

---

## 1. Visão geral

### 1.1 Objetivo

Desenvolver um agente de inteligência artificial para auxiliar a administradora de uma loja de móveis planejados no gerenciamento de clientes, equipes, agendamentos, disponibilidade e comunicação das programações.

A interação administrativa ocorrerá por meio de um bot no Telegram, utilizando mensagens de texto e áudio. O agente interpretará instruções em linguagem natural com Gemini, integrado ao Microsoft Agent Framework (MAF).

O backend será responsável por executar as operações, validar as regras de negócio, persistir informações e garantir a consistência dos agendamentos.

A aplicação utilizará uma **ASP.NET Core Web API**, que receberá as atualizações do Telegram por webhook HTTPS.

### 1.2 Objetivos específicos

- Criar, consultar, editar, reagendar, transferir, concluir, cancelar e reabrir agendamentos.
- Gerenciar clientes reutilizáveis e seus históricos.
- Administrar múltiplas equipes com agendas independentes.
- Impedir conflitos de horários dentro de uma mesma equipe.
- Controlar expedientes, folgas e bloqueios.
- Interpretar solicitações administrativas por texto e áudio.
- Manter o contexto de conversas e solicitações incompletas.
- Enviar programações aos grupos das equipes mediante autorização.
- Enviar resumos diários à administradora às 07h50.
- Registrar operações, falhas, tentativas e histórico de alterações.
- Garantir processamento recuperável de mensagens recebidas pelo webhook.

### 1.3 Fora do escopo

Não fazem parte do escopo funcional aprovado:

- Atendimento automatizado de clientes finais.
- Integração com WhatsApp.
- Painel web administrativo.
- Autenticação convencional com login e senha para múltiplos usuários.
- Contas administrativas para membros das equipes.
- Gerenciamento do catálogo de tipos de serviço por conversa.
- Exclusão definitiva de agendamentos.
- Operações de alteração em lote.
- Respostas em áudio.
- Lembretes individuais automáticos.
- Alertas automáticos de agendamentos atrasados.
- Especialização de equipes por tipo de serviço.
- Distribuição automática de serviços entre equipes.

---

## 2. Atores e canais de comunicação

### 2.1 Administradora

Apenas uma pessoa autorizada poderá administrar o sistema.

A autenticação operacional será realizada pela identificação do usuário no Telegram, validada pelo backend.

A administradora poderá utilizar mensagens de texto e áudio, mas todas as respostas produzidas pelo agente serão em texto.

### 2.2 Equipes

Cada equipe possuirá uma agenda independente.

As equipes não poderão enviar comandos administrativos ao agente.

Cada equipe poderá possuir um grupo Telegram associado, utilizado exclusivamente para receber programações autorizadas.

### 2.3 Clientes

Clientes são pessoas atendidas pela loja.

Cada cliente terá cadastro reutilizável e poderá estar associado a diversos agendamentos, preservando seu histórico.

### 2.4 Canais

| Canal | Finalidade |
|---|---|
| Telegram privado | Comunicação entre administradora e agente |
| Grupos Telegram | Recebimento de programações pelas equipes |
| Webhook HTTPS | Entrada das atualizações enviadas pelo Telegram à API |
| Telegram Bot API | Envio de respostas e programações |

---

## 3. Arquitetura técnica aprovada

### 3.1 Stack

| Componente | Tecnologia definida |
|---|---|
| Linguagem | C# |
| Plataforma | .NET 10 |
| Aplicação principal | ASP.NET Core Web API |
| Arquitetura | Monólito modular com separação de responsabilidades |
| Banco de dados | PostgreSQL |
| ORM | Entity Framework Core |
| Framework de agentes | Microsoft Agent Framework |
| Modelo de IA | Gemini via Google AI Studio |
| Comunicação com Telegram | Webhook HTTPS e Telegram Bot API |
| Processamento interno assíncrono | BackgroundService hospedado na API |
| Testes | Projeto de testes .NET |
| Versionamento | Git e GitHub |
| Ambiente inicial | macOS |
| Exposição HTTPS inicial | Túnel HTTPS público para a API local |

O modelo Gemini específico permanece sujeito à validação de compatibilidade, cotas e recursos disponíveis.

### 3.2 Estrutura da solução

A solução terá cinco projetos principais:

```text
Ian.slnx
│
├── Ian.Api
├── Ian.Application
├── Ian.Domain
├── Ian.Infrastructure
└── Ian.Tests
```

**Ian.Api**

Responsável por hospedar a aplicação ASP.NET Core, disponibilizar endpoints HTTP, receber webhooks, configurar dependências e iniciar serviços internos de processamento.

**Ian.Application**

Responsável pelos casos de uso, contratos de integração, coordenação dos fluxos e execução das operações autorizadas.

**Ian.Domain**

Responsável pelas entidades, regras de negócio, estados, validações de domínio e conceitos centrais do sistema.

Deverá permanecer independente das camadas de infraestrutura e apresentação.

**Ian.Infrastructure**

Responsável por persistência PostgreSQL/EF Core, comunicação com serviços externos, integrações Telegram/Gemini e implementações técnicas.

**Ian.Tests**

Responsável pelos testes unitários e de integração, incluindo validações de negócio, concorrência, webhooks e persistência.

### 3.3 Decisão sobre Worker Service

**Não haverá um projeto `Ian.Worker` separado.**

A aplicação principal será `Ian.Api`.

As atividades assíncronas e periódicas poderão utilizar implementações de `BackgroundService` registradas no host da própria API.

Essa decisão evita manter dois processos de aplicação independentes durante a fase inicial, preservando a possibilidade de separar responsabilidades futuramente, caso a escala ou as necessidades operacionais justifiquem.

### 3.4 Recebimento de mensagens pelo Telegram

O Telegram utilizará o mecanismo de webhook, substituindo definitivamente o Long Polling.

A rota prevista no backlog é:

```http
POST /webhooks/telegram
```

O fluxo será:

1. A administradora envia uma mensagem ao bot no Telegram.
2. O Telegram encaminha um `Update` por requisição HTTPS ao webhook da API.
3. A API valida o segredo do webhook e a estrutura mínima da atualização.
4. A API registra a atualização de forma durável no PostgreSQL, com identificação para evitar duplicidade.
5. Após confirmar a persistência, retorna uma resposta HTTP de sucesso ao Telegram.
6. O processamento interno recupera a atualização pendente e a encaminha ao fluxo apropriado.
7. O agente interpreta a solicitação, utiliza os casos de uso autorizados e envia uma resposta à administradora.

**Invariante:** a API não deve responder HTTP 2xx antes de confirmar o armazenamento durável da atualização.

A execução do Gemini e as operações demoradas não ocorrerão dentro da requisição do webhook.

### 3.5 Autenticidade e autorização

O endpoint do Telegram deverá validar o cabeçalho:

```http
X-Telegram-Bot-Api-Secret-Token
```

O valor deverá corresponder ao segredo configurado na aplicação.

A validação do webhook e a autorização administrativa são controles diferentes:

- A validação do segredo verifica a origem esperada da requisição.
- A validação do Telegram User ID determina se o usuário pode executar comandos administrativos.

Mensagens recebidas dos grupos das equipes não concedem permissão administrativa.

### 3.6 Processamento assíncrono e recuperação

O processamento das atualizações recebidas deverá ser recuperável.

Cada atualização terá identificação única por `update_id` e situação de processamento persistida.

Estados técnicos como `Received`, `Processing`, `Completed` e `FailedRetryable` poderão ser usados, ou equivalentes documentados.

O sistema deverá:

- Evitar processar simultaneamente a mesma atualização.
- Reconhecer atualizações duplicadas.
- Recuperar atualizações pendentes após reinicialização.
- Limitar tentativas de recuperação.
- Registrar falhas sem expor informações sensíveis.
- Evitar duplicação de operações de escrita.
- Não declarar sucesso quando houver incerteza sobre o resultado.

O processamento durável será baseado no PostgreSQL. Uma solução de mensageria externa não foi definida como dependência obrigatória do MVP.

### 3.7 Agente de IA

O Microsoft Agent Framework será utilizado para organizar a interação com o Gemini e as ferramentas da aplicação.

O modelo poderá interpretar intenções, identificar informações ausentes, formular perguntas e solicitar execução de ferramentas.

Entretanto, **o modelo de IA não terá autoridade para alterar diretamente o banco de dados nem para ignorar regras de negócio**.

Toda operação deverá passar pelos casos de uso da aplicação, responsáveis por autorização, consistência, disponibilidade e transações.

### 3.8 Execução inicial

O sistema será executado inicialmente em um computador macOS.

A API estará disponível localmente, enquanto um túnel HTTPS fornecerá um endereço público para o Telegram encaminhar os webhooks.

O túnel, a aplicação e as dependências necessárias deverão estar operacionais para o recebimento das atualizações.

Não existe garantia de disponibilidade contínua enquanto o computador estiver desligado, desconectado ou com o túnel indisponível.

A hospedagem permanente em VPS ou outro ambiente será avaliada posteriormente.

---

## 4. Catálogo de tipos de serviço

O sistema possuirá um catálogo inicial fixo.

| Tipo | Duração padrão | Modalidade padrão |
|---|---|---|
| Visita técnica | 4 horas | Externo |
| Montagem | 7 dias corridos | Externo |
| Ajuste | 7 dias corridos | Externo |
| Orçamento | 1 dia | Interno |

As durações representam valores padrão e poderão ser alteradas individualmente em cada agendamento.

A modalidade também poderá ser modificada para `Interno`, `Externo` ou `Remoto`.

**Endereço obrigatório:** somente quando a modalidade efetiva do agendamento for `Externo`.

Alterações nos padrões do catálogo não deverão modificar retroativamente os agendamentos anteriores.

---

## 5. Requisitos funcionais

| ID | Requisito |
|---|---|
| RF-001 / RF-003 | Gerenciar agendamentos individuais associados a tipos de serviço. |
| RF-002 / RF-006 / RF-007 | Utilizar catálogo predefinido com quatro tipos de serviço, duração e modalidade padrão. |
| RF-004 | Consultar agendamentos por período, equipe, cliente, tipo e status, inclusive combinando critérios. |
| RF-005 | Enviar resumo automático diário às 07h50 quando houver serviços agendados para execução no dia. |
| RF-008 | Enviar programação de equipe ao grupo Telegram mediante solicitação da administradora. |
| RF-009 | Cadastrar, consultar, renomear e desativar equipes; vincular grupos Telegram mediante confirmação. |
| RF-010 | Interpretar linguagem natural, solicitar dados faltantes e executar ferramentas autorizadas. |
| RF-011 / RF-012 | Receber mensagens de texto e áudio, respondendo somente em texto. |
| RF-013 | Manter cadastro reutilizável de clientes, associado ao histórico de serviços. |
| RF-014 | Cadastrar automaticamente novos clientes durante o primeiro agendamento. |
| RF-015 | Preservar endereço principal do cliente e endereço específico de cada agendamento. |
| RF-016 | Atualizar nome, telefone e endereço principal do cliente por Telegram. |
| RF-017 | Permitir alteração da modalidade padrão em um agendamento específico. |
| RF-018 | Editar integralmente agendamentos com status `Agendado`, respeitando regras e histórico. |

### 5.1 Critérios funcionais gerais

As operações de escrita deverão validar seus dados antes de produzir efeitos persistentes.

Agendamentos e clientes associados deverão ser criados de forma consistente, sem deixar registros parcialmente confirmados.

Quando houver ambiguidade, a administradora deverá ser consultada antes de uma alteração.

Sempre que uma operação for recusada, o agente deverá explicar a razão e, quando possível, apresentar alternativas válidas.

---

## 6. Regras de negócio

Os identificadores originais foram preservados para manter a rastreabilidade com o levantamento anterior e com as Issues.

### 6.1 Autorização, agendamentos e estados

**RN-001 — Autorização administrativa**

Somente a administradora identificada pelo Telegram User ID autorizado poderá executar comandos administrativos.

**RN-003 — Dados de agendamento**

Cada agendamento deverá registrar cliente, telefone, tipo de serviço, equipe, modalidade, período, endereço conforme modalidade, observações opcionais e status.

**RN-004 — Agendas independentes**

Cada equipe possuirá sua própria agenda.

**RN-005 — Prevenção de sobreposição**

Não poderá haver horários de ocupação sobrepostos para agendamentos ativos da mesma equipe.

Equipes distintas poderão realizar serviços simultaneamente.

**RN-006 — Duração individual**

A duração padrão poderá ser substituída em cada agendamento sem alterar os demais.

**RN-007 — Estados**

Os únicos estados de negócio dos agendamentos serão:

- `Agendado`
- `Concluído`
- `Cancelado`

Reagendamento não será um estado.

A passagem da data prevista não altera automaticamente o status.

**RN-008 — Cancelamento**

O cancelamento deverá exigir confirmação e preservar o agendamento e seu histórico.

Não haverá exclusão definitiva.

**RN-015 — Escolha da equipe**

A administradora deverá selecionar explicitamente a equipe responsável.

O agente não escolherá nem sugerirá uma equipe automaticamente.

**RN-016 — Transferência**

Agendamentos poderão ser transferidos entre equipes, mantendo sua identidade e histórico, desde que a equipe de destino esteja disponível.

**RN-024 — Confirmações**

Operações comuns e inequívocas não exigem confirmação adicional.

Operações sensíveis, como cancelamento, reabertura e bloqueio geral, exigem confirmação.

**RN-027 — Identificação inequívoca**

Quando o agendamento for identificado de forma inequívoca, a operação poderá prosseguir.

Quando houver múltiplos candidatos, a administradora deverá escolher o correto.

**RN-028 — Operações individuais**

Alterações deverão ser executadas individualmente.

Não serão permitidas mutações em lote, embora consultas possam envolver diversos registros.

**RN-033 — Reabertura**

Agendamentos `Concluídos` ou `Cancelados` poderão retornar ao estado `Agendado` após confirmação e nova verificação de disponibilidade.

**RN-034 — Histórico e auditoria**

Operações deverão produzir rastreabilidade, incluindo alterações, resultados, falhas e identificadores de correlação, com minimização de dados sensíveis.

**RN-039 — Conclusão antecipada e prorrogação**

Conclusão antecipada libera reservas futuras.

Atrasos somente produzem prorrogação após solicitação e validação, exceto extensões automáticas por folga integral aplicáveis a serviços prolongados.

**RN-040 — Atrasos**

Serviços vencidos e ainda `Agendados` deverão ser identificados apenas em consultas sob demanda.

Não haverá alertas automáticos de atraso.

**RN-042 — Desativação de equipe**

Não será permitido desativar uma equipe com serviços `Agendados` pendentes.

A administradora deverá transferir ou cancelar os compromissos antes da desativação.

### 6.2 Expediente, disponibilidade e bloqueios

**RN-009 — Expediente excepcional**

O sistema permitirá agendamentos fora do expediente mediante autorização explícita da administradora e validação dos conflitos.

Isso não autoriza ignorar bloqueios de indisponibilidade.

**RN-010 — Administração dos bloqueios**

A administradora poderá criar, consultar e remover bloqueios de dias inteiros ou intervalos.

**RN-011 — Bloqueios conflitantes**

Bloqueios não poderão ser efetivados se houver agendamentos ativos conflitantes.

O sistema deverá apresentar os serviços afetados e exigir resolução dos conflitos.

**RN-017 — Ocupação prolongada**

Serviços prolongados reservarão períodos de execução em todos os dias aplicáveis, e não somente na data final.

**RN-018 — Duração de uma semana**

Uma semana corresponde a sete dias corridos.

A ocupação de cada dia corresponde ao expediente efetivo, e não a um intervalo contínuo de 24 horas.

**RN-019 — Expediente padrão**

O expediente padrão será de **08h às 18h, todos os dias, inclusive domingos**.

**RN-020 — Exceções por equipe**

A administradora poderá estabelecer exceções de expediente por equipe e data, incluindo início posterior, término antecipado, jornada excepcional e folga integral.

**RN-021 — Folgas integrais**

Uma folga integral durante serviço prolongado deverá prorrogar sua data prevista para compensar o dia não trabalhado.

A alteração deverá validar os períodos adicionais e possíveis conflitos.

**RN-022 — Expediente parcialmente reduzido**

Uma redução parcial de jornada não produzirá extensão automática da duração do serviço.

**RN-023 — Bloqueios individuais e gerais**

Bloqueios poderão afetar uma equipe específica ou todas as equipes.

Ambos deverão respeitar as validações de disponibilidade.

**RN-031 — Endereço obrigatório**

Endereço será obrigatório na modalidade `Externo` e opcional nas modalidades `Interno` e `Remoto`.

**RN-032 — Modalidade efetiva**

A modalidade padrão será herdada do tipo de serviço, podendo ser substituída individualmente.

**RN-041 — Horário inicial dos serviços prolongados**

O horário de início informado será aplicado ao primeiro dia.

Os dias seguintes seguirão o expediente e as exceções aplicáveis.

**RN-045 — Capacidade das equipes**

Todas as equipes poderão executar todos os tipos de serviço.

Não haverá especialização obrigatória por equipe.

### 6.3 Conversa, contexto e resiliência

**RN-025 — Persistência do contexto**

Solicitações incompletas deverão permanecer recuperáveis após interrupções prolongadas ou reinicializações.

O contexto não poderá depender exclusivamente do histórico temporário do modelo de IA.

**RN-026 — Pendência única**

A administradora poderá manter uma solicitação de alteração pendente por vez.

Consultas poderão ocorrer sem cancelar a pendência.

Para substituir uma alteração pendente, o agente solicitará confirmação.

**RN-029 — Falhas transitórias**

Falhas transitórias deverão ser tratadas com tentativas limitadas.

Antes de repetir uma escrita com resultado incerto, a aplicação verificará se a alteração já foi aplicada.

**RN-030 — Ambiguidade**

Solicitações ambíguas deverão gerar esclarecimento, apresentando interpretações plausíveis antes da execução.

### 6.4 Comunicação e relatórios

**RN-012 — Condição do resumo diário**

O resumo automático somente será enviado quando houver ao menos um serviço `Agendado` cuja execução inclua o dia atual.

Serviços `Concluídos` e `Cancelados` não serão considerados.

**RN-013 — Horário do resumo**

O resumo será produzido às **07h50**, utilizando o fuso `America/Sao_Paulo`.

**RN-035 — Alterações após comunicação**

Se um agendamento comunicado anteriormente for modificado, o agente deverá sugerir o envio de atualização.

O envio somente ocorrerá mediante autorização da administradora.

Em transferências, deverá considerar os grupos de origem e destino.

**RN-036 — Conteúdo da programação**

A programação incluirá nome e telefone do cliente, tipo de serviço, período, horários, modalidade, endereço quando aplicável e observações.

O histórico completo do cliente não será compartilhado com os grupos.

**RN-037 — Período obrigatório**

A administradora deverá informar o período desejado ao solicitar envio de programação.

Caso esteja ausente, o agente perguntará antes de enviar.

**RN-038 — Programação detalhada**

A programação será organizada por dia e deverá considerar a jornada efetiva e os serviços prolongados iniciados antes do intervalo solicitado.

**RN-043 — Resumo consolidado**

O resumo das 07h50 reunirá todas as equipes e seus serviços em execução naquele dia, inclusive serviços iniciados em datas anteriores.

**RN-044 — Vínculo com grupo Telegram**

O bot deverá identificar o grupo Telegram e solicitar confirmação expressa da administradora antes de vinculá-lo a uma equipe.

**RN-046 — Falhas no envio**

Falhas de envio deverão ser tratadas com tentativas controladas.

A administradora deverá ser informada quando a API do Telegram não confirmar a aceitação da mensagem.

A aceitação pela API não será interpretada como confirmação de leitura pelos membros.

---

## 7. Requisitos técnicos da arquitetura revisada

Esta seção formaliza as decisões técnicas posteriores ao levantamento funcional original.

| ID | Requisito técnico |
|---|---|
| RT-001 | Hospedar a aplicação em ASP.NET Core Web API .NET 10. |
| RT-002 | Receber atualizações do Telegram exclusivamente por webhook HTTPS; não utilizar Long Polling. |
| RT-003 | Validar o segredo do webhook por meio do cabeçalho apropriado. |
| RT-004 | Persistir atualizações recebidas antes de confirmar HTTP 2xx. |
| RT-005 | Desacoplar o processamento da atualização da requisição HTTP. |
| RT-006 | Utilizar `BackgroundService` dentro de `Ian.Api`, sem `Ian.Worker` separado. |
| RT-007 | Persistir `update_id`, estados de processamento e tentativas para permitir deduplicação e recuperação. |
| RT-008 | Utilizar PostgreSQL e EF Core para persistência transacional. |
| RT-009 | Evitar duplicidade de efeitos de negócio em reentregas, retries e falhas. |
| RT-010 | Validar autorização e regras de negócio no backend, nunca apenas no modelo de IA. |
| RT-011 | Manter as credenciais fora do código-fonte e dos logs. |
| RT-012 | Executar inicialmente no macOS com exposição por túnel HTTPS. |
| RT-013 | Executar o resumo diário em serviço interno, com controle persistente contra duplicidade. |
| RT-014 | Garantir validações de concorrência para impedir reservas simultâneas conflitantes. |
| RT-015 | Disponibilizar testes automatizados para regras críticas, persistência, webhook e idempotência. |

### 7.1 Semântica de confirmação

O sucesso HTTP retornado ao Telegram significa que a atualização foi recebida e armazenada de maneira durável.

Não significa necessariamente que:

- O agente já interpretou a solicitação.
- A operação administrativa foi executada.
- Uma mensagem de resposta foi entregue.
- O destinatário leu a mensagem enviada.

Esses acontecimentos representam etapas distintas e deverão possuir rastreabilidade própria.

### 7.2 Idempotência

O mesmo `update_id` não poderá produzir múltiplas alterações de negócio equivalentes.

As operações de escrita deverão possuir mecanismos de proteção apropriados contra reexecução.

Não será presumida garantia absoluta de execução exatamente uma vez em integrações externas.

### 7.3 Jobs periódicos

O resumo das 07h50 deverá ser executado por serviço hospedado na aplicação.

Deverá existir controle persistente da execução e dos envios para reduzir a possibilidade de duplicidade após reinicializações.

A política exata para situações em que a aplicação estiver indisponível no horário programado permanece pendente de definição.

---

## 8. Modelo conceitual de dados

O modelo físico, índices e relacionamentos detalhados serão definidos nas tarefas de persistência.

| Entidade | Responsabilidade principal |
|---|---|
| `Cliente` | Cadastro, telefone, endereço principal e histórico |
| `TipoServico` | Catálogo fixo com duração e modalidade padrão |
| `Equipe` | Identificação, situação e vínculo com grupo Telegram |
| `Agendamento` | Serviço, cliente, equipe, modalidade, período, endereço, status |
| `PeriodoOcupacao` | Intervalos efetivamente reservados na agenda da equipe |
| `ExcecaoExpediente` | Jornadas excepcionais e folgas por equipe/data |
| `BloqueioAgenda` | Indisponibilidades individuais e gerais |
| `SolicitacaoPendente` | Estado persistente da solicitação em andamento |
| `HistoricoAlteracao` | Auditoria de mutações e eventos relevantes |
| `RegistroEnvio` | Mensagens de saída, destino, tentativas e resultados |
| `TelegramUpdateInbox` | Atualizações recebidas por webhook e controle de processamento |
| `RegistroExecucaoJob` | Controle de execução e idempotência de tarefas periódicas |

### 8.1 Integridade dos clientes

O telefone padronizado será o identificador operacional principal para a localização de clientes.

O cadastro automático deverá reutilizar o cliente existente quando houver identificação inequívoca.

A criação de cliente e seu primeiro agendamento deverá ocorrer de modo transacional.

O endereço específico utilizado em um serviço deverá ser preservado, mesmo após alterações posteriores no endereço principal do cliente.

### 8.2 Integridade dos agendamentos

Os períodos ocupados deverão corresponder à duração efetiva, ao expediente e às exceções aplicáveis.

As operações de criação, edição, transferência, reabertura e prorrogação deverão validar os períodos ocupados antes de confirmar a alteração.

Consultas de disponibilidade e operações de escrita concorrentes deverão respeitar as mesmas invariantes.

---

## 9. Requisitos não funcionais

### RNF-001 — Segurança, persistência e operação contínua

A aplicação deverá:

- Ser preparada para operação contínua, inclusive fora do horário comercial.
- Restringir operações administrativas ao Telegram User ID autorizado.
- Validar a autenticidade das requisições do webhook.
- Proteger tokens, chaves e credenciais.
- Evitar exposição de dados pessoais nos logs.
- Executar backups diários do banco de dados.
- Permitir restauração verificável de backups.
- Registrar falhas, tentativas e resultados com rastreabilidade.
- Aplicar medidas adequadas de proteção de dados pessoais.
- Evitar registros de sucesso quando uma transação não foi confirmada.
- Preservar operações pendentes recuperáveis após interrupções.

### 9.1 Disponibilidade inicial

A operação local no macOS não representa uma garantia de disponibilidade 24 horas por dia.

O recebimento de webhooks depende da disponibilidade da máquina, da API, do banco e do acesso HTTPS público.

Uma solução de hospedagem permanente poderá ser avaliada em etapa posterior.

### 9.2 Observabilidade

Os logs deverão identificar operações e falhas por meio de identificadores de correlação, sem registrar indiscriminadamente mensagens, telefones, endereços ou credenciais.

### 9.3 Consistência

Alterações críticas deverão ocorrer com garantias transacionais adequadas.

Falhas durante o processamento não poderão resultar em estados parciais considerados concluídos.

### 9.4 Qualidade

As regras críticas deverão possuir testes automatizados, especialmente:

- Sobreposição e concorrência de agendamentos.
- Cálculo de serviços prolongados.
- Folgas e prorrogações.
- Estados e reabertura.
- Recebimento e autenticação do webhook.
- Reentrega e idempotência.
- Persistência e recuperação.
- Envio de mensagens e tratamento de falhas.

---

## 10. Fluxos e critérios de aceite

### CA-01 — Agendamento de novo cliente

**Dado** um pedido contendo os dados necessários e uma equipe disponível, **quando** o cliente ainda não existir, **então** cliente e agendamento serão cadastrados de forma consistente.

Uma falha na transação não poderá produzir cadastro parcial confirmado.

### CA-02 — Conflito entre agendamentos

**Dado** que uma equipe possui horário ocupado, **quando** for solicitado serviço conflitante para a mesma equipe, **então** a operação será recusada e serão apresentadas alternativas.

Outra equipe poderá possuir compromisso simultâneo.

### CA-03 — Serviço de sete dias

**Dado** uma montagem iniciada na segunda-feira às 10h e com duração de sete dias, **quando** forem calculados os períodos, **então** o primeiro dia ocupará 10h–18h e os dias seguintes seguirão o expediente aplicável.

### CA-04 — Folga integral durante montagem

**Dado** uma folga integral em dia de serviço prolongado, **quando** a alteração for solicitada, **então** o sistema calculará a extensão, verificará conflitos e somente confirmará uma alteração consistente.

### CA-05 — Reabertura

**Dado** um agendamento concluído ou cancelado, **quando** a administradora solicitar reabertura, **então** haverá confirmação explícita e verificação de disponibilidade.

### CA-06 — Envio de programação

**Dado** um grupo Telegram confirmado e um período informado, **quando** a administradora solicitar o envio, **então** a programação detalhada apenas daquela equipe será encaminhada e registrada.

### CA-07 — Atualização após alteração

**Dado** um compromisso previamente comunicado, **quando** ocorrer alteração relevante, **então** o agente sugerirá o envio de atualização, sem realizar reenvio automático.

### CA-08 — Resumo das 07h50

**Dado** que existem serviços agendados em execução naquele dia, **quando** chegar o horário previsto, **então** será enviado um resumo consolidado de todas as equipes.

Se não houver atividades agendadas no dia, nenhuma mensagem será enviada.

### CA-09 — Áudio e contexto persistente

**Dado** um pedido iniciado por áudio e interrompido antes da coleta de todos os campos, **quando** a administradora retomar a conversa, **então** o agente recuperará a solicitação e pedirá apenas as informações restantes.

### CA-10 — Ambiguidade e falhas

**Dado** um pedido ambíguo ou falha de ferramenta, **quando** a solicitação for processada, **então** o sistema solicitará esclarecimento ou executará recuperação segura.

Não poderá inventar resultados nem duplicar uma alteração.

### CA-11 — Recebimento do webhook

**Dada** uma requisição com segredo correto e atualização válida, **quando** a API receber o evento, **então** deverá persistir a atualização antes de responder HTTP 2xx.

Requisições com segredo ausente ou inválido deverão ser rejeitadas sem processamento.

Caso a persistência falhe, a API não poderá confirmar recebimento durável.

### CA-12 — Recuperação após reinicialização

**Dada** uma atualização persistida que ainda não foi processada, **quando** a aplicação reiniciar, **então** o processamento poderá ser retomado.

A reentrega da mesma atualização não poderá criar agendamentos duplicados.

### CA-13 — Processamento desacoplado

**Dado** um webhook válido, **quando** a API confirmar o recebimento, **então** não precisará aguardar uma resposta do Gemini ou a conclusão de uma operação administrativa.

O trabalho será encaminhado ao processamento interno.

### CA-14 — Resumo sem duplicidade

**Dado** um resumo diário já processado ou enviado, **quando** o serviço periódico reiniciar ou tentar executar novamente a mesma ocorrência, **então** deverá consultar seu controle persistente e evitar envios duplicados indevidos.

### CA-15 — Operação local com webhook

**Dada** a API em execução no macOS e um túnel HTTPS válido, **quando** o Telegram enviar uma atualização, **então** a API deverá recebê-la por seu endpoint configurado.

Quando o túnel ou a máquina ficar indisponível, o sistema não deverá declarar disponibilidade contínua.

---

## 11. Pendências técnicas e pontos de refinamento

As decisões abaixo não devem ser confundidas com funcionalidades adicionais automaticamente aprovadas.

### PT-01 — Modelo Gemini

Definir o modelo específico e validar capacidade de uso de ferramentas, processamento de áudio, limites e disponibilidade do nível gratuito do Google AI Studio.

A #19 prepara `gemini-3.8-flash` como modelo textual configurável,
com SDK oficial e MAF. Compilação e Function Calling foram verificados
localmente com respostas sintéticas, sem validar disponibilidade ou
quota da conta real. O smoke real e a decisão de áudio continuam
pendentes. Evidências e versões: `docs/spikes/maf-gemini.md`.

### PT-02 — Estratégia de áudio

Definir limites de tamanho e duração, abordagem de transcrição/interpretação e tratamento de falhas.

Na preparação da issue #17, foram definidos limites iniciais configuráveis
de 5 MiB, 120 segundos e timeout de 60 segundos, com download em memória
e descarte dos buffers. A ativação e a escolha final do provedor continuam
pendentes do spike real e da integração MAF/Gemini da #19. A duração e o
formato efetivos deverão ser validados pelo adaptador, além dos metadados
Telegram. Evidências e pendências: `docs/spikes/voz.md`.

### PT-03 — Hospedagem permanente

Avaliar futuramente a necessidade de VPS ou outra infraestrutura para operação contínua, considerando custo, segurança e disponibilidade.

### PT-04 — Backups

Definir destino, retenção, criptografia, automação e testes de restauração.

### PT-05 — Concorrência

Detalhar a implementação física das garantias transacionais e de exclusão de conflitos no PostgreSQL.

### PT-06 — Retenção e privacidade

Definir políticas de retenção, descarte, mascaramento e proteção de informações pessoais.

### PT-07 — Tratamento de atrasos operacionais do job

Definir comportamento quando a API estiver indisponível às 07h50 e retornar posteriormente.

### PT-08 — Disponibilidade do túnel

Escolher a ferramenta de túnel HTTPS e estabelecer procedimentos de registro, atualização e diagnóstico do webhook.

### PS-01 — Semântica dos intervalos

Formalizar precisamente os limites dos intervalos, a duração de um dia e o comportamento de jornadas excepcionais.

### PS-02 — Folgas e bloqueios

Definir o recálculo transacional de serviços prolongados afetados por folgas integrais e bloqueios.

### PS-03 — Exceções retroativas

Definir regras para alterações de expediente e registro de conclusão referentes a datas anteriores.

### PS-04 — Jornada parcialmente reduzida

Detalhar os períodos ocupados em jornadas reduzidas sem produzir prorrogação automática.

### PS-05 — Identificação de clientes

Definir tratamento para telefones compartilhados, mudança de número e clientes com múltiplos contatos.

### PS-06 — Versões das programações

Definir critérios para considerar uma programação desatualizada, notificação de transferências e prevenção de reenvio duplicado.

### PS-07 — Privacidade nos grupos

Validar o vínculo do grupo antes do envio de dados pessoais e distinguir confirmação da API Telegram de leitura humana.

---

## 12. Planejamento e desenvolvimento

### 12.1 Organização do trabalho

O backlog do Ian está organizado em oito épicos e 55 tarefas no GitHub Issues.

A execução seguirá a ordem de dependências técnicas e funcionais, não necessariamente a conclusão integral de um épico antes do seguinte.

Prioridades P0 e P1 identificam entregas críticas e principais, enquanto P2 e P3 representam trabalhos complementares ou posteriores, de acordo com os critérios registrados nas respectivas Issues.

### 12.2 Fluxo Git

- `main`: branch estável.
- `dev`: integração das tarefas.
- `feature/*`: desenvolvimento individual de funcionalidades.
- Pull Requests de `feature/*` para `dev`.
- Validação de critérios de aceite antes do merge.
- Uso de **Squash and merge** para consolidar os commits de cada entrega.
- Integração de `dev` em `main` após homologação da versão.

### 12.3 Prazo

A meta original de entrega foi estabelecida para 15/11/2026.

A capacidade de referência é de um desenvolvedor, com dedicação habitual de três a quatro horas diárias.

A meta não substitui as estimativas, dependências ou verificações de aceite de cada tarefa. A conclusão de todo o escopo depende do progresso e da validação das entregas.

---

## 13. Estado das decisões arquiteturais

| Decisão | Situação |
|---|---|
| ASP.NET Core Web API .NET 10 | Definida |
| Substituição do Worker separado | Definida |
| Webhook HTTPS em vez de Long Polling | Definida |
| PostgreSQL e Entity Framework Core | Definida |
| Microsoft Agent Framework | Definida |
| Gemini via Google AI Studio | Definida; modelo específico pendente |
| BackgroundService hospedado na API | Definida |
| Persistência durável das atualizações | Definida |
| Idempotência e recuperação após falhas | Requisito técnico definido |
| macOS como ambiente inicial | Definida |
| Túnel HTTPS para desenvolvimento/operação inicial | Definida; ferramenta pendente |
| Hospedagem permanente | Pendente |
| Estratégia detalhada de backup e restauração | Pendente |
| Modelo físico completo e políticas de retenção | Pendente |

---

## 14. Controle de versões

| Versão | Data | Alteração |
|---|---|---|
| 1.0 | 05/11/2026 | Consolidação original dos requisitos funcionais, regras de negócio e critérios de aceite. |
| 1.0 — Revisão técnica | 09/11/2026 | Atualização da arquitetura para ASP.NET Core Web API, Telegram Webhook HTTPS, PostgreSQL/EF Core, MAF/Gemini e BackgroundService interno; inclusão dos requisitos técnicos de persistência, idempotência, segurança e recuperação. |

### Encerramento

A revisão técnica do DRS 1.0 preserva o escopo funcional previamente aprovado.

A arquitetura passa a utilizar uma única aplicação ASP.NET Core Web API, com recebimento de atualizações por webhook HTTPS e processamento interno desacoplado.

As decisões detalhadas ainda pendentes deverão ser formalizadas nas Issues e na documentação técnica, sem alterar silenciosamente as regras de negócio aprovadas.
