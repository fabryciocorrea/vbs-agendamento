# Testes do prontuário (paciente fictício)

Pré-requisitos: scripts 01 a 34 executados, `dotnet run` reiniciado, perfil com `Registros do prontuário` (visualizar/incluir; editar só para o administrador) e `Correções do prontuário` (visualizar, só administrador).

1. **Acesso (H1):** com o médico responsável, abra Pacientes > botão Prontuário. Com outro médico ou a recepção (mesmo liberado), a tela mostra a mensagem "Você não é o profissional responsável..." e o log registra a tentativa.
2. **Novo registro (médico):** Novo registro > escrever > Salvar > confirmar. O registro aparece com nome e CRM/UF do médico. Repita o mesmo texto em menos de 2 minutos: deve recusar (REGISTRO_DUPLICADO).
3. **Sem edição pelo médico:** não há botão Editar; `PUT` direto retorna 403.
4. **Administrador inclui:** escolher o profissional, preencher solicitante, autorizador e observação (mínimo 10 caracteres). O registro vem com a etiqueta "Incluído pelo administrador".
5. **Administrador edita:** Editar > novo texto + autorização. O registro mostra "Corrigido em"; Histórico traz o texto anterior e o novo. Texto igual ao atual: SEM_ALTERACAO.
6. **Paciente inativo:** não aceita novo registro (PACIENTE_INATIVO); o histórico continua legível.
7. **Troca do profissional responsável (RN16):** o médico anterior perde o acesso; o novo vê todos os registros, inclusive os antigos.
8. **Log:** conferir em `LOG_AUDITORIA` as ações VISUALIZAR, INCLUIR e EDITAR com paciente e quantidade.
9. **Banco:** `UPDATE` direto na conta da aplicação deve falhar; correção pela TI exige o contexto de sessão do script 08.
10. **Acesso pelo cadastro:** em Pacientes, o botão Prontuário (ícone de documento) abre o prontuário. O médico vê só os pacientes dele; a recepção, mesmo liberada, recebe a mensagem de acesso negado ao abrir.
11. **Atalho na Agenda:** na consulta em atendimento ou concluída, o botão Prontuário abre o prontuário com o novo registro já vinculado à consulta. Consulta de outro profissional ou de outro paciente: CONSULTA_INVALIDA.
12. **Documento de autorização:** como administrador, editar um registro: escolher o profissional que solicitou na lista, digitar o responsável legal, anexar PDF/JPG/PNG (até 5 MB) e escrever a observação. Todos os campos são obrigatórios; sem o documento a tela não deixa salvar. Arquivo de outro tipo (por exemplo .exe renomeado para .pdf) é recusado.
13. **Download:** no histórico e na etiqueta de inclusão pelo administrador, o botão Documento baixa o arquivo (só quem tem Correções do prontuário). O acesso aparece no `LOG_AUDITORIA`.
14. **Imutabilidade:** `UPDATE` ou `DELETE` em `PRONTUARIO_AUTORIZACAO_DOC` deve falhar (erro 50067). Correção direta pela TI (origem BANCO) continua possível sem documento.
15. **Médico:** não vê os campos de autorização nem o botão Editar.
16. **Etiqueta:** registro incluído pelo administrador mostra "Incluído por <nome do administrador>"; passar o mouse mostra quem solicitou e quem autorizou.
17. **Transferência (administrador):** na tela do prontuário ou na tela do prontuário, botão Transferência de prontuário > novo profissional + motivo (mínimo 10 caracteres) > confirmar. O responsável do paciente muda; os registros antigos continuam com o nome de quem escreveu.
18. **Perda de acesso (RN16):** depois da transferência, o médico anterior não abre mais o prontuário (acesso negado) e o novo médico vê todos os registros, inclusive os antigos.
19. **Histórico:** em Pacientes > editar > histórico de profissionais, a transferência aparece com data, usuário e motivo. O log tem o evento EDITAR do paciente com origem "transferencia-prontuario".
20. **Permissão:** o botão só aparece para perfis com "Transferência de prontuário" (Editar) marcada; sem a liberação, a API responde 403. Profissional já responsável como destino: PROFISSIONAL_JA_RESPONSAVEL.

## Ajustes da transferência
21. No cadastro do paciente, o botão agora se chama **Transferir prontuário** e só aparece com a permissão "Transferência de prontuário" (Editar). Ele abre a mesma janela da tela do prontuário, com motivo obrigatório (mínimo 10 caracteres). A troca aparece em "Ver histórico" com o motivo.
22. Usuário da recepção com a permissão de transferência (e sem ser o médico responsável) consegue transferir pelo cadastro. Sem a permissão, o botão não aparece e a API devolve 403.
23. Paciente com consulta futura (agendada ou confirmada) com o profissional anterior: depois da transferência aparece um aviso com a quantidade. As consultas não mudam; remarque ou cancele na Agenda.
24. O profissional anterior deixa de ver o prontuário; o novo vê todos os registros.
