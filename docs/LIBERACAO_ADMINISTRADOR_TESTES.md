# Liberação do administrador - instalação e testes

## Instalação
1. SSMS: executar `banco/27_LIBERACAO_ADMINISTRADOR_V1_3.sql` (substitui o script 26; se o 26 ainda não foi executado, pode rodar só o 27).
2. Extrair o zip sobrescrevendo os arquivos e reiniciar `dotnet run`.
3. Sair do sistema e entrar de novo (o front passa a saber que o usuário é administrador).

## Regra
As telas de ação não mostram nenhum campo de justificativa. Quando uma regra operacional bloqueia a operação de um **administrador**, abre uma janela pequena só com o campo **Justificativa da liberação** (10 a 500 caracteres) e o motivo do bloqueio em uma linha. Ao clicar em Liberar, a operação é refeita com a liberação; em Voltar, nada muda.

A justificativa fica na consulta (detalhe: "Liberação do administrador"), no histórico de status e no log; na aba Convênio do Faturamento a consulta aparece com "Liberada pelo administrador". Os outros perfis continuam com os bloqueios, sem a janela.

Regras que o administrador pode liberar:
- Convênio: iniciar atendimento ou concluir sem guia autorizada (GUIA_NAO_AUTORIZADA).
- Particular: iniciar atendimento sem pagamento confirmado (PAGAMENTO_PENDENTE).
- Cancelamento pelo paciente fora da antecedência (CANCELAMENTO_FORA_DO_PRAZO).
- Falta antes do horário e conclusão antes do horário.
- Pular etapas do fluxo (por exemplo, concluir direto, iniciar sem check-in) e cancelar consulta em atendimento.
- Confirmar pagamento fora do check-in.
- Agendar/remarcar em horário no passado, feriado, recesso, fora do expediente ou no intervalo (em Nova consulta, marque "Encaixe" para informar data e hora livres).
- Convênio sem credenciamento, convênio diferente do cadastro do paciente, convênio inativo e carteirinha vencida (Nova consulta). Para o administrador, o campo Convênio é uma lista com todos os convênios; os demais perfis veem só o convênio do paciente.

Continuam valendo para todos (integridade dos dados):
- Consulta em status final (Concluída, Cancelada, Falta, Remarcada) não muda de status.
- Dois atendimentos no mesmo horário do mesmo profissional (para isso existe o encaixe) e o limite de encaixes do dia.
- O log de acessos só recebe inclusão; o perfil Administrador não perde as próprias liberações.

## Roteiro
1. Consulta de convênio com guia pendente: Confirmar agendamento > Check-in > Iniciar atendimento: o sistema bloqueia e abre a janela da justificativa; informe e clique em Liberar; o status vira Em atendimento e o detalhe mostra a liberação.
2. Faturamento > Convênio: a consulta aparece com "Liberada pelo administrador".
3. Consulta futura: Concluído (admin) > liberação > Concluída.
4. Nova consulta com Encaixe em data passada: clique em Agendar, informe a justificativa na janela que abrir e libere.
5. Perfil não administrador: os mesmos bloqueios continuam e a janela de justificativa não aparece.
