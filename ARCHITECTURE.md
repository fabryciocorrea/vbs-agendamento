# Arquitetura – Viver Bem Saúde (agendamento)

## 1. Visão geral

```
Navegador (React + Ant Design)  ──HTTP/JSON──►  API ASP.NET Core  ──Dapper──►  SQL Server (VBSAGENDA)
   web/                                          src/VBS.Agendamento.Api         procedures, gatilhos, views
```

- **Um projeto de API só**, organizado em pastas por funcionalidade (`Features/<Nome>`). Dividir em camadas separadas continua possível.
- **Dapper com SQL escrito à mão**, porque o banco tem procedures, gatilhos e `ROWVERSION` usados diretamente.
- **Regras críticas ficam no banco** (procedures e gatilhos) e a API as traduz em erros legíveis. O front-end só esconde ou desabilita; quem garante é o servidor.

## 2. Servidor (`src/VBS.Agendamento.Api`)

```
Program.cs                 configuração, pipeline, AddScoped dos serviços
Common/                    erros (Erro.Validacao/Regra/Conflito/NaoEncontrado/SemPermissao), CPF, e-mail, DataBrasil, Formatos
Infra/Db/                  ConexaoFactory, Sql.Cmd (comando com transação e cancelamento), auditoria
Infra/Seguranca/           SessaoAuthHandler, PermissaoService, [Permissao], hash de senha
Infra/Inicializacao/       CatalogoTelas (telas gravadas ao iniciar), administrador inicial
Features/<Nome>/           Dtos.cs, Service.cs, Controller.cs
contrato/openapi.yaml      contrato da API (servido em /openapi.yaml em desenvolvimento)
```

Funcionalidades: Auth, Pacientes, Profissionais, Colaboradores, Unidades, Salas, Especialidades, FormasPagamento, Convenios, Credenciamentos, Agenda, Consultas, Faturamento (guias e financeiro), Usuarios, Perfis, Apoio (listas).

### 2.1 Segurança

- **Sessão opaca no servidor:** token aleatório; só o hash vai para `SESSAO_USUARIO`. Expira após 30 minutos de inatividade, encerra no logout, no bloqueio ou na troca de senha.
- **Senha:** PBKDF2-SHA256, 210 mil iterações, sal por senha. Bloqueio após 5 erros; limite de 10 tentativas por minuto nas rotas públicas.
- **Permissão por tela e ação** em toda requisição: `[Permissao("tela:acao", ...)]`. Várias exigências funcionam como **OU**. O administrador passa por `PermissaoService.TemAsync`. Ações: visualizar, incluir, editar, excluir, exportar.
- **Catálogo de telas** (`CatalogoTelas.cs`) gravado em `TELA_SISTEMA` a cada início (insere novas, atualiza nomes, nunca apaga). Permissões do administrador são protegidas por gatilho.
- **Listas de apoio** (`/apoio/{tipo}`) aceitam a permissão de qualquer tela que as use (agenda, agendamento, pacientes, convênios, faturamento), para que a Recepção funcione sem a tela "Listas de apoio".
- **Auditoria:** `LOG_AUDITORIA` somente de inclusão (ações VISUALIZAR, INCLUIR, EDITAR, EXCLUIR, EXPORTAR, LOGIN, LOGIN_FALHA, CORRECAO_PRONTUARIO).
- **Datas:** gravadas em UTC e exibidas no horário de Brasília (`DataBrasil`).

### 2.2 Erros

Formato `application/problem+json`, com `codigo` estável para o front-end tratar (por exemplo `CONVENIO_NAO_CREDENCIADO`, `GUIA_NAO_AUTORIZADA`, `PAGAMENTO_PENDENTE`, `HORARIO_NO_PASSADO`). Conflito de versão (`ROWVERSION` / `versao`) devolve 409.

## 3. Regras de negócio principais

### 3.1 Status da consulta (procedure `USP_CONSULTA_ALTERAR_STATUS`)

```
AGENDADA ─► CONFIRMADA ─► CHECK_IN ─► EM_ATENDIMENTO ─► CONCLUIDA
   │            │            │
   └────────────┴────────────┴──► CANCELADA (prazo e motivo)    AGENDADA/CONFIRMADA ─► FALTA (após o horário)
```

- Atalho para CONCLUIDA a partir de AGENDADA, CONFIRMADA ou CHECK_IN só depois do horário de início.
- **Particular:** o pagamento só é confirmado após o check-in e é exigido para iniciar o atendimento.
- **Convênio:** iniciar e concluir exigem guia **AUTORIZADA** (`GUIA_STATUS`: PENDENTE, AUTORIZADA, NEGADA; autorizada marca o pagamento como confirmado).
- Status finais (concluída, cancelada, falta) nunca mudam.

