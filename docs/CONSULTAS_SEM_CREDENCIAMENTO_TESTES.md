# Consultas sem credenciamento: roteiro de testes

Antes: reiniciar o `dotnet run`. A tela usa a permissão de visualizar "Agendamento de consultas".

1. Agende uma consulta de convênio para uma data futura com credenciamento APROVADO e vigente. Ela não aparece na tela.
2. Em Credenciamentos, suspenda o credenciamento (ou mude para "Em análise", ou ponha data de fim anterior à consulta). A consulta passa a aparecer. O aviso do sino (tipo Credenciamento) abre esta tela.
3. Os filtros de unidade e convênio reduzem a lista. A lista vai da consulta mais próxima para a mais distante.
4. Consulta cancelada, concluída, em atendimento, passada ou particular não aparece.
5. Consulta liberada pelo administrador (liberação de credenciamento) não aparece.
6. Reaprove o credenciamento: a consulta sai da lista sozinha.
7. Usuário médico: vê só as próprias consultas. Nada é cancelado ou alterado por esta tela.
