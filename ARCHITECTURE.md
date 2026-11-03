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

Funcionalidades: Auth, Pacientes, Profissionais, Colaboradores, Unidades, Salas, Especialidades, FormasPagamento, Convenios, Credenciamentos, Agenda, Consultas, Faturamento (guias e financeiro), ListaEspera, Dashboards (painel), Exclusao, Usuarios, Perfis, Apoio (listas).

### 2.1 Segurança

- **Sessão opaca no servidor:** token aleatório; só o hash vai para `SESSAO_USUARIO`. Expira após 30 minutos de inatividade, encerra no logout, no bloqueio ou na troca de senha.
- **Senha:** PBKDF2-SHA256, 210 mil iterações, sal por senha. Bloqueio após 5 erros; limite de 10 tentativas por minuto nas rotas públicas.
- **Permissão por tela e ação** em toda requisição: `[Permissao("tela:acao", ...)]`. Várias exigências funcionam como **OU**. O administrador passa por `PermissaoService.TemAsync`. Ações: visualizar, incluir, editar, cancelar, excluir, exportar. A ação **cancelar** é separada de editar (`PERFIL_TELA_PERMISSAO.PODE_CANCELAR`) e é exigida para cancelar consulta (`consulta.status`), guia (`faturamento.financeiro`) e item da lista de espera (`listaespera.cadastro`); quem só edita não cancela. As permissões são lidas no login: depois de mudar o perfil, o usuário entra de novo.
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

### 3.4 Exclusão de cadastros

`Features/Exclusao` concentra as exclusões definitivas, **só para o administrador** (`DELETE` em pacientes, convênios, credenciamentos, profissionais, colaboradores, especialidades, formas de pagamento, unidades, salas e usuários). Em cada uma o servidor confere o histórico (consultas, prontuário, lista de espera, usuários vinculados etc.); se houver, responde `409 REGISTRO_COM_VINCULOS` e o caminho é **Inativar**. Vínculos de cadastro (por exemplo, especialidades de um profissional) saem junto. Todas gravam `EXCLUIR` no log. Usuário não exclui a si mesmo nem usuário de perfil protegido. No front-end, o `BotaoExcluir` só aparece para o administrador.

Para o paciente poder ser excluído, o script 29 retira as chaves de `LOG_AUDITORIA` e dos consentimentos LGPD para `PACIENTE` e `RESPONSAVEL_LEGAL`; esses registros de somente inclusão continuam guardados, sem vínculo com uma linha existente.

### 3.5 Faturamento

Tela única (`faturamento.financeiro`): resumo do período (consultas concluídas), guias de convênio (registrar número e situação), particulares (pagamento) e exportação CSV (com auditoria). A lista de guias traz todas as consultas de convênio, menos as remarcadas, inclusive as canceladas.

**Guia cancelada (script 30).** Situações da guia: PENDENTE, AUTORIZADA, NEGADA e CANCELADA. A guia cancelada não muda mais.
- Cancelar a consulta cancela sozinho a guia pendente ou negada.
- Com a guia **autorizada**, a consulta não pode ser cancelada (erro 50016 na procedure, `GUIA_AUTORIZADA_CANCELAR_ANTES` na API): o faturamento cancela a guia primeiro, informando o motivo. O administrador pode liberar com justificativa.
- A guia autorizada só aceita a ação Cancelar. Depois que a consulta está cancelada, só se cancela a guia autorizada que ficou.

### 3.6 Lista de espera

`Features/ListaEspera` (`listaespera.cadastro`): situações AGUARDANDO, NOTIFICADO (horário liberado), ATENDIDO e CANCELADO. Quando uma consulta futura é cancelada e há paciente aguardando para o mesmo profissional ou especialidade, a procedure de status marca o item como NOTIFICADO e cria a notificação interna (RN08). Cancelar um item exige a ação cancelar.

### 3.7 Painel

`Features/Dashboards`, `GET /api/v1/dashboards/painel-do-dia` (`dashboard.painel:visualizar`; médico recebe 403 `MEDICO_SEM_ACESSO`, RN20). Recebe `dataInicial` e `dataFinal` (um dia, uma semana ou até 366 dias) e filtros de unidade, profissional e tipo (convênio ou particular). Devolve totais por situação, pendências, distribuição por convênio e particular (sem canceladas, remarcadas e faltas), lista de espera e as consultas em andamento (até 12). Pendências de faturamento e lista de espera só vêm para quem tem essas permissões. O front-end atualiza a cada 30 segundos; as pendências abrem janelas (`PainelModais.tsx`) em vez de trocar de tela.

