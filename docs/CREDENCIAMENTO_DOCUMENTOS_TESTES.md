# Testes - Documentos do credenciamento

1. Rodar o script 43 e reiniciar o servidor. Em Perfis de acesso > Cadastros aparece "Documentos do credenciamento": liberar visualizar, incluir e excluir (o administrador já tem tudo).
2. Credenciamentos: na coluna Ações, o ícone de clipe "Anexar documentos" abre a janela "Documentos - convênio / unidade / especialidade".
3. Enviar um PDF fictício com o tipo Protocolo: aparece na lista com tipo, descrição, arquivo, tamanho, data e quem enviou. O tipo "Outro" exige descrição.
4. .docx e .xlsx fictícios são aceitos; PDF e imagem abrem em outra aba, Word e Excel baixam. Outro formato, extensão trocada ou arquivo acima de 10 MB são recusados.
5. Excluir (com permissão de excluir) pede confirmação e apaga o arquivo. Máximo de 30 documentos por credenciamento.
6. Sem a permissão de visualizar, o clipe não aparece e a API responde 403. Sem incluir, o formulário de envio não aparece; sem excluir, a lixeira não aparece.
7. Excluir um credenciamento (administrador) apaga também os documentos dele.
8. Log de acessos: INCLUIR, VISUALIZAR e EXCLUIR da entidade CREDENCIAMENTO_DOCUMENTO.
