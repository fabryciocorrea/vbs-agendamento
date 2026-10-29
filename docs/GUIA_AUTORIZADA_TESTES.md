# Convênio: guia autorizada para iniciar o atendimento

## Instalação
1. SSMS: executar `banco/26_GUIA_AUTORIZADA_PARA_ATENDER_V1_3.sql`.
2. Extrair o zip sobrescrevendo os arquivos e reiniciar `dotnet run`.

## Regra
Convênio: confirmar o agendamento e fazer o check-in continuam livres. Iniciar o atendimento e concluir a consulta só com a guia AUTORIZADA (Faturamento > aba Convênio). Guia pendente ou negada: botões desabilitados na agenda e o servidor recusa (409 GUIA_NAO_AUTORIZADA). Sem exceção para o administrador.

## Roteiro
1. Consulta de convênio: Confirmar agendamento > Check-in. O botão "Iniciar atendimento" fica desabilitado ("Aguardando a autorização da guia").
2. Faturamento > Convênio: registrar a guia como Autorizada (com número).
3. Voltar à agenda: Iniciar atendimento > Concluído.
4. Registrar a guia como Negada: os botões voltam a ficar bloqueados.