### 3.8 Painel financeiro

`GET /api/v1/dashboards/financeiro` (`dashboard.financeiro:visualizar`; médico recebe 403 `MEDICO_SEM_ACESSO`). Período de até 366 dias (padrão: o mês atual) e filtros de unidade, profissional e tipo. O **faturado** soma só as consultas CONCLUIDAS; as ainda ativas (agendada, confirmada, check-in, em atendimento) vão em **previsto**; canceladas, remarcadas e faltas não contam. Devolve totais (particular recebido e a confirmar; convênio por situação da guia), evolução diária (períodos de até 62 dias) ou mensal, e quadros por convênio/particular, especialidade e profissional com a participação em %. Tela: `paginas/dashboards/PainelFinanceiroPagina.tsx`, rota `/painel-financeiro`. A comparação com o mês anterior e a liberação do campo de valor (RN20) seguem pendentes até existir o catálogo de campos.

### 3.8b Consultas sem credenciamento (J4)

`GET /consultas/sem-credenciamento` (em `ConsultaService`, permissão `consulta.agendamento:visualizar`): consultas de convênio AGENDADA ou CONFIRMADA, futuras, cujo credenciamento deixou de valer (`FN_CREDENCIADO` com a data de Brasília), exceto as liberadas pelo administrador. Filtros de unidade e convênio; o médico vê só as suas. Nada é cancelado. Na tela `/consultas-sem-credenciamento`; o aviso de credenciamento do sino abre essa tela.

### 3.8a Sino de notificações

`Features/Notificacoes` (`notificacao.interna`): lê `NOTIFICACAO_INTERNA`, onde as procedures gravam os avisos (horário liberado, credenciamento, guia cancelada etc.). `GET /notificacoes-internas` (filtros tipo, lida, consultaId), `GET /notificacoes-internas/nao-lidas/contagem` e `PATCH /notificacoes-internas/{id}/leitura`. Cada usuário vê os avisos dirigidos a todos e os dirigidos a ele; o usuário vinculado a profissional vê só os dele. Lido por um, lido por todos: a primeira leitura grava quem leu e quando e marcar de novo não muda nada. Nada é excluído. No navegador, `layout/SinoNotificacoes.tsx` consulta o contador a cada minuto (pausa com a aba escondida) e abre uma gaveta com a lista.

### 3.9 Relatórios

`Features/Relatorios` (`GET /api/v1/relatorios/...`, uma permissão por relatório; médico recebe 403 `MEDICO_SEM_ACESSO`). Período de até 366 dias e filtros de profissional, unidade, convênio e tipo. Cada geração vai para o log (`VISUALIZAR`).
- **Consultas por profissional:** atendimentos (concluídas) e agendamentos (tudo, menos remarcadas).
- **Ocupação da agenda:** minutos disponíveis (expediente do profissional sem intervalos, feriados e recesso, calculado por `AgendaService.MontarCalendarioAsync`) e ocupados (duração das consultas dentro do expediente; canceladas, remarcadas e encaixes ficam de fora; encaixes aparecem à parte).
- **Cancelamentos e faltas:** por profissional, com cancelamento pelo paciente ou pela clínica (`CONSULTA_STATUS_HISTORICO.CANCELADO_POR`); a visão detalhada lista paciente, data, motivo e quem cancelou, com paginação.
- **Lista de espera:** por data de entrada na fila, com totais por situação (aguardando, horário liberado, atendido, cancelado), tempo médio de espera de quem ainda está na fila e lista paginada filtrável por situação. Convênio e tipo não se aplicam.
- **Prontuários:** registros por paciente e profissional no período (data do registro), com pacientes distintos e marcas de corrigido e incluído pelo administrador. Nunca traz o texto clínico (L7); só o administrador pode ver o texto na tela (`incluirTexto`), e a exportação nunca o inclui. Médico não acessa (RN20). Cada geração e exportação vão para o log.
- **Financeiros** (mesma regra do Painel financeiro: só consultas CONCLUIDAS): **faturamento por tipo** (particular e convênio), **por convênio** (uma linha por convênio, do maior valor ao menor), **particular** (uma linha por forma de pagamento) e **mensal** (um mês por linha, meses sem faturamento zerados, variação contra o mês anterior do relatório). Cada linha separa confirmado (particular recebido; convênio com guia autorizada), pendente e negado. Código em `RelatorioService.Financeiro.cs` (classe `partial`).
- **Cadastrais** (`RelatorioService.Cadastros.cs`, `GET /relatorios/cadastros/{pacientes|profissionais|colaboradores|convenios|credenciamentos}`): tabela genérica (colunas e linhas já formatadas) com **todos os dados** do cadastro e o **CPF completo, sem máscara**. Cada tipo tem permissão própria (`relatorio.cadastro_*`), nunca vale para o médico, e cada consulta (`VISUALIZAR`) e exportação (`EXPORTAR`) vão para o log (LGPD). Pacientes trazem também o responsável legal; profissionais, as especialidades e unidades; convênios, as especialidades; credenciamentos, vigência, protocolo e valor. Em colaboradores e prestadores, CTPS, PIS/PASEP e admissão só saem para quem também tem `colaborador.cadastro:visualizar`. Filtros: ativos/inativos e, por tipo, nacionalidade (pacientes nacionais ou estrangeiros; em "estrangeiros" a tabela mostra também o tipo e o número do documento, em "nacionais" os oculta), profissional responsável, convênio, unidade, vínculo ou situação do credenciamento.
- Na tela, os relatórios ficam agrupados em **Operacionais**, **Financeiros** e **Cadastros**; o seletor só aparece se o usuário enxerga os dois grupos.

