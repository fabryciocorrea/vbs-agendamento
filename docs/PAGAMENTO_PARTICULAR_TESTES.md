# Confirmação de pagamento do particular (recepção)

## Instalação
Rodar `banco/24_PAGAMENTO_APOS_CHECKIN_V1_3.sql` no SSMS, extrair o zip sobrescrevendo os arquivos e reiniciar `dotnet run`.

## Regras
- Botão "Confirmar pagamento" no detalhe da consulta particular depois do check-in, com pagamento pendente. Exige a permissão consulta.agendamento:editar.
- Registra data, hora e usuário (RN17) e grava no log.
- Convênio: não tem o botão; o pagamento vale pela guia autorizada no Faturamento de guias.
- Já confirmado: 409 PAGAMENTO_JA_CONFIRMADO. Consulta alterada por outra pessoa: 409 CONSULTA_ALTERADA.

## Roteiro
1. Criar consulta particular sem marcar "Pagamento já recebido".
2. Confirmar o agendamento e fazer o check-in (passa sem pagamento). Tentar "Iniciar atendimento": bloqueia com "Confirme o pagamento antes de iniciar o atendimento"; o botão Confirmar pagamento só aparece no Check-in.
3. Abrir o detalhe → Confirmar pagamento → o campo Pagamento mostra "Confirmado em ...".
4. Iniciar atendimento: passa.
5. Consulta de convênio: o botão não aparece.
