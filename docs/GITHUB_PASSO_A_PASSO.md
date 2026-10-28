# Passo a passo: criar o repositório no GitHub e enviar o projeto

Pasta do projeto no seu computador: `VBS_Agendamento_Servidor` (contém `src`, `web`, `banco`, `postman`).
Todos os comandos abaixo são digitados no terminal do VS Code (menu **Terminal > New Terminal**), dentro dessa pasta.

## Antes de começar

- O repositório deve ser **privado**. O projeto é de uma empresa de saúde e o `appsettings.Development.json` cita o nome do servidor do banco.
- Nunca envie senhas, dados reais de pacientes nem arquivos `appsettings.Local.json` ou `secrets.json`. O `.gitignore` já bloqueia esses arquivos.
- **OneDrive:** a pasta atual está dentro do OneDrive. O Git cria uma pasta oculta `.git` com muitos arquivos pequenos, e o OneDrive pode
  travar ou duplicar esses arquivos. Duas formas seguras:
  1. (mais simples) manter onde está e, no OneDrive, clicar com o botão direito na pasta do projeto > **Sempre manter neste dispositivo**,
     e evitar mexer no projeto enquanto o OneDrive estiver sincronizando; ou
  2. (recomendada) depois do primeiro envio, clonar o repositório numa pasta fora do OneDrive (por exemplo `C:\Projetos\VBS_Agendamento`)
     e passar a trabalhar nela (veja a seção "Trabalhar em outra pasta ou computador").

## Passo 1 – Instalar o Git

1. Baixe o **Git for Windows** em https://git-scm.com/download/win e instale com as opções padrão.
2. Feche e abra o VS Code. No terminal, confira: `git --version`.
3. Configure seu nome e e-mail (uma vez só):
   ```
   git config --global user.name "Fabrycio Correa"
   git config --global user.email "fabrycio_correa@outlook.com"
   git config --global init.defaultBranch main
   ```
   O e-mail deve ser o mesmo cadastrado na sua conta do GitHub (ou o e-mail privado `...@users.noreply.github.com` que o GitHub oferece em Settings > Emails).

## Passo 2 – Criar a conta e o repositório no GitHub

1. Entre em https://github.com (crie a conta, se ainda não tiver; ative a verificação em duas etapas em **Settings > Password and authentication**).
2. Clique em **+** (canto superior direito) > **New repository**.
3. Preencha:
   - **Repository name:** `vbs-agendamento`
   - **Description:** Sistema de agendamento de consultas – Viver Bem Saúde
   - **Visibility:** **Private**
   - **Não marque** "Add a README file", "Add .gitignore" nem "Choose a license" (o projeto já tem esses arquivos; o repositório precisa nascer vazio).
4. Clique em **Create repository**. Na página seguinte, copie o endereço HTTPS, no formato
   `https://github.com/SEU_USUARIO/vbs-agendamento.git`.

## Passo 3 – Preparar a pasta do projeto

1. Extraia o zip desta entrega na raiz `VBS_Agendamento_Servidor`, sobrescrevendo. Ele traz `README.md`, `ARCHITECTURE.md`, `.gitignore`, `.gitattributes`,
   `docs/GITHUB_PASSO_A_PASSO.md` e `web/LEIA-ME.md`.
2. Confirme que existe a pasta `banco` na raiz com os scripts `01` a `18`. Se os scripts estiverem em outro lugar, copie-os para `banco`.
3. Opcional: copie para `docs` o documento do projeto (`.docx` ou `.pdf`) se quiser guardá-lo junto do código.
4. **Não** apague `node_modules`, `bin` ou `obj`: o Git os ignora sozinho.

## Passo 4 – Criar o repositório local e conferir o que vai subir

No terminal, na pasta `VBS_Agendamento_Servidor`:

```
git init -b main
git add .
git status
```

Confira a lista do `git status`:

- **Deve** aparecer: `README.md`, `ARCHITECTURE.md`, `.gitignore`, `src/...`, `web/...`, `banco/...`, `postman/...`, `docs/...`.
- **Não deve** aparecer: `node_modules`, `bin`, `obj`, `.vs`, arquivos `.zip`, `secrets.json`, `appsettings.Local.json`.

