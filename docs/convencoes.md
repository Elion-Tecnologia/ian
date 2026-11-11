
# IA.N — Convenções de Desenvolvimento

**Projeto:** IA.N  
**Linguagem:** C#  
**Plataforma:** .NET 10  
**Aplicação:** ASP.NET Core Web API

## 1. Objetivo

Este documento estabelece os padrões que devem orientar
a implementação e manutenção do IA.N.

Seu objetivo é manter o código consistente, legível,
testável, seguro e compatível com a arquitetura aprovada.

Estas convenções complementam:

- `AGENTS.md`
- `docs/arquitetura.md`
- `docs/requisitos/DRS.md`

Os padrões existentes no repositório devem ser preservados,
salvo quando houver uma razão técnica para modificá-los.

As orientações para código novo não devem provocar
refatorações fora do escopo da issue.

## 2. Princípios de desenvolvimento

### 2.1 Simplicidade

Implementar a solução mais simples que satisfaça os
requisitos e preserve as invariantes do sistema.

Evitar abstrações desnecessárias, generalizações
prematuras e complexidade sem benefício demonstrável.

### 2.2 Responsabilidade

Classes e métodos devem possuir responsabilidades claras.

Separar lógica de negócio, coordenação de operações,
persistência e comunicação externa.

Não criar novas camadas para responsabilidades que
já possuem um lugar apropriado na arquitetura.

### 2.3 Consistência

Antes de escrever código:

1. Localizar implementações semelhantes.
2. Identificar os padrões já utilizados.
3. Reutilizar componentes existentes quando apropriado.
4. Evitar implementações duplicadas.
5. Limitar as modificações ao escopo da issue.

### 2.4 Testabilidade

Projetar comportamentos de modo que possam ser
validados com testes automatizados.

Priorizar testes de comportamento e regras de negócio,
não detalhes internos de implementação.

## 3. Convenções de C#

### 3.1 Nomenclatura

Utilizar as convenções usuais do C#:

| Elemento | Convenção |
|---|---|
| Classes | PascalCase |
| Interfaces | I + PascalCase |
| Métodos | PascalCase |
| Propriedades | PascalCase |
| Variáveis locais | camelCase |
| Parâmetros | camelCase |
| Campos privados | _camelCase |
| Constantes | PascalCase |
| Namespaces | PascalCase |
| Métodos assíncronos | Sufixo Async |

Exemplos existentes:

- `TelegramUpdateDispatcher`
- `ITelegramUpdateInboxWriter`
- `TelegramInboxConsumer`
- `ProcessNextAsync`
- `IanDbContext`

Preservar os nomes de domínio em português, conforme
o modelo aprovado.

Exemplos:

- `Cliente`
- `Agendamento`
- `PeriodoOcupacao`

Nomes técnicos existentes em inglês devem permanecer
consistentes com os componentes aos quais pertencem.

Não renomear classes ou propriedades apenas para
uniformizar o idioma.

### 3.2 Organização dos arquivos

Preferir uma classe principal por arquivo.

O nome do arquivo deve corresponder ao principal
tipo declarado, quando possível.

Utilizar namespaces compatíveis com as pastas
e projetos existentes.

Preferir file-scoped namespaces, conforme o padrão
predominante do repositório.

### 3.3 Recursos da linguagem

Utilizar recursos de C# compatíveis com o SDK e
a versão configurada nos projetos.

Manter nullable reference types habilitados.

Utilizar tipos apropriados para representar
ausência de valores.

Não utilizar o operador de supressão de nulidade
(`!`) para ocultar problemas reais.

Preferir propriedades `required` quando seus valores
forem indispensáveis na criação de objetos.

Não adicionar bibliotecas para substituir recursos
simples já disponíveis na plataforma .NET.

### 3.4 Métodos

Métodos devem possuir nomes que expressem sua finalidade.

Evitar métodos excessivamente longos ou com múltiplas
responsabilidades independentes.

Utilizar guard clauses quando simplificarem
o fluxo de validação.

Não extrair métodos apenas para reduzir artificialmente
o número de linhas.

Comentários devem explicar decisões importantes,
restrições ou comportamentos pouco evidentes.

Evitar comentários que apenas repitam o código.

## 4. Programação assíncrona

Utilizar `async/await` para operações de I/O assíncronas.

Métodos que retornam `Task` ou `Task<T>` devem utilizar
o sufixo `Async`, salvo contratos existentes que
estabeleçam outra convenção.

Propagar `CancellationToken` pelas operações assíncronas
quando apropriado.

Não bloquear operações assíncronas utilizando
`.Result` ou `.Wait()`.

