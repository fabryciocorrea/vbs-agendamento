# Pacotes de consultas - roteiro de testes

Pré-requisitos: script `banco/37_PACOTES_V1_3.sql` rodado; servidor reiniciado; em Perfis de acesso, liberar "Pacotes de consultas (modelos)" e "Pacotes vendidos" (recepção: venda visualizar/incluir/editar). Use só pacientes fictícios.

## Modelos
1. Pacotes > Modelos de pacote > Novo: nome "Fisioterapia mensal", especialidade, 8 sessões, R$ 800,00, validade 30 dias. Deve listar valor por sessão R$ 100,00.
2. Repetir o mesmo nome: erro 409 PACOTE_DUPLICADO.
3. Sessões 0 ou 61, valor 0, validade 0 ou 366: erro de validação.
4. Inativar o modelo: some da lista de venda; pacotes já vendidos continuam.

## Venda
5. Pacotes vendidos > Vender: paciente, modelo, forma de pagamento. Validade = hoje + 29 dias.
6. Pagamento "já recebido" marcado: sai como pago; senão, "Confirmar pagamento" depois (as consultas ligadas passam a pagas).

## Nova consulta (tipo Particular)
7. Campo "Pacote" lista só pacotes disponíveis do paciente na especialidade escolhida.
8. Escolher um pacote: Valor e Forma somem; a consulta sai com o valor da sessão; saldo cai 1.
9. "Vender um pacote novo...": escolher o modelo; vende e usa a primeira sessão na mesma operação. Se o horário der conflito, o pacote não é vendido.
10. Convênio: o campo não aparece; via API, pacote com CONVENIO dá erro.
11. Especialidade diferente, data após a validade, pacote sem saldo ou cancelado: erros PACOTE_ESPECIALIDADE, PACOTE_VENCIDO, PACOTE_SEM_SALDO, PACOTE_CANCELADO.

## Saldo
12. Cancelar a consulta: sessão volta. Marcar falta: sessão consumida. Remarcar: continua usando 1 sessão e o pacote acompanha a nova consulta.
13. Abrir o pacote (sessões): lista as consultas, com "consome" falso para canceladas e remarcadas.
14. Duas abas, última sessão ao mesmo tempo: só uma conclui; a outra recebe PACOTE_SEM_SALDO.

## Cancelamento e exclusão
15. Cancelar pacote sem sessões usadas, com motivo: ok. Com sessão usada: 409 PACOTE_EM_USO.
16. Excluir paciente, especialidade ou forma de pagamento com pacote: 409 REGISTRO_COM_VINCULOS.

## Permissões e log
17. Médico vinculado só vê pacotes dos seus pacientes (RN16/RN49).
18. Venda, uso, pagamento e cancelamento aparecem no log de auditoria.

## Agendamento repetido (Nova consulta > Repetir)
19. Escolher "Toda semana", 4 consultas, segunda 09/11 às 10h: cria 09, 16, 23 e 30/11, todas às 10h. A prévia de datas aparece antes de agendar.
20. "A cada 15 dias": 09/11, 23/11, 07/12, 21/12.
21. "Todo mês": 09/11 (2ª segunda), 14/12, 11/01, 08/02. Começando na 5ª segunda (30/11): 28/12 (última), 25/01...
22. Ocupar uma das datas antes (ou escolher uma série que cruze feriado): as demais são criadas e a janela final lista a data pulada com o motivo.
23. Primeira data indisponível: nada é criado e aparece o erro da própria consulta.
24. Com pacote de 4 sessões e série de 6: cria 4 e informa as 2 últimas como PACOTE_SEM_SALDO. Com "Vender um pacote novo": vende uma vez só.
25. "Pagamento já recebido" (consulta avulsa) vale só para a primeira consulta. Encaixe esconde a repetição.