**Exportação em PDF e CSV.** O servidor entrega a tabela já formatada (`TabelaExportacao`: título, filtros, colunas, linhas e totais) em `GET /relatorios/{relatorio}/exportacao` e, para o Faturamento, em `GET /faturamento/exportacao-tabela`; exige a permissão de exportação **e** a de visualização do relatório, limita a 10.000 linhas e grava `EXPORTAR` no log. O navegador (`util/exportar.ts`, com jsPDF e jspdf-autotable) monta o PDF (clínica, relatório, filtros, usuário, data e hora, número de página) ou o CSV (UTF-8 com BOM e `;`). O CSV do Faturamento continua sendo gerado pelo servidor (`/faturamento/exportacao`). Tela: `paginas/relatorios/RelatoriosPagina.tsx`, rota `/relatorios`; o botão é `componentes/MenuExportar.tsx`.

### 3.10 Cadastro de paciente

CPF obrigatório para todo paciente brasileiro, inclusive o menor de idade; o estrangeiro pode usar tipo e número de documento. Menor de 18 anos exige responsável legal; telefone e e-mail são opcionais para o menor.

Campos de identificação e endereço (script `banco/32_PACIENTE_SEXO_FILIACAO_CEP_V1_3.sql`): **sexo** (M, F ou O, opcional); **nome da mãe obrigatório**, exceto quando o paciente marca **"Não possui"** (o documento não registra a mãe; nesse caso o nome deve ficar vazio); **nome do pai** opcional; **país** obrigatório (a tela não preenche sozinha; os cadastros antigos ficam com Brasil); **CEP** obrigatório, 8 dígitos; **bairro** obrigatório. O **tipo do documento do estrangeiro** é uma lista: Passaporte de origem, RNE / CRNM ou Outro documento. Cadastros antigos sem o nome da mãe, CEP ou bairro passam a exigi-los no próximo salvamento. O relatório cadastral de pacientes mostra todos esses campos (filiação em seção própria).