Não utilizar `async void` em operações comuns
da aplicação.

Durante encerramento do host, respeitar o
cancelamento e preservar a recuperabilidade
de operações pendentes.

## 5. Camadas e dependências

### 5.1 Domain

Colocar nesta camada:

- Entidades de negócio.
- Estados e conceitos do domínio.
- Invariantes.
- Comportamentos de negócio independentes
  de infraestrutura.

Não adicionar dependências diretas de EF Core,
HTTP, Telegram ou Gemini às entidades.

### 5.2 Application

Colocar nesta camada:

- Casos de uso.
- Contratos de serviços necessários.
- Coordenação das operações.
- Fluxos de autorização.
- Orquestração de comportamentos.

As operações devem respeitar as regras do domínio.

Interfaces devem representar dependências ou
contratos necessários, não ser criadas
automaticamente para toda classe.

### 5.3 Infrastructure

Colocar nesta camada:

- EF Core e PostgreSQL.
- Implementações de contratos.
- Integrações externas.
- Persistência.
- Serviços técnicos de processamento.

As classes de infraestrutura não devem substituir
a camada Application como local padrão para
novas regras de negócio.

### 5.4 Api

Colocar nesta camada:

- Endpoints HTTP.
- Validação de aspectos próprios do transporte.
- Composição de dependências.
- Configuração do host.
- Respostas HTTP.

Endpoints devem permanecer enxutos.

Não implementar diretamente nos endpoints
as regras de disponibilidade de agendas,
cálculo de serviços ou operações administrativas.

## 6. Injeção de dependências

Utilizar o mecanismo nativo de Dependency Injection
do ASP.NET Core.

Registrar serviços com ciclos de vida apropriados:

- Transient: instâncias independentes quando necessário.
- Scoped: serviços associados a um escopo de execução.
- Singleton: serviços que podem ser compartilhados
  com segurança durante toda a vida da aplicação.

Não registrar componentes com estado mutável
não seguro para concorrência como Singleton.

O `IanDbContext` deve respeitar seu ciclo de vida Scoped.

BackgroundServices devem criar escopos quando
precisarem resolver serviços Scoped.

Antes de criar uma nova configuração de dependências,
consultar `Ian.Infrastructure/DependencyInjection.cs`.

## 7. Persistência e Entity Framework Core

### 7.1 DbContext

O contexto principal é `IanDbContext`.

Novas entidades persistentes devem ser mapeadas
conforme os padrões existentes de configuração.

Evitar colocar configurações extensas diretamente
no DbContext.

### 7.2 Mapeamentos

Utilizar `IEntityTypeConfiguration<T>` para configurar
entidades quando necessário.

Definir explicitamente os elementos relevantes:

- Chaves primárias.
- Chaves estrangeiras.
- Restrições de nulidade.
- Índices.
- Restrições de unicidade.
- Regras de exclusão.
- Check constraints apropriadas.

Não presumir que toda regra de negócio pode
ser garantida somente por validações em C#.

### 7.3 Migrations

Alterações no modelo persistente devem incluir
as migrations correspondentes quando necessárias.

Não editar manualmente migrations já aplicadas
em ambientes compartilhados para reescrever
o histórico.

Utilizar migrations incrementais.

Revisar o SQL e os efeitos de alterações
que modifiquem dados existentes.

Operações destrutivas exigem avaliação explícita.

### 7.4 Consultas

Utilizar consultas compatíveis com os requisitos
de desempenho e consistência.

Para consultas somente de leitura, considerar
`AsNoTracking()` quando apropriado.

Evitar consultas repetitivas desnecessárias.

Não materializar grandes conjuntos de dados
sem necessidade.

Não adicionar Repository ou Unit of Work genéricos
apenas por convenção.

O EF Core já oferece mecanismos próprios de
rastreamento e unidade de trabalho.

Abstrações adicionais exigem benefício concreto.

### 7.5 Transações

Operações relacionadas que precisam ser atômicas
devem compartilhar uma transação adequada.

Nunca considerar uma alteração concluída antes
da confirmação da transação.

Considerar concorrência, reentregas e possíveis
falhas durante o commit.

## 8. Datas e horários

O IA.N manipula horários de agendamento,
expedientes e execuções periódicas.

Os instantes persistidos devem seguir a
modelagem UTC existente.

Utilizar `DateTimeOffset` quando apropriado
para representar instantes.

Datas civis e horários locais de expediente
devem manter sua semântica própria.

O fuso operacional aprovado é:

`America/Sao_Paulo`

Não presumir que o relógio local do servidor
representa o horário de negócio.