## Cancelar a série (Cancelar consulta > "O que cancelar")
26. Rodar o script 38 antes. Criar uma série semanal de 4 consultas e abrir a primeira: o cancelamento mostra "Somente esta consulta" ou "Esta e as próximas da série (3 depois desta)".
27. "Somente esta": cancela só ela. Abrir a segunda: agora mostra "2 depois desta".
28. "Esta e as próximas": cancela a atual e todas as futuras AGENDADA ou CONFIRMADA; mensagem "N consulta(s) cancelada(s)". Consulta já em check-in ou concluída da série não é cancelada.
29. Consulta avulsa (sem série) ou a última da série: o campo não aparece.
30. Com pacote: cada cancelamento devolve a sessão (conferir em Pacotes > Sessões).
31. Consultas criadas antes do script 38 não pertencem a série.

## Pendente de faturamento
38. Consulta de convênio concluída (por exemplo, com liberação do administrador) e com guia ainda aguardando o faturamento: ao lado de "Concluída" aparece "Pendente de faturamento". Depois que o faturamento autoriza a guia, a etiqueta some. Guia negada ou cancelada, ou consulta particular: não aparece.
39. A mesma etiqueta aparece no cartão da consulta na agenda (semana, dia e mês), ao lado de "Concluída".

## Observação do check-in (convênio)
32. Rodar o script 39. Consulta por convênio em Check-in: aparece o campo "Observação" acima de "Iniciar atendimento".
33. Digitar um texto e clicar em Iniciar atendimento: o atendimento começa e a observação passa a aparecer como linha "Observação" no detalhe.
34. Iniciar sem texto: funciona normalmente (o campo é opcional).
35. Consulta particular em Check-in: o campo não aparece.
36. Convênio com guia pendente (botão bloqueado) e texto digitado: nada é gravado até o atendimento poder começar. Administrador liberando a regra: a observação é gravada uma vez só.
37. Mais de 500 caracteres: o campo limita a digitação.

## Cadastro pelo detalhe da consulta
40. No detalhe da consulta, o nome do paciente e o do profissional aparecem como texto comum, com o ícone de olho no canto direito da linha. O olho do paciente abre o cadastro completo (só leitura); o do profissional abre o cadastro do profissional no mesmo estilo (dados pessoais, contato, endereço, conselho, especialidades e unidades).
41. Sem a permissão "visualizar" do cadastro correspondente, o ícone de olho não aparece.

## Encaixe
42. Ao lado de "Nova consulta" existe o botão "Encaixe" (só para quem tem permissão de editar o agendamento).
43. Ele abre a mesma tela, com o título "Encaixe", sem a caixa de marcar encaixe e sem a repetição. Ao escolher o dia, aparecem só os horários livres daquele dia.
44. Ao agendar, a consulta fica marcada como encaixe (conta no limite diário de encaixes). "Nova consulta" continua sem encaixe.

## Intervalo de almoço
45. Rodar o script 40. Em "Nova consulta" de qualquer profissional, segunda a sexta, não aparecem horários que encostam em 12:00–13:00 (11:45 e 12:30 somem); o dia passa a ter 11:00 e depois 13:00.
46. Consultas já marcadas em 11:45 ou 12:30 continuam na agenda.
47. Na agenda, o bloqueio "Intervalo" aparece das 12:00 às 13:00.

## Carteirinha vencida
48. Consulta de convênio com carteirinha vencida, usuário da recepção: o botão "Iniciar atendimento" fica ativo; ao clicar, aparece o aviso "O atendimento não pode ser iniciado, pois a validade da carteirinha do convênio do paciente está vencida. Solicite autorização do administrador ou da gerência para liberação." e a consulta continua em Check-in.
49. O administrador vê a janela de justificativa (10 a 500 caracteres); ao confirmar, o atendimento inicia e a liberação fica gravada.
50. "Concluído" funciona igual: não administrador recebe o aviso de que a consulta não pode ser concluída; o administrador informa uma nova justificativa.
51. Com a validade da carteirinha atualizada no cadastro do paciente, nada disso é pedido. Particular não é afetado.
52. Confirmar o agendamento de uma consulta de convênio com carteirinha vencida: a confirmação acontece e aparece o aviso "Carteirinha vencida" orientando regularizar. Com a carteirinha em dia, nenhum aviso.
