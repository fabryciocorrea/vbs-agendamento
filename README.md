# Viver Bem Saúde – Sistema de agendamento de consultas

Sistema web de agendamento de consultas, com agenda por profissional, convênios com credenciamento, guias, faturamento e controle de acesso por perfil.

> **Situação:** desenvolvimento e testes locais (`localhost`), com pacientes fictícios. Não há dados reais no banco. O código C# foi escrito sem compilador no ambiente de geração; erros de build são corrigidos conforme aparecem.

Detalhes técnicos (camadas, regras, fluxos e banco): veja [ARCHITECTURE.md](ARCHITECTURE.md).

## Tecnologias

| Camada | Tecnologia |
|---|---|
| Front-end | React 18, TypeScript, Vite, Ant Design 5, React Router 6 |
| API | ASP.NET Core (net8.0), Dapper, Microsoft.Data.SqlClient |
| Banco | SQL Server Express, banco `VBSAGENDA` (scripts em `banco/`) |

## O que já funciona

| Área | Conteúdo |
|---|---|
| Acesso | Login, sessão com expiração por inatividade, bloqueio após 5 senhas erradas, recuperação de senha, perfis de acesso com permissão por tela e ação |
| Cadastros | Pacientes (com responsável legal e consentimento), profissionais, colaboradores, unidades, salas, especialidades, formas de pagamento, convênios, usuários e perfis |
| Convênios | Valor por especialidade, credenciamento por unidade, valor vigente na data da consulta |
| Agenda | Agenda semanal do profissional, nova consulta (particular ou convênio, presencial ou online), encaixe, remarcação, horários livres conforme expediente, feriados e recessos |
| Fluxo da consulta | Agendada → Confirmada → Check-in → (pagamento, se particular) → Em atendimento → Concluída; também Cancelada, Falta e Remarcada, com motivo e prazo |
| Convênio | Guia do convênio (pendente, autorizada, negada); só inicia e conclui atendimento com guia autorizada |
| Pagamento particular | Confirmado pela recepção após o check-in; exigido para iniciar o atendimento |
| Faturamento | Tela única com resumo financeiro, guias de convênio, particulares e exportação CSV |
| Administrador | Sem bloqueios: ao esbarrar numa regra, informa uma justificativa (10 a 500 caracteres) e a operação é refeita e registrada |
| Auditoria | Login, consulta, inclusão, alteração e exportação gravados em `LOG_AUDITORIA` |

## Como rodar

**Requisitos:** .NET SDK 8, Node.js 18 ou mais novo, SQL Server Express com o banco `VBSAGENDA`.

1. **Banco:** no SSMS, rode os scripts de `banco/` na ordem numérica (01 a 27). Depois de cada atualização do projeto, rode os scripts novos.
2. **Conexão:** confira `src/VBS.Agendamento.Api/appsettings.Development.json` (autenticação integrada do Windows, sem criar login novo).
3. **API:**
   ```
   cd src/VBS.Agendamento.Api
   dotnet run
   ```
   Na primeira execução, com o banco sem usuários, o console mostra **ADMINISTRADOR INICIAL CRIADO** com a senha gerada (aparece uma única vez). Teste em `http://localhost:5000/vbsagenda`.
4. **Telas:**
   ```
   cd web
   npm install
   npm run dev
   ```
   Abra `http://localhost:5173`.

Sempre que alterar arquivos do servidor, pare e rode `dotnet run` de novo. O catálogo de telas é gravado sozinho a cada início.

## Perfil Recepção (exemplo)

Para o usuário ver só **Agenda** e **Pacientes** no menu, marque no perfil: Agenda do profissional (Visualizar), Cadastro de pacientes (Visualizar, Incluir, Editar), Agendamento de consultas (Visualizar, Incluir, Editar) e Status da consulta (Editar). As duas últimas não criam itens de menu; só liberam botões dentro da Agenda.

## Estrutura de pastas

```
banco/                      scripts SQL (01 a 27), rodar em ordem no SSMS
docs/                       roteiros de teste e notas de cada funcionalidade
postman/                    coleção de testes da API
src/VBS.Agendamento.Api/    servidor (Program.cs, Common, Infra, Features, contrato)
web/                        telas (src/api, auth, layout, paginas, util)
```

## Pendências conhecidas

- Retenção de logs (RNF04) em aberto.
- Lista de espera, notificações, relatórios, dashboards e prontuário ainda sem tela.
- Link de videochamada (Google Meet) para consultas online.
- Liberação campo a campo (catálogo `CAMPO_SISTEMA` vazio).
- Testes automáticos.
- Produção: conta própria da API na `ROLE_APP_AGENDAMENTO`, servidor de e-mail real (hoje o link de recuperação aparece no console), backup e criptografia de disco.

## Se o `dotnet build` reclamar

- Versão do .NET diferente: troque `net8.0` em `VBS.Agendamento.Api.csproj`.
- Pacotes: `Dapper` 2.1.35 e `Microsoft.Data.SqlClient` 5.2.2 (ou a mais nova).
- "certificate chain": confira `TrustServerCertificate=True` na conexão.
- Erro de login no SQL Server: a conta do Windows que roda o `dotnet run` precisa de acesso ao `VBSAGENDA`.
