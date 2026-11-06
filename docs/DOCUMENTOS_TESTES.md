# Testes - Documentos internos

1. Rodar o script 42 e reiniciar o servidor. Em Perfis de acesso > módulo Cadastros aparece "Documentos internos da empresa": liberar visualizar, incluir e excluir para os perfis que precisam (o administrador já tem tudo).
2. Menu "Documentos" (usuário com a permissão): abre a lista vazia ("Nenhum documento cadastrado").
3. "Novo documento": escolher o tipo (Folha timbrada, Termo, Contrato, Política ou norma, Formulário, Modelo de documento, Outro), título obrigatório, descrição opcional, arquivo obrigatório. Enviar um PDF fictício: aparece na lista com tipo, título, arquivo, tamanho, data e quem enviou.
4. Enviar um .docx e um .xlsx fictícios: aceitos. O ícone de olho baixa o arquivo; no PDF e nas imagens abre em outra aba.
5. Arquivo de outro formato (por exemplo .exe ou .txt) ou com a extensão trocada (um .txt renomeado para .pdf): recusado com a mensagem de formatos aceitos. Arquivo maior que 10 MB: recusado.
6. Filtros: o tipo e a busca (título, descrição ou nome do arquivo) reduzem a lista.
7. Excluir (só com a permissão de excluir): pede confirmação e apaga o arquivo.
8. Usuário sem a permissão de visualizar não vê o item no menu e a API responde 403. Sem incluir, o botão "Novo documento" não aparece; sem excluir, o ícone da lixeira não aparece.
9. Log de acessos: envio (INCLUIR), abertura (VISUALIZAR) e exclusão (EXCLUIR) da entidade DOCUMENTO_INTERNO.