Conversões devem ocorrer de maneira explícita.

Cálculos de serviços prolongados devem respeitar
os dias locais de execução e o expediente fixo do MVP (08h–18h, todos os dias).

DiasCorridos reserva janelas completas 08h–18h em cada data, inclusive
a primeira. Horário diferente exige esclarecimento; não assumir início
parcial. A unidade Minutos mantém sua semântica de consumo de expediente.

Não converter períodos de vários dias em uma
reserva contínua de 24 horas por dia.

## 9. Validação e regras de negócio

Validar dados antes de produzir efeitos persistentes.

A validação deve ocorrer na camada adequada.

Exemplos:

- Formato do payload: transporte ou entrada.
- Autorização administrativa: backend.
- Disponibilidade da agenda única: regra de negócio.
- Unicidade de telefone: domínio e banco.
- Integridade referencial: banco de dados.

Não depender exclusivamente do agente de IA
para validar operações.

Preservar as regras aprovadas no DRS.

Quando uma solicitação contrariar uma regra,
retornar ou produzir um resultado que permita
explicar a recusa ao usuário.

Não modificar uma invariante para fazer
um teste passar sem revisar o requisito.

## 10. Tratamento de erros

Não ignorar exceções silenciosamente.

Tratar erros na camada que possui responsabilidade
para interpretá-los ou recuperar a operação.

Diferenciar:

- Erros de validação.
- Violações de regras de negócio.
- Falhas de integração.
- Falhas transitórias.
- Cancelamento de operação.
- Falhas inesperadas.

Não utilizar exceções como fluxo normal
quando um resultado explícito for mais adequado.

Em integrações sujeitas a falhas transitórias,
aplicar políticas de tentativas controladas.

Antes de repetir uma operação de escrita com
resultado incerto, verificar a idempotência.

Não retornar sucesso fictício.

Não registrar dados sensíveis em mensagens de erro.

## 11. Segurança

Nunca inserir no código:

- Tokens reais de bots.
- Chaves de API.
- Senhas.
- Strings de conexão com credenciais reais.
- Segredos de webhook.

Utilizar User Secrets em Development ou mecanismos
seguros de configuração do ambiente.

Não registrar indiscriminadamente:

- Mensagens privadas.
- Telefones.
- Endereços.
- Payloads completos contendo dados pessoais.
- Credenciais.

Preservar os controles de autorização administrativa.

Não confiar exclusivamente em dados fornecidos
pelo Telegram ou pelo modelo de IA.

Evitar exposição de detalhes internos nas
respostas HTTP.

## 12. Logs e observabilidade

Utilizar `ILogger<T>` e logging estruturado.

Mensagens de log devem ser objetivas e adequadas
ao diagnóstico.

Preferir parâmetros estruturados em vez
de interpolação de strings.

Utilizar identificadores de correlação
quando disponíveis.

Distinguir recebimento, processamento e
conclusão de operações.

Não registrar uma etapa intermediária
como conclusão de todo o fluxo.

## 13. Padrões para testes

### 13.1 Framework

Utilizar xUnit, conforme o projeto existente.

Preferir `[Fact]` para cenários individuais.

Utilizar `[Theory]` e `[InlineData]`
quando múltiplas entradas compartilham
o mesmo comportamento esperado.

Organizar testes por componente ou comportamento.

### 13.2 Nomenclatura

Manter nomes descritivos que indiquem
o comportamento verificado.

Exemplos de nomes existentes:

- `DuplicateUpdateIsStoredOnlyOnce`
- `ExpiredLeaseAllowsRecoveryAfterCrash`
- `DeniedMessagesNeverReachHandlers`

Para código novo, preferir nomes que expressem
cenário e resultado esperado.

Evitar nomes genéricos como `Test1`.

Não renomear testes existentes apenas para
padronizar nomenclatura.

### 13.3 Testes unitários

Testar comportamentos isolados quando possível.

Utilizar fakes, stubs ou spies quando eles
facilitarem a verificação.

Não introduzir frameworks adicionais de mocking
sem necessidade.

Evitar dependência de serviços externos reais
em testes unitários.

### 13.4 Testes de integração

Utilizar testes de integração quando o comportamento
depender de recursos reais do banco ou do HTTP.

Os testes de PostgreSQL existentes utilizam a
configuração de ambiente:

`IAN_TEST_POSTGRES_CONNECTION_STRING`

Esses testes devem operar somente sobre bancos
de teste apropriados e isolados.

Nunca executar testes destrutivos contra bancos
de produção ou ambientes compartilhados.

Não considerar um teste de integração validado
quando ele tiver sido ignorado por ausência
de infraestrutura.

