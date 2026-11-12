# Homologação local do MVP textual (#59)

A homologação automatizada usa o webhook ASP.NET Core, consumidor da inbox,
MAF, Tools e casos de uso reais, com PostgreSQL descartável. Telegram e Gemini
são simulados, inclusive o transporte HTTP do SDK Google; não consome quota.
O relógio e as falhas são controlados pelos testes. O modelo nunca acessa SQL.

## 1. Banco e testes automatizados

Pré-requisitos: .NET SDK 10, Docker com PostgreSQL 17 e dependências restauradas.
Na raiz do repositório, com a API parada:

```bash
docker run --name ian59-postgres --rm -d \
  -e POSTGRES_DB=ian_t59_tests -e POSTGRES_USER=ian_test \
  -e POSTGRES_PASSWORD=ian_test_local \
  -p 127.0.0.1:55459:5432 postgres:17-alpine
docker exec ian59-postgres pg_isready -U ian_test -d ian_t59_tests
# Prossiga quando pg_isready indicar accepting connections.
export IAN_TEST_POSTGRES_DATABASE=ian_t59_tests
export IAN_TEST_POSTGRES_CONNECTION_STRING='Host=127.0.0.1;Port=55459;Database=ian_t59_tests;Username=ian_test;Password=ian_test_local'
export IAN_RUN_GEMINI_SMOKE=0 IAN_ALLOW_GEMINI_QUOTA=0
dotnet restore Ian.slnx
dotnet test Ian.Tests/Ian.Tests.csproj --configuration Release --no-restore \
  --filter 'Category=HomologacaoMvp'
dotnet test Ian.slnx --configuration Release --no-restore
dotnet build Ian.slnx --configuration Release --no-restore
```

A senha acima serve somente para esse contêiner descartável em loopback.
Os testes apagam/recriam schemas e aplicam migrations exclusivamente no banco
confirmado pelas duas variáveis; nunca use a origem ou `ian_t03_tmp`.
Sem conexão, testes PostgreSQL são ignorados: isso não comprova homologação.
Os dois smokes reais Gemini devem permanecer ignorados. A categoria
`HomologacaoMvp` também participa do check **Build and test**, na etapa PostgreSQL.

## 2. API, túnel HTTPS e webhook (execução manual opcional)

Esta etapa contata Telegram/Gemini reais e consome quota; execute somente após
autorização explícita. Não é requisito para repetir a homologação simulada.
Requer Python 3, `cloudflared`, bot de teste e conta administradora autorizada.
Não use dados de clientes reais. Para iniciar vazio, pare o contêiner acima
(`docker stop ian59-postgres`) e repita sua criação e a verificação de prontidão.

Configure os segredos de Development por entrada oculta, sem argumentos ou
histórico contendo credenciais:

```bash
python3 - <<'PY'
import getpass, json, subprocess
settings = {
    'Telegram:BotToken': getpass.getpass('Token do bot de teste: '),
    'Telegram:WebhookSecret': getpass.getpass('Segredo do webhook (ASCII letras/dígitos/_/-): '),
    'Telegram:AdministratorUserId': input('ID numérico da administradora: '),
    'Gemini:ApiKey': getpass.getpass('Chave Gemini: '),
}
subprocess.run(['dotnet', 'user-secrets', 'set', '--project', 'Ian.Api'],
               input=json.dumps(settings), text=True, check=True)
PY
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Default="$IAN_TEST_POSTGRES_CONNECTION_STRING"
dotnet tool restore
# Exclusivamente no contêiner descartável confirmado acima:
dotnet ef database update --project Ian.Infrastructure --startup-project Ian.Api
dotnet dev-certs https --trust
dotnet run --project Ian.Api --launch-profile https
```

Em outro terminal, valide `https://localhost:7160/app/v1/health` e abra o túnel:

```bash
cloudflared tunnel --url https://localhost:7160 --no-tls-verify
```

`--no-tls-verify` atende apenas ao certificado de desenvolvimento no destino
local. Copie a URL pública HTTPS mostrada pelo túnel e registre o webhook:

```bash
python3 - <<'PY'
import getpass, json, urllib.request
token = getpass.getpass('Token do mesmo bot de teste: ')
secret = getpass.getpass('Mesmo segredo do webhook: ')
base = input('URL pública HTTPS do túnel: ').rstrip('/')
if not base.startswith('https://'):
    raise SystemExit('É necessária uma URL HTTPS.')
payload = json.dumps({'url': base + '/webhooks/telegram', 'secret_token': secret,
                      'allowed_updates': ['message']}).encode()
request = urllib.request.Request('https://api.telegram.org/bot' + token + '/setWebhook',
    data=payload, headers={'Content-Type': 'application/json'})
try:
    with urllib.request.urlopen(request, timeout=20) as response:
        accepted = json.load(response).get('ok') is True
except Exception:
    raise SystemExit('Não foi possível registrar o webhook.') from None
print('Webhook registrado.' if accepted else 'Webhook recusado.')
PY
```

## 3. Checklist funcional reproduzível

Na conversa privada com o bot, use datas futuras explícitas e anote os IDs
retornados. Os exemplos abaixo usam janeiro de 2030; avance as datas se necessário.
Cada item tem correspondente automatizado em `TelegramAgentPostgresTests`.

- [ ] Peça os serviços e localize/cadastre Ana, telefone fictício 11999999999.
- [ ] Peça orçamento de Ana em 07/01/2030; consulte a agenda: reserva 08h–18h.
  Edite observações, reagende pelo ID para 08/01, confira 07/01 livre e conclua.
- [ ] Crie outro orçamento em 08/01: a conclusão liberou a reserva. Peça
  cancelamento pelo ID: `sim` não basta; envie `confirmar <código>` retornado.
  Consulte histórico e disponibilidade: cancelado, sem reserva ativa.
- [ ] Peça montagem por sete dias em 14/01 às 10h: deve esclarecer, sem gravar.
  Responda 08h e endereço Rua A: sete janelas 08h–18h, incluindo domingo.
  Um orçamento sobreposto deve ser recusado sem alterar os anteriores.
- [ ] Peça visita técnica de 240 minutos em 31/12/2030 às 16h, Rua A:
  16h–18h e 08h–10h de 01/01. Às 09h59 há conflito; às 10h há adjacência.
  Início antes das 08h ou às 18h deve ser recusado. Respostas usam São Paulo;
  os períodos equivalentes são armazenados em UTC.
- [ ] Inicie pedido sem telefone/endereço, reinicie a API e complete: retoma
  contexto sem criar antes de obter os dados. Após 30 minutos, não reutiliza
  contexto expirado. Candidatos homônimos exigem `cliente <ID>` ou
  `agendamento <ID>`; o modelo não escolhe sozinho.
- [ ] Confirmação vencida (5 minutos), repetida ou de versão antiga não altera
  dados. Concorrência aceita somente a versão atual. Mensagem de outra pessoa,
  grupo, bot ou mídia não executa comandos nem chama o modelo.
- [ ] Execute a categoria automatizada para reproduzir redelivery, consumidores
  concorrentes, rollback após gravação e quedas antes/depois do commit/envio.
  Nenhuma falha declara sucesso sem persistência; envio incerto não é repetido.

Ao terminar, interrompa API/túnel e remova o banco com
`docker stop ian59-postgres`. A execução local depende de máquina e túnel ativos;
não oferece disponibilidade 24/7. Não há implantação nesta homologação.
A #51 acrescenta retentativas duráveis da saída privada; consulte os estados e
limites em README.md. Não reenvie automaticamente resultados incertos.
