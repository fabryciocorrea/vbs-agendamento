using Dapper;
using Microsoft.Extensions.Options;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Auth;

public sealed class AuthService
{
    private readonly IConexaoFactory _db;
    private readonly HashSenha _hash;
    private readonly SegurancaOpcoes _seguranca;
    private readonly IAuditoria _auditoria;
    private readonly IPermissaoService _permissoes;
    private readonly IEnviadorEmail _email;

    public AuthService(
        IConexaoFactory db,
        HashSenha hash,
        IOptions<SegurancaOpcoes> seguranca,
        IAuditoria auditoria,
        IPermissaoService permissoes,
        IEnviadorEmail email)
    {
        _db = db;
        _hash = hash;
        _seguranca = seguranca.Value;
        _auditoria = auditoria;
        _permissoes = permissoes;
        _email = email;
    }

    private sealed class UsuarioLogin
    {
        public int Id { get; set; }
        public string NomeUsuario { get; set; } = string.Empty;
        public string NomeCompleto { get; set; } = string.Empty;
        public string SenhaHash { get; set; } = string.Empty;
        public bool Ativo { get; set; }
        public int Tentativas { get; set; }
        public DateTime? Bloqueio { get; set; }
        public int PerfilId { get; set; }
        public string PerfilNome { get; set; } = string.Empty;
    }

    private sealed class Bloqueio
    {
        public int Tentativas { get; set; }
        public DateTime? DataBloqueio { get; set; }
    }

