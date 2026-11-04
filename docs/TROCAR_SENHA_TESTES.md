# Trocar a própria senha (P70) – roteiro de teste

Rota: `POST /api/v1/auth/alterar-senha` (usuário logado). Tela: avatar no canto superior direito, "Alterar senha" (ou "Meu perfil", "Alterar minha senha").

Use só usuários de teste e senhas de teste.

| # | Passo | Resultado esperado |
|---|---|---|
| 1 | Entrar com um usuário de teste, abrir o avatar e clicar em "Alterar senha" | Janela com Senha atual, Nova senha e Confirmar nova senha |
| 2 | Enviar com a senha atual errada | Erro no campo "Senha atual incorreta"; a senha não muda; a tentativa fica na auditoria (EDITAR, USUARIO, "senha atual incorreta") |
| 3 | Nova senha com menos de 10 caracteres ou sem número, letra maiúscula, letra minúscula ou caractere especial | Erro com a regra da senha |
| 4 | Nova senha igual à atual | Erro "A nova senha deve ser diferente da atual." |
| 5 | Confirmação diferente da nova senha | Erro "As senhas não conferem" |
| 6 | Dados corretos | Mensagem de sucesso; continua logado nesta janela |
| 7 | Com outro navegador ou janela anônima já logada no mesmo usuário, fazer qualquer ação | A outra sessão cai pedindo novo login (sessão encerrada por SENHA_ALTERADA) |
| 8 | Sair e entrar com a senha antiga | Recusada |
| 9 | Entrar com a nova senha | Entra |
| 10 | Usuário de perfil de profissional sem profissional vinculado (RN49) | Consegue trocar a senha mesmo vendo o aviso de vínculo |
| 11 | Consulta: `SELECT TOP 5 * FROM DBO.LOG_AUDITORIA ORDER BY 1 DESC;` | Registro EDITAR em USUARIO com `campo = senha`, `origem = propria`; nenhuma senha gravada |
