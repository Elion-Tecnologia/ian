# Ian

Agente de inteligência artificial para gerenciar agendas e serviços de equipes de uma loja de móveis planejados por meio do Telegram.

## Objetivo

Permitir que um administrador consulte e organize agendamentos de clientes, equipes, horários, indisponibilidades e programações, com suporte a mensagens de texto e áudio.

## Tecnologias planejadas

- C# / .NET 10 — Worker Service (sem API HTTP própria)
- Microsoft Agent Framework (MAF) com Gemini via Google AI Studio
- Telegram Bot API com Long Polling
- PostgreSQL e Entity Framework Core
- Execução inicial local no macOS

## Organização do desenvolvimento

- `main`: versão estável
- `dev`: integração das funcionalidades
- `feature/*`: desenvolvimento de funcionalidades, integradas à `dev` por Pull Requests

## Situação

Repositório inicializado. A implementação ainda não foi iniciada.

> Nunca adicionar chaves de API, tokens do Telegram ou outros segredos ao repositório.
