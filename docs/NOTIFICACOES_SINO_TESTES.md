# Sino de notificações: roteiro de testes

Antes: reiniciar o `dotnet run`, marcar **Visualizar** (e **Editar**, para marcar como lido) em "Notificações internas" no perfil e entrar de novo.

1. Com a permissão, o sino aparece na barra, ao lado do nome. Sem Visualizar, o sino não aparece.
2. Gere um aviso: cancele uma consulta futura com paciente aguardando na lista de espera (mesmo profissional ou especialidade). Em até 1 minuto o contador do sino sobe; abrindo a tela, ele atualiza no mesmo instante ao abrir a gaveta.
3. A gaveta mostra o tipo (etiqueta), a data e hora, a mensagem e, quando há, o paciente e a data da consulta.
4. "Lido" tira o aviso de "Não lidos" e o contador desce. Em "Todos" ele continua, com "Lido por <nome> em <data>".
5. Entre com outro usuário: o aviso já aparece como lido, com o nome de quem leu. Marcar de novo não muda o nome nem a hora.
6. "Abrir" marca como lido e leva à tela certa: horário liberado vai para a Lista de espera, credenciamento para Credenciamentos, os demais para a Agenda.
7. Usuário sem Editar: vê os avisos e os botões "Lido" não aparecem.
8. Usuário médico: só vê avisos dirigidos a ele (os da recepção, para todos, não aparecem).
9. No log: EDITAR `NOTIFICACAO_INTERNA` a cada primeira leitura.
