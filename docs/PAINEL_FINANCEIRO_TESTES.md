# Painel financeiro: roteiro de testes

Use só pacientes fictícios. Depois de atualizar o servidor, reinicie o `dotnet run`. Quem muda de perfil precisa entrar de novo.

## Acesso
1. No perfil de teste, marque **Painel financeiro: Visualizar**. Entre de novo: o item "Painel financeiro" aparece no menu.
2. Sem a permissão, o item não aparece e a rota mostra "sem permissão".
3. Usuário vinculado a médico: a API responde 403 mesmo com a permissão.

## Números (use o mês atual)
1. Conclua duas consultas particulares (uma com pagamento confirmado, outra sem) e uma de convênio com guia autorizada.
2. **Faturado** = soma das três; **Ticket médio** = faturado dividido por 3.
3. **Particular recebido** só a confirmada; **a confirmar** a outra. **Convênio autorizado** a de convênio.
4. Deixe uma consulta agendada: o valor aparece em **Previsto**, não em Faturado.
5. Cancele uma consulta: não entra em nenhum total.
6. Compare com o resumo da tela de Faturamento no mesmo período: o faturado deve bater.

## Filtros
1. **Dia**, **Semana** e **Mês** mudam as datas. Mexer em uma data deixa o período personalizado.
2. Período maior que 62 dias: o gráfico passa a ser por mês. Maior que 366 dias: aviso do servidor.
3. Filtrar por unidade, profissional e tipo (Convênio/Particular) muda todos os quadros juntos.

## Quadros
1. "Por convênio e particular": porcentagens somam 100% e as colunas Autorizado, Pendente e Negado só aparecem para convênio.
2. "Por especialidade" e "Por profissional" ordenados do maior valor para o menor.