    // ------------------------------------------------------------------ login
    public async Task<SessaoSaida> LoginAsync(LoginEntrada? entrada, string? ip, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        var nome = entrada?.NomeUsuario?.Trim();
        if (string.IsNullOrEmpty(nome)) erros.Add(new ErroCampo("nomeUsuario", "Campo Não Preenchido"));
        else if (nome.Length > 60) erros.Add(new ErroCampo("nomeUsuario", "Máximo de 60 caracteres"));
        if (string.IsNullOrEmpty(entrada?.Senha)) erros.Add(new ErroCampo("senha", "Campo Não Preenchido"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        await using var con = await _db.AbrirAsync(ct);

        var u = await con.QueryFirstOrDefaultAsync<UsuarioLogin>(Sql.Cmd(@"
SELECT U.CODIGO_USUARIO AS Id, U.NOME_USUARIO AS NomeUsuario, U.NOME_COMPLETO AS NomeCompleto,
       U.SENHA_HASH AS SenhaHash, U.ATIVO AS Ativo, CAST(U.TENTATIVAS_FALHAS AS INT) AS Tentativas,
       U.DATA_BLOQUEIO AS Bloqueio, U.CODIGO_PERFIL AS PerfilId, P.NOME AS PerfilNome
FROM DBO.USUARIO U
JOIN DBO.PERFIL_ACESSO P ON P.CODIGO_PERFIL = U.CODIGO_PERFIL
WHERE U.NOME_USUARIO = @Nome;", new { Nome = nome }, ct: ct));

        // Usuário inexistente ou inativo: mesma resposta genérica, e o mesmo custo de tempo.
        if (u is null || !u.Ativo)
        {
            _hash.Verificar(entrada!.Senha!, _hash.HashFalso);
            await _auditoria.RegistrarAsync("LOGIN_FALHA", "USUARIO", u?.Id, null, u?.Id,
                new { nomeUsuario = nome, motivo = u is null ? "inexistente" : "inativo" }, ct: ct);
            throw Erro.NaoAutenticado();
        }

        if (u.Bloqueio is not null) throw UsuarioBloqueado();

        if (!_hash.Verificar(entrada!.Senha!, u.SenhaHash))
        {
            var estado = await con.QueryFirstAsync<Bloqueio>(Sql.Cmd(@"
UPDATE DBO.USUARIO
   SET TENTATIVAS_FALHAS = TENTATIVAS_FALHAS + 1,
       DATA_BLOQUEIO = CASE WHEN TENTATIVAS_FALHAS + 1 >= @Max THEN SYSUTCDATETIME() ELSE DATA_BLOQUEIO END
 WHERE CODIGO_USUARIO = @Id;
SELECT CAST(TENTATIVAS_FALHAS AS INT) AS Tentativas, DATA_BLOQUEIO AS DataBloqueio
FROM DBO.USUARIO WHERE CODIGO_USUARIO = @Id;",
                new { Id = u.Id, Max = _seguranca.MaxTentativasLogin }, ct: ct));

            await _auditoria.RegistrarAsync("LOGIN_FALHA", "USUARIO", u.Id, null, u.Id,
                new { nomeUsuario = nome, motivo = "senha", tentativas = estado.Tentativas }, ct: ct);

            if (estado.DataBloqueio is not null)
            {
                // D5: ao bloquear, as sessões abertas caem na hora.
                await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.SESSAO_USUARIO SET DATA_ENCERRAMENTO = SYSUTCDATETIME(), MOTIVO_ENCERRAMENTO = 'USUARIO_BLOQUEADO'
WHERE CODIGO_USUARIO = @Id AND DATA_ENCERRAMENTO IS NULL;", new { Id = u.Id }, ct: ct));
                throw UsuarioBloqueado();
            }

            throw Erro.NaoAutenticado();
        }

        // Senha correta: zera o contador, registra o login e abre a sessão.
        var token = Tokens.Gerar();
        await using (var tx = (Microsoft.Data.SqlClient.SqlTransaction)await con.BeginTransactionAsync(ct))
        {
            await con.ExecuteAsync(Sql.Cmd(
                "UPDATE DBO.USUARIO SET TENTATIVAS_FALHAS = 0, DATA_ULTIMO_LOGIN = SYSUTCDATETIME() WHERE CODIGO_USUARIO = @Id;",
                new { Id = u.Id }, tx, ct));

            await con.ExecuteAsync(Sql.Cmd(
                "INSERT INTO DBO.SESSAO_USUARIO (CODIGO_USUARIO, TOKEN_HASH, IP) VALUES (@Id, @Hash, @Ip);",
                new { Id = u.Id, Hash = Tokens.Hash(token), Ip = ip }, tx, ct));

            await _auditoria.RegistrarAsync("LOGIN", "USUARIO", u.Id, null, u.Id, null, con, tx, ct);
            await tx.CommitAsync(ct);
        }

        return new SessaoSaida
        {
            AccessToken = token,
            TokenType = "Bearer",
            ExpiresIn = _seguranca.InatividadeMinutos * 60,
            Usuario = new UsuarioResumoDto
            {
                Id = u.Id,
                NomeUsuario = u.NomeUsuario,
                NomeCompleto = u.NomeCompleto,
                Perfil = new PerfilReferenciaDto { Id = u.PerfilId, Nome = u.PerfilNome }
            }
        };
    }

    private static ApiException UsuarioBloqueado() =>
        new(423, "Usuário bloqueado", "Usuário bloqueado por excesso de tentativas. Peça o desbloqueio ao administrador.");

    // ----------------------------------------------------------------- logout
    public async Task LogoutAsync(UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.SESSAO_USUARIO SET DATA_ENCERRAMENTO = SYSUTCDATETIME(), MOTIVO_ENCERRAMENTO = 'LOGOUT'
WHERE CODIGO_SESSAO = @Id AND DATA_ENCERRAMENTO IS NULL;", new { Id = usuario.SessaoId }, ct: ct));
    }

    // --------------------------------------------------------------- /auth/eu
    public async Task<UsuarioAtualSaida> ObterAtualAsync(UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var u = await con.QueryFirstOrDefaultAsync<UsuarioLogin>(Sql.Cmd(@"
SELECT U.CODIGO_USUARIO AS Id, U.NOME_USUARIO AS NomeUsuario, U.NOME_COMPLETO AS NomeCompleto,
       U.SENHA_HASH AS SenhaHash, U.ATIVO AS Ativo, 0 AS Tentativas, U.DATA_BLOQUEIO AS Bloqueio,
       U.CODIGO_PERFIL AS PerfilId, P.NOME AS PerfilNome
FROM DBO.USUARIO U JOIN DBO.PERFIL_ACESSO P ON P.CODIGO_PERFIL = U.CODIGO_PERFIL
WHERE U.CODIGO_USUARIO = @Id;", new { Id = usuario.Id }, ct: ct))
            ?? throw Erro.NaoAutenticado("Usuário não encontrado.");

        return new UsuarioAtualSaida
        {
            Usuario = new UsuarioResumoDto
            {
                Id = u.Id,
                NomeUsuario = u.NomeUsuario,
                NomeCompleto = u.NomeCompleto,
                Perfil = new PerfilReferenciaDto { Id = u.PerfilId, Nome = u.PerfilNome }
            },
            ProfissionalId = usuario.ProfissionalId,
            Permissoes = await _permissoes.ListarAsync(usuario.PerfilId, usuario.Administrador, ct)
        };
    }

    // ------------------------------------------------------- recuperar senha
    /// <summary>Resposta é sempre a mesma, exista ou não o e-mail (não revela quem tem conta).</summary>
    public async Task RecuperarSenhaAsync(RecuperarSenhaEntrada? entrada, CancellationToken ct)
    {
        var email = entrada?.Email?.Trim();
        if (string.IsNullOrEmpty(email)) throw Erro.Validacao("email", "Campo Não Preenchido");
        if (email.Length > 150 || !Formatos.Email.IsMatch(email)) throw Erro.Validacao("email", "E-mail inválido");

        await using var con = await _db.AbrirAsync(ct);
        var usuarioId = await con.ExecuteScalarAsync<int?>(Sql.Cmd(
            "SELECT CODIGO_USUARIO FROM DBO.USUARIO WHERE EMAIL = @Email AND ATIVO = 1;", new { Email = email }, ct: ct));
        if (usuarioId is null) return;

        var token = Tokens.Gerar();
        await con.ExecuteAsync(Sql.Cmd(@"
INSERT INTO DBO.TOKEN_RECUPERACAO_SENHA (CODIGO_USUARIO, TOKEN_HASH, DATA_EXPIRA)
VALUES (@Id, @Hash, DATEADD(MINUTE, @Minutos, SYSUTCDATETIME()));",
            new { Id = usuarioId, Hash = Tokens.Hash(token), Minutos = _seguranca.ValidadeTokenRecuperacaoMinutos }, ct: ct));

        var link = _seguranca.UrlRedefinicaoSenha.Replace("{token}", Uri.EscapeDataString(token));
        await _email.EnviarAsync(email, "Recuperação de senha - Viver Bem Saúde",
            $"Use o link para criar uma nova senha (vale por {_seguranca.ValidadeTokenRecuperacaoMinutos} minutos e só pode ser usado uma vez): {link}", ct);
    }

    // ------------------------------------------------------ redefinir senha
    public async Task RedefinirSenhaAsync(RedefinirSenhaEntrada? entrada, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (string.IsNullOrWhiteSpace(entrada?.Token)) erros.Add(new ErroCampo("token", "Campo Não Preenchido"));
        if (string.IsNullOrEmpty(entrada?.NovaSenha)) erros.Add(new ErroCampo("novaSenha", "Campo Não Preenchido"));
        else if (!Formatos.Senha.IsMatch(entrada.NovaSenha))
            erros.Add(new ErroCampo("novaSenha", "A senha deve ter de 10 a 128 caracteres, com letras, números e pelo menos um caractere especial."));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await con.BeginTransactionAsync(ct);

        var tokenId = await con.ExecuteScalarAsync<int?>(Sql.Cmd(@"
SELECT CODIGO_TOKEN FROM DBO.TOKEN_RECUPERACAO_SENHA WITH (UPDLOCK)
WHERE TOKEN_HASH = @Hash AND DATA_TROCA_SENHA IS NULL AND DATA_EXPIRA > SYSUTCDATETIME();",
            new { Hash = Tokens.Hash(entrada!.Token!.Trim()) }, tx, ct));

        if (tokenId is null)
            throw new ApiException(410, "Token inválido", "Token inválido, expirado ou já utilizado. Peça um novo link de recuperação.");

        var usuarioId = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT CODIGO_USUARIO FROM DBO.TOKEN_RECUPERACAO_SENHA WHERE CODIGO_TOKEN = @Id;", new { Id = tokenId }, tx, ct));

        // Não mexe em DATA_BLOQUEIO: só o administrador desbloqueia (D5).
        await con.ExecuteAsync(Sql.Cmd(
            "UPDATE DBO.USUARIO SET SENHA_HASH = @Hash, DATA_ATUALIZACAO = SYSUTCDATETIME() WHERE CODIGO_USUARIO = @Id;",
            new { Id = usuarioId, Hash = _hash.Gerar(entrada.NovaSenha!) }, tx, ct));

        await con.ExecuteAsync(Sql.Cmd(
            "UPDATE DBO.TOKEN_RECUPERACAO_SENHA SET DATA_TROCA_SENHA = SYSUTCDATETIME() WHERE CODIGO_TOKEN = @Id;",
            new { Id = tokenId }, tx, ct));

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.SESSAO_USUARIO SET DATA_ENCERRAMENTO = SYSUTCDATETIME(), MOTIVO_ENCERRAMENTO = 'SENHA_ALTERADA'
WHERE CODIGO_USUARIO = @Id AND DATA_ENCERRAMENTO IS NULL;", new { Id = usuarioId }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "USUARIO", usuarioId, null, usuarioId,
            new { campo = "senha", origem = "recuperacao" }, con, tx, ct);

        await tx.CommitAsync(ct);
    }
}