### 3.2 Convênio, credenciamento e valor

- `CONVENIO_ESPECIALIDADE` guarda o valor por convênio e especialidade; `CREDENCIAMENTO_CONVENIO` pode sobrepor o valor por unidade, com vigência.
- O valor da consulta por convênio é buscado no servidor (`CredenciamentoService.BuscarValorAsync`), vigente na data da consulta.
- Qualquer perfil pode escolher o convênio na Nova consulta, mas é preciso **credenciamento ativo** (unidade + especialidade). Sem ele, só o administrador libera, com justificativa.
- A carteirinha do cadastro só vale para o convênio do paciente.

### 3.3 Liberação do administrador

O administrador não tem bloqueios. Ao esbarrar numa regra liberável, o front-end abre uma janela só com a justificativa (10 a 500 caracteres) e repete a operação com `liberacaoAdministrador`. O servidor valida que quem liberou é administrador, grava a última liberação em `CONSULTA.LIBERACAO_ADMIN_*` e acrescenta ao histórico. Liberável: horário fora das regras, convênio sem credenciamento, convênio inativo, carteirinha vencida, guia não autorizada, pagamento pendente, transições de status ativas, cancelamento fora do prazo.

**Continuam bloqueados para todos (integridade):** status finais, sobreposição de consultas e limite de encaixes, log somente de inclusão, permissões do administrador.

### 3.4 Faturamento

Tela única (`faturamento.financeiro`): resumo do período (consultas concluídas), guias de convênio (registrar número e situação), particulares (pagamento) e exportação CSV (com auditoria).

## 4. Front-end (`web/src`)

```
main.tsx, App.tsx           rotas; cada rota protegida por <RotaProtegida tela="...">
layout/LayoutPrincipal.tsx  menu lateral (itens com { key, label, icon, tela }); só aparece o que o perfil permite
auth/                       AuthContext (sessao, pode(tela, acao)) e RotaProtegida
api/cliente.ts, tipos.ts    cliente HTTP (api.get/post/put/patch), ApiErro (status, codigo, erros, extras) e tipos
paginas/<área>/             uma pasta por tela (agenda, pacientes, convenios, credenciamentos, faturamento, perfis, usuarios, ...)
componentes/, util/         componentes e formatadores compartilhados
```

Pontos importantes:
- **Menu e rotas** dependem das permissões do perfil. Para um perfil ver só Agenda e Pacientes, basta liberar só essas telas em Perfis de acesso.
- **Agenda** (`paginas/agenda`): `AgendaProfissionalPagina` (semana por profissional), `NovaConsultaModal`, `AcoesConsulta` (botões do fluxo de status, pagamento, remarcação) e `liberacao.tsx` (justificativa do administrador).
- Botões são exibidos conforme o status, a permissão e o tipo (particular ou convênio); o servidor revalida tudo.

## 5. Banco de dados (`banco/`)

Scripts numerados, executados em ordem no SSMS. Marcos:

| Faixa | Conteúdo |
|---|---|
| 01–05 | Tabelas, objetos, views, login e sessão |
| 06–08 | Status, remarcação, formas de pagamento, prontuário, administrador |
| 09–11 | Credenciamento, notificações, índices |
| 12–18 | Endereço, contatos opcionais, responsável legal, LGPD, sala, dados CLT |
| 19–22 | Valor de convênio, por especialidade e por credenciamento |
| 23–27 | Guia do convênio, pagamento após check-in, faturamento unificado, guia autorizada, liberação do administrador |

Convenções: nomes de objetos e comandos em MAIÚSCULAS; sem criar outros logins no SQL Server; conexão por autenticação integrada em desenvolvimento.

## 6. Como acrescentar uma funcionalidade

1. Script SQL novo em `banco/` (próximo número), em maiúsculas.
2. `Features/<Nome>/` com Dtos, Service e Controller; `[Permissao("tela:acao")]` em cada ação.
3. Registrar o serviço no `Program.cs` (`AddScoped`) e a tela em `CatalogoTelas.cs`, se for nova.
4. Atualizar `contrato/openapi.yaml`.
5. Tela em `web/src/paginas/<área>`, rota em `App.tsx` e item no menu em `LayoutPrincipal.tsx`.
6. Reiniciar o `dotnet run` e rodar o script no SSMS.

## 7. Limitações e decisões em aberto

- Retenção de logs (RNF04); P18 adiado; liberação campo a campo; Always Encrypted do prontuário; lista de espera, notificações, relatórios e Google Meet; produção (conta da API, e-mail, backup, disco criptografado).