### 13.5 Cenários críticos

Priorizar cobertura para:

- Autorização Telegram.
- Autenticidade do webhook.
- Persistência antes da resposta HTTP 2xx.
- Deduplicação de updates.
- Idempotência de escritas.
- Recuperação após interrupções.
- Concorrência entre agendamentos.
- Cálculo de períodos de ocupação.
- Janelas completas nos serviços em dias corridos e esclarecimento de horário divergente.
- Preservação de dados legados na migração, inclusive bloqueios e exceções retirados do modelo ativo.
- Transições de estado.
- Confirmações obrigatórias.

Os testes devem derivar dos critérios de aceite
da issue e dos requisitos aplicáveis.

### 13.6 Qualidade dos testes

Cada teste deve possuir um resultado esperado claro.

Evitar testes que apenas comprovem que um
método foi chamado quando o resultado funcional
puder ser observado diretamente.

Testes devem ser independentes sempre que possível.

Não alterar testes para ocultar regressões.

## 14. Integrações externas

### 14.1 Telegram

Preservar o fluxo webhook e inbox durável.

Não introduzir Long Polling.

Não executar operações demoradas antes da
confirmação do armazenamento da atualização.

Preservar os mecanismos de autenticação,
deduplicação, recuperação e autorização.

### 14.2 Microsoft Agent Framework e Gemini

O modelo de IA deve atuar como mecanismo
de interpretação e orquestração.

Não permitir mutações diretas no banco por tools.

As tools devem utilizar operações controladas
pela aplicação.

Respeitar confirmações, autorizações e
validações do backend.

Não inventar resultados quando uma ferramenta falhar.

### 14.3 Políticas de resiliência

Não adicionar retries indiscriminadamente.

Cada política deve considerar:

- Possibilidade de duplicidade.
- Idempotência.
- Limite de tentativas.
- Tempo de espera.
- Falhas permanentes.
- Recuperação após reinicialização.

## 15. Dependências externas

Antes de adicionar um pacote NuGet:

1. Verificar se o .NET já oferece a funcionalidade.
2. Verificar se existe pacote equivalente no projeto.
3. Avaliar manutenção e compatibilidade.
4. Justificar sua necessidade.
5. Evitar dependências redundantes.

Não introduzir MediatR, AutoMapper, bibliotecas
de validação ou novos frameworks arquiteturais
automaticamente.

Esses componentes podem ser avaliados quando
resolverem necessidades concretas.

## 16. Alterações de código

Antes da implementação:

1. Ler a issue.
2. Identificar as regras relevantes no DRS.
3. Consultar a arquitetura.
4. Inspecionar o código relacionado.
5. Identificar dependências e testes afetados.

Durante a implementação:

1. Preservar contratos existentes quando possível.
2. Manter as alterações dentro do escopo.
3. Evitar refatorações não relacionadas.
4. Adicionar testes pertinentes.
5. Atualizar a documentação afetada.

Após a implementação:

1. Restaurar dependências quando necessário.
2. Compilar a solução.
3. Executar os testes relevantes.
4. Revisar o diff.
5. Informar pendências ou limitações.

Não executar commits, push ou merge sem
solicitação explícita do usuário.

## 17. Comandos de validação

Executar a partir da raiz do repositório:

```powershell
dotnet restore Ian.slnx

dotnet build Ian.slnx --configuration Release --no-restore

dotnet test Ian.slnx --configuration Release --no-build --no-restore
```

Testes que dependam de infraestrutura adicional
devem ter seus pré-requisitos identificados.

Não declarar testes como aprovados sem
confirmar sua execução.

## 18. Git e Pull Requests

Utilizar as branches:

- `main`: código estável.
- `dev`: integração.
- `feature/*`: desenvolvimento.

Cada issue deve produzir mudanças relacionadas
ao seu próprio escopo.

Evitar incluir modificações não relacionadas
no mesmo commit ou Pull Request.

Pull Requests de funcionalidades devem ter
`dev` como destino.

A integração exige validação e autorização explícita do usuário para merge,
inclusive Squash and merge.

## 19. Evolução das convenções

Estas convenções podem evoluir conforme
as necessidades do projeto.

Uma nova convenção deve resolver um problema
real de consistência, manutenção ou qualidade.

Não impor uma regra nova retroativamente
sobre todo o código sem justificativa.

Quando houver conflito entre uma convenção,
a implementação e um requisito aprovado,
investigar antes de decidir.

As regras de negócio aprovadas no DRS não
podem ser alteradas silenciosamente por uma
preferência de estilo ou arquitetura.