Se algo indevido aparecer, desfaça com `git rm -r --cached nome_da_pasta`, confira o `.gitignore` e rode `git add .` de novo.

## Passo 5 – Primeiro commit

```
git commit -m "Primeira versão do sistema de agendamento: login, cadastros e segurança"
```

Se o Git reclamar de nome ou e-mail, volte ao Passo 1 (item 3).

## Passo 6 – Conectar ao GitHub e enviar

```
git remote add origin https://github.com/SEU_USUARIO/vbs-agendamento.git
git push -u origin main
```

Na primeira vez abre uma janela do navegador para entrar no GitHub (Git Credential Manager). Entre, autorize e volte ao terminal.
Se pedir usuário e senha no terminal, use um **Personal Access Token** no lugar da senha:
GitHub > **Settings > Developer settings > Personal access tokens > Tokens (classic) > Generate new token**, com a permissão `repo`.
Guarde o token em local seguro; ele não deve ficar em nenhum arquivo do projeto.

## Passo 7 – Conferir no GitHub

1. Atualize a página do repositório. Devem aparecer as pastas `banco`, `src`, `web`, `postman`, `docs` e os arquivos `README.md` e `ARCHITECTURE.md`.
2. O README é exibido na página inicial; o diagrama do `ARCHITECTURE.md` aparece desenhado.
3. Em **Settings > General**, confirme que o repositório aparece como **Private**.
4. Procure por texto sensível: no GitHub, tecle `/` e busque por "senha" e "password" para ver se alguma senha real foi parar em algum arquivo.

## Rotina do dia a dia

Ao terminar uma fatia testada (por exemplo, "Colaboradores"):

```
git status
git add .
git commit -m "Colaboradores: cadastro com dados da carteira de trabalho (CLT)"
git push
```

Boas práticas:

- Um commit por entrega testada, com mensagem curta e clara.
- Antes de começar uma alteração arriscada, crie um ramo: `git switch -c ajuste-agenda`.
  Quando estiver testado: `git switch main`, `git merge ajuste-agenda`, `git push`.
- Para marcar versões: `git tag v0.5` e `git push origin v0.5`.
- Para ver o histórico: `git log --oneline`. Para desfazer alterações **ainda não commitadas** de um arquivo: `git restore caminho\do\arquivo`.
- Pelo VS Code também dá: ícone **Source Control** (barra lateral), escrever a mensagem, **Commit** e **Sync Changes**.

## Trabalhar em outra pasta ou computador

```
git clone https://github.com/SEU_USUARIO/vbs-agendamento.git
cd vbs-agendamento
cd web
npm install
cd ..\src\VBS.Agendamento.Api
dotnet restore
```

Depois, rode os scripts da pasta `banco` no SSMS (se for outro banco), confira a conexão em `appsettings.Development.json`
e siga o README (`dotnet run` no servidor e `npm run dev` na tela). Os arquivos ignorados (`node_modules`, `bin`, `obj`) são recriados pelos comandos acima.

## Se algo der errado

| Problema | O que fazer |
|---|---|
| `git push` recusado: "remote contains work that you do not have" | O repositório foi criado com README/licença. Rode `git pull origin main --allow-unrelated-histories`, resolva e faça `git push` |
| Subiu um arquivo que não devia (sem segredo) | `git rm --cached caminho\arquivo`, inclua no `.gitignore`, `git commit -m "Remove arquivo"` e `git push` |
| Subiu uma **senha ou chave** | Troque a senha/chave imediatamente (ela já está no histórico). Só apagar o arquivo não basta |
| Aviso "LF will be replaced by CRLF" | É normal no Windows; o `.gitattributes` já cuida disso |
| Erro de autenticação | Refaça o login pelo navegador ou gere um novo token (Passo 6) |
| OneDrive mostra conflito em `.git` | Pause a sincronização, ou mova o projeto para uma pasta fora do OneDrive (clonando de novo) |