### 3.11 Prontuário (`Features/Prontuarios`)
- Rotas `api/v1/pacientes/{pacienteId}/prontuario` (listar, incluir), `/{registroId}` (consultar, editar) e `/{registroId}/correcoes`. Respostas com `Cache-Control: no-store`.
- Acesso (H1): liberação da tela **e** ser o profissional responsável atual do paciente ou administrador. Quem não cumpre recebe 403 `ACESSO_PRONTUARIO_NEGADO`, e a tentativa fica no log (H2). Toda leitura grava VISUALIZAR com a quantidade de registros.
- Inclusão: o profissional responsável faz INSERT direto (nome e conselho copiados do cadastro, data e hora do servidor). O administrador usa `USP_PRONTUARIO_INCLUIR_ADMIN` com profissional, solicitante, autorizador e observação. Registro idêntico ao anterior em menos de 2 minutos: 409 `REGISTRO_DUPLICADO` (H4).
- Autorização do administrador (script 33): profissional solicitante escolhido na lista, nome do responsável legal, observação e **documento anexado** (PDF, JPG ou PNG, até 5 MB, tipo conferido pelo conteúdo, SHA-256, imutável em `PRONTUARIO_AUTORIZACAO_DOC`). O documento é enviado antes (`POST .../prontuario/autorizacoes`), vale para um uso e é baixado só por quem tem `prontuario.correcao:visualizar`.
- Transferência (`POST .../prontuario/transferencia`, tela `prontuario.transferencia`, script 34): troca o profissional responsável (RN18) com motivo obrigatório, gravado em `PACIENTE_PROFISSIONAL_HISTORICO.MOTIVO` e no log. O anterior perde o acesso (RN16); os registros antigos seguem em nome de quem escreveu. Quem tem a liberação transfere sem precisar ser o responsável atual (a transferência não lê texto clínico). O cadastro do paciente usa a mesma transferência (botão "Transferir prontuário"; `PUT /pacientes/{id}/profissional-responsavel` também exige a liberação e o motivo). Consultas já agendadas não mudam; a resposta traz quantas ficaram com o profissional anterior e a tela avisa.
- Edição: só o administrador, por `USP_PRONTUARIO_EDITAR_ADMIN`; o gatilho grava `PRONTUARIO_CORRECAO` e o log. Nada é excluído (RN19).
- Acesso pelo cadastro do paciente: botão "Prontuário" na lista de Pacientes (liberação `prontuario.registro`, mais `paciente.cadastro:visualizar`). A Agenda tem o botão "Prontuário" nas consultas em atendimento ou concluídas, que abre o prontuário já vinculado à consulta.
- Tela: `paginas/prontuario/ProntuarioPagina.tsx` (rota `/pacientes/:pacienteId/prontuario`), aberta pelo botão "Prontuário" da lista de pacientes. O rascunho fica só no `localStorage` do navegador.

## 4. Front-end (`web/src`)

```
main.tsx, App.tsx           rotas; cada rota protegida por <RotaProtegida tela="...">
layout/LayoutPrincipal.tsx  menu lateral (itens com { key, label, icon, tela }); só aparece o que o perfil permite
auth/                       AuthContext (sessao, pode(tela, acao)) e RotaProtegida
api/cliente.ts, tipos.ts    cliente HTTP (api.get/post/put/patch), ApiErro (status, codigo, erros, extras) e tipos
paginas/<área>/             uma pasta por tela (agenda, pacientes, convenios, credenciamentos, faturamento, perfis, usuarios, ...)
componentes/, util/         componentes e formatadores compartilhados (LinkPaciente e LinkConvenio abrem o cadastro em uma janela)
```

Pontos importantes:
- **Menu e rotas** dependem das permissões do perfil. Para um perfil ver só Agenda e Pacientes, basta liberar só essas telas em Perfis de acesso.
- **Agenda** (`paginas/agenda`): `AgendaProfissionalPagina` (semana por profissional, filtro de situação, etiqueta Remarcada), `NovaConsultaModal`, `AcoesConsulta` (botões do fluxo de status, pagamento, remarcação) e `liberacao.tsx` (justificativa do administrador).
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
| 28–29 | Inativação de credenciamento; exclusão pelo administrador (retira as chaves que ligavam log e consentimentos ao paciente) |
| 30 | Guia cancelada: guia autorizada impede cancelar a consulta (o faturamento cancela a guia antes; administrador pode liberar); consulta cancelada cancela a guia pendente ou negada |
| 31 | Permissão "Cancelar" nos perfis de acesso (separada de "Editar"): cancelar consulta, guia e item da lista de espera |

Convenções: nomes de objetos e comandos em MAIÚSCULAS; sem criar outros logins no SQL Server; conexão por autenticação integrada em desenvolvimento.

## 6. Como acrescentar uma funcionalidade

1. Script SQL novo em `banco/` (próximo número), em maiúsculas.
2. `Features/<Nome>/` com Dtos, Service e Controller; `[Permissao("tela:acao")]` em cada ação.
3. Registrar o serviço no `Program.cs` (`AddScoped`) e a tela em `CatalogoTelas.cs`, se for nova.
4. Atualizar `contrato/openapi.yaml`.
5. Tela em `web/src/paginas/<área>`, rota em `App.tsx` e item no menu em `LayoutPrincipal.tsx`.
6. Reiniciar o `dotnet run` e rodar o script no SSMS.

## 7. Limitações e decisões em aberto

- Retenção de logs (RNF04); P18 adiado; liberação campo a campo; Always Encrypted do prontuário; Google Meet; produção (conta da API, e-mail, backup, disco criptografado).
