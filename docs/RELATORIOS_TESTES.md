# Relatórios: roteiro de testes

Use só pacientes fictícios. Depois de atualizar o servidor, reinicie o `dotnet run`. Na pasta `web`, rode uma vez `npm install jspdf@2.5.2 jspdf-autotable@3.8.4` (PDF). Quem muda de perfil precisa entrar de novo.

## Acesso
1. No perfil de teste, marque **Visualizar** em um ou mais relatórios e **Exportar** em "Exportação de relatórios". Entre de novo: o item "Relatórios" aparece no menu com as abas liberadas.
2. Sem "Exportar", o botão Exportar não aparece, mas o relatório abre.
3. Usuário vinculado a médico: a API responde 403 mesmo com a permissão.

## Consultas por profissional
1. Período do mês: atendimentos = consultas concluídas; agendamentos = todas, menos remarcadas.
2. Remarque uma consulta: o agendamento só conta no novo horário.
3. Filtrar por profissional, unidade, convênio e tipo muda os números.

## Ocupação da agenda
1. Disponível = expediente do profissional no período, sem intervalos, feriados e recesso. Confira um profissional com expediente de 8h às 12h em um dia útil: 4h por dia.
2. Marque uma consulta de 30 minutos: o ocupado sobe 0h30 e a porcentagem muda.
3. Cancele a consulta: o ocupado volta. Encaixe não entra no ocupado; aparece em "Encaixes".
4. Uma segunda sem expediente ou um feriado não aumentam o disponível.

## Cancelamentos e faltas
1. Cancele uma consulta como "paciente" e outra como "clínica"; marque uma falta. Os totais mostram cada uma separada e as porcentagens.
2. "Detalhado" lista cada uma com paciente, data, situação, quem cancelou e o motivo, com paginação.
3. Remarcadas não entram.

## Lista de espera
1. Coloque três pessoas fictícias na fila e passe uma para "Horário liberado", uma para "Atendido" e deixe uma "Aguardando". Os quatro cartões mostram cada situação; "Entraram na fila" é a soma.
2. O período vale para a data de entrada na fila. Filtre por profissional e unidade: os números mudam. Convênio e tipo não aparecem nesse relatório.
3. O filtro de situação muda só a lista, não os cartões.
4. "Dias na fila" aparece só para quem ainda espera; atendidos e cancelados mostram "—". O tempo médio considera apenas quem ainda espera.

## Faturamento por tipo
1. Só entram consultas concluídas. Conclua uma particular (com pagamento confirmado) e uma de convênio (guia autorizada): "Recebido / autorizado" soma as duas; sem confirmação, o valor vai para "A confirmar / pendente"; guia negada vai para "Negado".
2. O total confere com o Faturado do Painel financeiro e com a tela de Faturamento no mesmo período.

## Faturamento por convênio
1. Uma linha por convênio, do maior valor para o menor; particular não aparece. O filtro "tipo" não existe aqui.
2. "% do total" soma 100%.

## Cadastros (pacientes, profissionais, colaboradores, convênios, credenciamentos)
1. Marque **Visualizar** nas cinco telas "Relatório cadastral de ..." do perfil de teste e entre de novo: aparece o grupo **Cadastros**. Cada relatório depende só da sua permissão.
2. **Pacientes**: o CPF aparece completo (000.000.000-00), com responsável legal (nome, CPF e telefone) para os menores, convênio, carteirinha e profissional responsável. "Ativos e inativos" traz os dois; "Só inativos" traz só os inativados.
3. **Profissionais**: especialidades e unidades em uma célula só; filtro por unidade.
4. **Colaboradores e prestadores**: com perfil que também tem Visualizar em "Cadastro de colaboradores e prestadores", aparecem CTPS, PIS/PASEP e admissão; sem essa permissão, essas colunas somem e o PDF/CSV avisa nos filtros.
5. **Convênios** (com especialidades e valores) e **Credenciamentos** (convênio, unidade, especialidade, situação, vigência, protocolo e valor; filtros por convênio, unidade e situação).
6. Exporte cada um em CSV e PDF: o CSV traz todas as colunas. No log aparecem VISUALIZAR (a cada consulta) e EXPORTAR (com o número de linhas).
7. Usuário médico: 403, mesmo com a permissão.

## Faturamento particular
1. Só atendimentos particulares concluídos, uma linha por forma de pagamento (dinheiro, cartão, Pix...). "Recebido" é o pagamento confirmado; o resto fica em "A confirmar". Filtros de convênio e tipo não aparecem.

## Faturamento mensal
1. Escolha de janeiro até o mês atual: cada mês é uma linha, inclusive os sem consulta (zerados).
2. A variação compara com o mês anterior da lista; no primeiro mês e quando o anterior é zero aparece "—".
3. Particular + Convênio = Total, e o total do período confere com o Faturado do Painel financeiro.

## Prontuários
1. Aba "Prontuários" (Operacionais): lista os registros do período com paciente, profissional (e conselho) e as marcas "Corrigido" e "Incluído por <nome do administrador>". Não aparece texto clínico.
2. "Pacientes distintos" confere com a quantidade de pacientes diferentes na lista.
3. Administrador: ligue "Mostrar o texto (só na tela)" e o texto aparece. Outro usuário não vê a opção; pela API recebe 403 `TEXTO_SOMENTE_ADMINISTRADOR`.
4. Exportação em PDF e CSV: nunca traz o texto, mesmo com o interruptor ligado.
5. Unidade, convênio e tipo só alcançam registros ligados a uma consulta.
6. Usuário médico: 403, mesmo com a permissão. No log: VISUALIZAR `RELATORIO_PRONTUARIOS` e EXPORTAR.

## Acesso aos novos relatórios
Cada relatório tem permissão própria: faturamento por tipo, por convênio, particular, mensal, lista de espera e os cinco cadastrais. As duas telas novas aparecem no perfil depois de reiniciar o servidor. Marque **Visualizar** no perfil e entre de novo. O seletor "Operacionais | Financeiros" só aparece para quem enxerga os dois grupos.

## Exportação
1. **PDF** e **CSV** em cada relatório: o arquivo traz os mesmos números da tela. O PDF tem a clínica, o título, os filtros, o usuário e a data e hora, e "Página X de Y".
2. O CSV abre no Excel com acentos corretos e colunas separadas.
3. No **Faturamento** (aba Resumo), o botão Exportar também oferece PDF e CSV.
4. Cada exportação aparece no log (ação EXPORTAR).
5. Nos relatórios financeiros, o PDF e o CSV trazem os valores em R$ e a linha de total. Na lista de espera, a exportação respeita o filtro de situação.
