# VBS Agendamento – Sistema de agendamento de consultas (Viver Bem Saúde)

Sistema web para agendamento de consultas do Centro Integrado de Psicologia Viver Bem Saúde.

- **Tela (frontend):** React 18 + TypeScript + Vite + Ant Design 5 (pasta `web`)
- **Servidor (API):** ASP.NET Core (.NET 8) + Dapper (pasta `src/VBS.Agendamento.Api`)
- **Banco:** SQL Server Express, banco `VBSAGENDA` (scripts na pasta `banco`)
- **Contrato da API:** OpenAPI 3.0.3, 123 operações (`src/VBS.Agendamento.Api/contrato/openapi.yaml`)

> **Ambiente de desenvolvimento local.** Use somente dados fictícios. Este repositório deve ser **privado**.
> Não grave neste repositório senhas, cadeias de conexão de produção nem dados reais de pacientes.

Para entender como o sistema é organizado, leia o [ARCHITECTURE.md](ARCHITECTURE.md).
Para publicar o código no GitHub, siga o [passo a passo](docs/GITHUB_PASSO_A_PASSO.md).

---

## Situação atual

O trabalho é feito em fatias (servidor e tela juntos, testadas localmente).

| Cadastro / função | Situação |
|---|---|
| Login, sessão, recuperação de senha (e-mail simulado no console) | Pronto |
| Unidades (com Número e Complemento) | Pronto |
| Salas (identificação livre, ex.: "Sala 312" ou "Consultório 01") | Pronto |
| Especialidades | Pronto |
| Convênios | Pronto |
| Formas de pagamento | Pronto |
| Profissionais (horários, expediente, especialidades e unidades) | Pronto |
| Pacientes (menores com responsável legal, consentimentos LGPD, troca de profissional responsável com histórico) | Pronto |
| Perfis de acesso e Usuários | Pronto |
| Colaboradores e prestadores (CLT com dados da carteira de trabalho, MEI, matrícula automática) | Em teste |
| Agenda e consultas | Próxima fatia |
| Lista de espera, prontuário, notificações, relatórios e painéis | Previstos no contrato |

## Requisitos

- .NET SDK 8 ou mais novo (`dotnet --version`)
- Node.js 20 ou mais novo (`node --version`)
- SQL Server Express com o banco `VBSAGENDA` criado
- SQL Server Management Studio (SSMS) para rodar os scripts
- Git (para o repositório)

## Como preparar o banco

1. Abra o SSMS e conecte no servidor do banco.
2. Rode, **em ordem numérica**, os scripts da pasta `banco`: `01` a `18`.
   O script `07` é só de conferência. Cada script informa no cabeçalho o que faz.
3. Os scripts não criam logins no SQL Server. O servidor usa a conta do Windows (autenticação integrada).
4. Regra do projeto: scripts de banco sempre em MAIÚSCULAS (apenas textos entre aspas ficam em caixa normal).

## Como rodar o servidor

1. Confira a cadeia de conexão em `src/VBS.Agendamento.Api/appsettings.Development.json`
   (servidor, banco `VBSAGENDA`, `Integrated Security=True`).
2. No terminal:
   ```
   cd src/VBS.Agendamento.Api
   dotnet run
   ```
3. Na primeira execução, com o banco sem usuários, o console mostra **ADMINISTRADOR INICIAL CRIADO** e a senha gerada.
   Anote a senha (só aparece uma vez) e troque-a depois em **Usuários**.
4. Teste: `http://localhost:5000/vbsagenda` deve responder `"status": "ok"`.
5. Depois de qualquer alteração no servidor, pare (Ctrl+C) e rode `dotnet run` de novo.

Para guardar a conexão ou a senha fora dos arquivos:
`dotnet user-secrets init` e `dotnet user-secrets set "ConnectionStrings:VBSAGENDA" "..."` na pasta do projeto.

## Como rodar a tela

```
cd web
npm install
npm run dev
```

Abra `http://localhost:5173`. Em desenvolvimento o Vite repassa `/api` para `http://localhost:5000`,
então não há problema de CORS. O comando `npm run build` confere os tipos do TypeScript e gera a versão de produção.

## Estrutura do repositório

```
banco/                          scripts SQL numerados (01 a 18)
src/VBS.Agendamento.Api/        servidor ASP.NET Core
  Common/                       erros, validações (CPF, CNPJ, e-mail, telefone), datas de Brasília
  Infra/                        banco, auditoria, segurança (sessão e liberações), inicialização
  Features/<Nome>/              uma pasta por funcionalidade (Controller, Service, Dtos)
  contrato/openapi.yaml         contrato da API
web/                            tela React
  src/api/                      cliente HTTP e tipos
  src/auth/                     sessão e proteção de rotas
  src/layout/                   menu e cabeçalho
  src/paginas/<nome>/           uma pasta por tela
  src/util/                     máscaras e validações (CPF, CNPJ, celular, datas)
postman/                        coleção para testar a API
docs/                           guias do projeto
```

## Acesso e liberações

- Cada tela do menu só aparece se o perfil puder **visualizar** a tela. As ações (incluir, editar, excluir, exportar)
  são conferidas pelo servidor em cada requisição.
- O perfil **Administrador** é protegido e tem acesso total.
- Códigos de tela e ação seguem o padrão `tela.cadastro:acao` (por exemplo, `sala.cadastro:editar`).

## Pendências conhecidas

- Liberação campo a campo (catálogo de campos ainda vazio).
- Retenção do log de auditoria (RNF04) em aberto.
- E-mail de recuperação de senha ainda simulado (o link aparece no console do servidor).
- Testes automáticos ainda não existem.
- Definição final do credenciamento de convênios (opção A em teste).

## Convenções de trabalho

- Cada entrega altera servidor e tela juntos e é testada localmente antes de seguir.
- Nomes de campos do banco em MAIÚSCULAS, com sublinhado (ex.: `CODIGO_UNIDADE`).
- Mensagem padrão para campo obrigatório vazio: **"Campo Não Preenchido"**.
- Inativar em vez de excluir (RN14).
- Nunca versionar dados reais, senhas ou arquivos `appsettings.Local.json`/`secrets.json`.
