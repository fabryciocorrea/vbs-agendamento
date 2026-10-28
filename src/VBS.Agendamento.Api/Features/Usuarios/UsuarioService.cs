using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Auth;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Usuarios;

/// <summary>
/// Cadastro de usuários (administrador ou quem tem a liberação; não há autocadastro).
/// Regras: nome de usuário e e-mail únicos (409), um profissional só tem um usuário (409), RN14 (inativar, não excluir),
/// inativar, bloquear ou trocar a senha encerram as sessões abertas, só o administrador mexe em usuário do perfil Administrador.
/// </summary>
public sealed class UsuarioService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private static readonly Regex FormatoNomeUsuario = new(@"^[a-z0-9]+(\.[a-z0-9]+)+$", RegexOptions.Compiled);

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;
    private readonly HashSenha _hash;

    public UsuarioService(IConexaoFactory db, IAuditoria auditoria, HashSenha hash)
    {
        _db = db;
        _auditoria = auditoria;
        _hash = hash;
    }

    private const string SelectUsuario = @"
SELECT U.CODIGO_USUARIO AS Id, U.NOME_USUARIO AS NomeUsuario, U.NOME_COMPLETO AS NomeCompleto, U.EMAIL AS Email,
       U.CARGO AS Cargo, U.ATIVO AS Ativo, U.CODIGO_PERFIL AS PerfilId, P.NOME AS PerfilNome, P.PROTEGIDO AS PerfilProtegido,
       U.CODIGO_PROFISSIONAL AS ProfissionalId, PR.NOME AS ProfissionalNome,
       U.DATA_BLOQUEIO AS BloqueadoEm, U.DATA_ULTIMO_LOGIN AS UltimoLoginEm,
       U.DATA_CRIACAO AS CriadoEm, U.CRIACAO_USUARIO AS CriadoPorId, UC.NOME_COMPLETO AS CriadoPorNome,
       U.DATA_ATUALIZACAO AS AtualizadoEm, U.ATUALIZACAO_USUARIO AS AtualizadoPorId, UA.NOME_COMPLETO AS AtualizadoPorNome
FROM DBO.USUARIO U
JOIN DBO.PERFIL_ACESSO P ON P.CODIGO_PERFIL = U.CODIGO_PERFIL
LEFT JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = U.CODIGO_PROFISSIONAL
LEFT JOIN DBO.USUARIO UC ON UC.CODIGO_USUARIO = U.CRIACAO_USUARIO
LEFT JOIN DBO.USUARIO UA ON UA.CODIGO_USUARIO = U.ATUALIZACAO_USUARIO";

    private sealed class Linha
    {
        public int Id { get; set; }
        public string NomeUsuario { get; set; } = string.Empty;
        public string NomeCompleto { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Cargo { get; set; } = string.Empty;
        public bool Ativo { get; set; }
        public int PerfilId { get; set; }
        public string PerfilNome { get; set; } = string.Empty;
        public bool PerfilProtegido { get; set; }
        public int? ProfissionalId { get; set; }
        public string? ProfissionalNome { get; set; }
        public DateTime? BloqueadoEm { get; set; }
        public DateTime? UltimoLoginEm { get; set; }
        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }
        public string? CriadoPorNome { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public int? AtualizadoPorId { get; set; }
        public string? AtualizadoPorNome { get; set; }
    }

    private sealed class DadosValidados
    {
        public string NomeUsuario { get; set; } = string.Empty;
        public string NomeCompleto { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Cargo { get; set; } = string.Empty;
        public int PerfilId { get; set; }
        public int? ProfissionalId { get; set; }
    }

    private sealed class ProfissionalInfo
    {
        public int Id { get; set; }
        public bool Ativo { get; set; }
    }

    private sealed class PerfilInfo
    {
        public int Id { get; set; }
        public bool Protegido { get; set; }
    }

    // ---------------------------------------------------------------- lista e consulta
    public async Task<UsuarioPaginaDto> ListarAsync(FiltroUsuarios f, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            onde.Append(" AND (U.NOME_USUARIO LIKE @Busca OR U.NOME_COMPLETO LIKE @Busca OR U.EMAIL LIKE @Busca)");
            p.Add("Busca", "%" + Formatos.EscaparLike(f.Busca.Trim()) + "%");
        }
        if (f.PerfilId.HasValue)
        {
            onde.Append(" AND U.CODIGO_PERFIL = @PerfilId");
            p.Add("PerfilId", f.PerfilId.Value);
        }
        if (f.Ativo.HasValue)
        {
            onde.Append(" AND U.ATIVO = @Ativo");
            p.Add("Ativo", f.Ativo.Value);
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.USUARIO U" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(
            SelectUsuario + onde + " ORDER BY U.NOME_COMPLETO, U.CODIGO_USUARIO OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new UsuarioPaginaDto { Itens = linhas.Select(ParaDto).ToList(), Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total };
    }

    public async Task<UsuarioDto> ObterAsync(int id, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var linha = await CarregarAsync(con, null, id, ct) ?? throw Erro.NaoEncontrado("Usuário não encontrado.");
        return ParaDto(linha);
    }

    // ---------------------------------------------------------------- criar
    public async Task<UsuarioDto> CriarAsync(UsuarioEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        var dados = ValidarDados(d, erros);
        if (string.IsNullOrEmpty(d?.Senha)) erros.Add(new ErroCampo("senha", NaoPreenchido));
        else if (!Formatos.Senha.IsMatch(d.Senha))
            erros.Add(new ErroCampo("senha", "Mínimo de 10 caracteres, com letras, números e pelo menos um caractere especial"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await ConferirReferenciasAsync(con, tx, dados!, null, usuario, ct);
        await ConferirDuplicidadesAsync(con, tx, dados!, null, ct);

        int id;
        try
        {
            id = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.USUARIO (CODIGO_PERFIL, CODIGO_PROFISSIONAL, NOME_USUARIO, SENHA_HASH, NOME_COMPLETO, EMAIL, CARGO, ATIVO,
                         CRIACAO_USUARIO, ATUALIZACAO_USUARIO)
VALUES (@PerfilId, @ProfissionalId, @NomeUsuario, @Hash, @NomeCompleto, @Email, @Cargo, @Ativo, @Usuario, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                new
                {
                    PerfilId = dados!.PerfilId,
                    ProfissionalId = dados.ProfissionalId,
                    NomeUsuario = dados.NomeUsuario,
                    Hash = _hash.Gerar(d!.Senha!),
                    NomeCompleto = dados.NomeCompleto,
                    Email = dados.Email,
                    Cargo = dados.Cargo,
                    Ativo = d.Ativo ?? true,
                    Usuario = usuario.Id
                }, tx, ct));
        }
        catch (SqlException ex) when (TraduzirDuplicidade(ex) is { } conflito)
        {
            throw conflito;
        }

        // A senha nunca vai para o log.
        await _auditoria.RegistrarAsync("INCLUIR", "USUARIO", id, null, usuario.Id,
            new { nomeUsuario = dados.NomeUsuario, perfilId = dados.PerfilId, profissionalId = dados.ProfissionalId }, con, tx, ct);

        var linha = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Usuário recém-criado não encontrado.");
        await tx.CommitAsync(ct);
        return ParaDto(linha);
    }

    // ---------------------------------------------------------------- atualizar
    public async Task<UsuarioDto> AtualizarAsync(int id, UsuarioAtualizacao? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        var dados = ValidarDados(d, erros);
        if (erros.Count > 0) throw Erro.Validacao(erros);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Usuário não encontrado.");
        ConferirAcessoAoUsuarioProtegido(atual, usuario);

        var perfilNovo = await ConferirReferenciasAsync(con, tx, dados!, atual, usuario, ct);

        // Não deixa o sistema sem administrador ativo.
        if (atual.PerfilProtegido && atual.Ativo && !perfilNovo.Protegido)
            await ConferirOutroAdministradorAsync(con, tx, id, ct);

        await ConferirDuplicidadesAsync(con, tx, dados!, id, ct);

        try
        {
            await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.USUARIO
   SET NOME_USUARIO = @NomeUsuario, NOME_COMPLETO = @NomeCompleto, EMAIL = @Email, CARGO = @Cargo,
       CODIGO_PERFIL = @PerfilId, CODIGO_PROFISSIONAL = @ProfissionalId,
       DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
 WHERE CODIGO_USUARIO = @Id;",
                new
                {
                    NomeUsuario = dados!.NomeUsuario,
                    NomeCompleto = dados.NomeCompleto,
                    Email = dados.Email,
                    Cargo = dados.Cargo,
                    PerfilId = dados.PerfilId,
                    ProfissionalId = dados.ProfissionalId,
                    Usuario = usuario.Id,
                    Id = id
                }, tx, ct));
        }
        catch (SqlException ex) when (TraduzirDuplicidade(ex) is { } conflito)
        {
            throw conflito;
        }

        var alterados = new List<string>();
        void Dif(string campo, object? antes, object? depois) { if (!Equals(antes, depois)) alterados.Add(campo); }
        Dif("nomeUsuario", atual.NomeUsuario, dados.NomeUsuario);
        Dif("nomeCompleto", atual.NomeCompleto, dados.NomeCompleto);
        Dif("email", atual.Email, dados.Email);
        Dif("cargo", atual.Cargo, dados.Cargo);
        Dif("perfilId", atual.PerfilId, dados.PerfilId);
        Dif("profissionalId", atual.ProfissionalId, dados.ProfissionalId);
        await _auditoria.RegistrarAsync("EDITAR", "USUARIO", id, null, usuario.Id, new { campos = alterados }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Usuário não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // ---------------------------------------------------------------- ativar / inativar
    public async Task<UsuarioDto> AlterarStatusAsync(int id, StatusEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d?.Ativo is null) throw Erro.Validacao("ativo", NaoPreenchido);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Usuário não encontrado.");
        ConferirAcessoAoUsuarioProtegido(atual, usuario);

        if (!d.Ativo.Value)
        {
            if (id == usuario.Id)
                throw Erro.Conflito("Você não pode inativar o seu próprio usuário.", "USUARIO_PROPRIO");
            if (atual.PerfilProtegido && atual.Ativo)
                await ConferirOutroAdministradorAsync(con, tx, id, ct);
        }

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.USUARIO SET ATIVO = @Ativo, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_USUARIO = @Id;", new { Ativo = d.Ativo.Value, Usuario = usuario.Id, Id = id }, tx, ct));

        if (!d.Ativo.Value) await EncerrarSessoesAsync(con, tx, id, "USUARIO_INATIVADO", ct);

        await _auditoria.RegistrarAsync("EDITAR", "USUARIO", id, null, usuario.Id,
            new { campos = new[] { "ativo" }, ativo = d.Ativo.Value }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Usuário não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // ---------------------------------------------------------------- desbloquear (D5)
    public async Task DesbloquearAsync(int id, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Usuário não encontrado.");
        ConferirAcessoAoUsuarioProtegido(atual, usuario);

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.USUARIO SET TENTATIVAS_FALHAS = 0, DATA_BLOQUEIO = NULL, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_USUARIO = @Id;", new { Usuario = usuario.Id, Id = id }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "USUARIO", id, null, usuario.Id, new { campo = "desbloqueio" }, con, tx, ct);
        await tx.CommitAsync(ct);
    }

    // ---------------------------------------------------------------- nova senha definida pelo administrador
    public async Task RedefinirSenhaAsync(int id, SenhaEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(d?.NovaSenha)) throw Erro.Validacao("novaSenha", NaoPreenchido);
        if (!Formatos.Senha.IsMatch(d.NovaSenha))
            throw Erro.Validacao("novaSenha", "Mínimo de 10 caracteres, com letras, números e pelo menos um caractere especial");

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Usuário não encontrado.");
        ConferirAcessoAoUsuarioProtegido(atual, usuario);

        // Não desbloqueia: só o botão de desbloquear faz isso (D5).
        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.USUARIO SET SENHA_HASH = @Hash, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_USUARIO = @Id;", new { Hash = _hash.Gerar(d.NovaSenha), Usuario = usuario.Id, Id = id }, tx, ct));

        await EncerrarSessoesAsync(con, tx, id, "SENHA_ALTERADA", ct);
        await _auditoria.RegistrarAsync("EDITAR", "USUARIO", id, null, usuario.Id,
            new { campo = "senha", origem = "administrador" }, con, tx, ct);
        await tx.CommitAsync(ct);
    }

    // =========================================================== apoio
    private static DadosValidados? ValidarDados(UsuarioAtualizacao? d, List<ErroCampo> erros)
    {
        if (d is null)
        {
            erros.Add(new ErroCampo("corpo", "Corpo da requisição inválido"));
            return null;
        }

        var nomeUsuario = d.NomeUsuario?.Trim() ?? string.Empty;
        if (nomeUsuario.Length == 0) erros.Add(new ErroCampo("nomeUsuario", NaoPreenchido));
        else if (nomeUsuario.Length > 60) erros.Add(new ErroCampo("nomeUsuario", "Máximo de 60 caracteres"));
        else if (!FormatoNomeUsuario.IsMatch(nomeUsuario))
            erros.Add(new ErroCampo("nomeUsuario", "Use primeiro e último nome separados por ponto, em minúsculas e sem acento (exemplo: maria.silva)"));

        var nomeCompleto = d.NomeCompleto?.Trim() ?? string.Empty;
        if (nomeCompleto.Length == 0) erros.Add(new ErroCampo("nomeCompleto", NaoPreenchido));
        else if (nomeCompleto.Length > 150) erros.Add(new ErroCampo("nomeCompleto", "Máximo de 150 caracteres"));

        var email = d.Email?.Trim() ?? string.Empty;
        if (email.Length == 0) erros.Add(new ErroCampo("email", NaoPreenchido));
        else if (email.Length > 150) erros.Add(new ErroCampo("email", "Máximo de 150 caracteres"));
        else if (!Formatos.Email.IsMatch(email)) erros.Add(new ErroCampo("email", "E-mail inválido"));

        var cargo = d.Cargo?.Trim() ?? string.Empty;
        if (cargo.Length == 0) erros.Add(new ErroCampo("cargo", NaoPreenchido));
        else if (cargo.Length > 80) erros.Add(new ErroCampo("cargo", "Máximo de 80 caracteres"));

        if (d.PerfilId is null) erros.Add(new ErroCampo("perfilId", NaoPreenchido));

        return new DadosValidados
        {
            NomeUsuario = nomeUsuario, NomeCompleto = nomeCompleto, Email = email, Cargo = cargo,
            PerfilId = d.PerfilId ?? 0, ProfissionalId = d.ProfissionalId
        };
    }

    /// <summary>Confere perfil e profissional. Devolve o perfil escolhido.</summary>
    private static async Task<PerfilInfo> ConferirReferenciasAsync(
        SqlConnection con, SqlTransaction tx, DadosValidados dados, Linha? atual, UsuarioAtual quemEdita, CancellationToken ct)
    {
        var perfil = await con.QueryFirstOrDefaultAsync<PerfilInfo>(Sql.Cmd(
            "SELECT CODIGO_PERFIL AS Id, PROTEGIDO AS Protegido FROM DBO.PERFIL_ACESSO WHERE CODIGO_PERFIL = @Id;",
            new { Id = dados.PerfilId }, tx, ct));
        if (perfil is null) throw Erro.Validacao("perfilId", "Perfil não encontrado");

        // Só o administrador coloca alguém no perfil Administrador ou tira de lá.
        var mudaDePerfil = atual is null || atual.PerfilId != dados.PerfilId;
        if (mudaDePerfil && perfil.Protegido && !quemEdita.Administrador)
            throw Erro.SemPermissao("Somente o administrador pode atribuir o perfil Administrador.", "PERFIL_PROTEGIDO");

        if (dados.ProfissionalId.HasValue)
        {
            var mudaDeProfissional = atual is null || atual.ProfissionalId != dados.ProfissionalId;
            var profissional = await con.QueryFirstOrDefaultAsync<ProfissionalInfo>(Sql.Cmd(
                "SELECT CODIGO_PROFISSIONAL AS Id, ATIVO AS Ativo FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id;",
                new { Id = dados.ProfissionalId.Value }, tx, ct));
            if (profissional is null) throw Erro.Validacao("profissionalId", "Profissional não encontrado");
            if (mudaDeProfissional && !profissional.Ativo) throw Erro.Validacao("profissionalId", "O profissional está inativo");
        }

        return perfil;
    }

    private static async Task ConferirDuplicidadesAsync(SqlConnection con, SqlTransaction tx, DadosValidados dados, int? ignorarId, CancellationToken ct)
    {
        var p = new { NomeUsuario = dados.NomeUsuario, Email = dados.Email, ProfissionalId = dados.ProfissionalId, Ignorar = ignorarId };

        if (await con.ExecuteScalarAsync<int>(Sql.Cmd(
                "SELECT COUNT(1) FROM DBO.USUARIO WHERE NOME_USUARIO = @NomeUsuario AND (@Ignorar IS NULL OR CODIGO_USUARIO <> @Ignorar);", p, tx, ct)) > 0)
            throw NomeUsuarioDuplicado();

        if (await con.ExecuteScalarAsync<int>(Sql.Cmd(
                "SELECT COUNT(1) FROM DBO.USUARIO WHERE EMAIL = @Email AND (@Ignorar IS NULL OR CODIGO_USUARIO <> @Ignorar);", p, tx, ct)) > 0)
            throw EmailDuplicado();

        if (dados.ProfissionalId.HasValue && await con.ExecuteScalarAsync<int>(Sql.Cmd(
                "SELECT COUNT(1) FROM DBO.USUARIO WHERE CODIGO_PROFISSIONAL = @ProfissionalId AND (@Ignorar IS NULL OR CODIGO_USUARIO <> @Ignorar);", p, tx, ct)) > 0)
            throw ProfissionalJaTemUsuario();
    }

    private static async Task ConferirOutroAdministradorAsync(SqlConnection con, SqlTransaction tx, int idDoUsuario, CancellationToken ct)
    {
        var outros = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.USUARIO U JOIN DBO.PERFIL_ACESSO P ON P.CODIGO_PERFIL = U.CODIGO_PERFIL
WHERE P.PROTEGIDO = 1 AND U.ATIVO = 1 AND U.CODIGO_USUARIO <> @Id;", new { Id = idDoUsuario }, tx, ct));
        if (outros == 0)
            throw Erro.Conflito("O sistema precisa de pelo menos um administrador ativo.", "ULTIMO_ADMINISTRADOR");
    }

    private static void ConferirAcessoAoUsuarioProtegido(Linha alvo, UsuarioAtual quemEdita)
    {
        if (alvo.PerfilProtegido && !quemEdita.Administrador)
            throw Erro.SemPermissao("Somente o administrador altera um usuário do perfil Administrador.", "PERFIL_PROTEGIDO");
    }

    private static Task EncerrarSessoesAsync(SqlConnection con, SqlTransaction tx, int id, string motivo, CancellationToken ct) =>
        con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.SESSAO_USUARIO SET DATA_ENCERRAMENTO = SYSUTCDATETIME(), MOTIVO_ENCERRAMENTO = @Motivo
WHERE CODIGO_USUARIO = @Id AND DATA_ENCERRAMENTO IS NULL;", new { Id = id, Motivo = motivo }, tx, ct));

    private static ApiException NomeUsuarioDuplicado() => Erro.Conflito("Este nome de usuário já está cadastrado.", "NOME_USUARIO_DUPLICADO");
    private static ApiException EmailDuplicado() => Erro.Conflito("Este e-mail já está cadastrado.", "EMAIL_DUPLICADO");
    private static ApiException ProfissionalJaTemUsuario() =>
        Erro.Conflito("Este profissional já tem um usuário.", "PROFISSIONAL_JA_TEM_USUARIO");

    /// <summary>Reserva para a corrida entre duas requisições: o banco recusa e traduzimos o erro.</summary>
    private static ApiException? TraduzirDuplicidade(SqlException ex)
    {
        if (ex.Number is not (2601 or 2627)) return null;
        if (ex.Message.Contains("UQ_USUARIO_NOME_USUARIO", StringComparison.OrdinalIgnoreCase)) return NomeUsuarioDuplicado();
        if (ex.Message.Contains("UQ_USUARIO_EMAIL", StringComparison.OrdinalIgnoreCase)) return EmailDuplicado();
        if (ex.Message.Contains("UX_USUARIO_PROFISSIONAL", StringComparison.OrdinalIgnoreCase)) return ProfissionalJaTemUsuario();
        return null;
    }

    private static Task<Linha?> CarregarAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(SelectUsuario + " WHERE U.CODIGO_USUARIO = @Id;", new { Id = id }, tx, ct));

    private static UsuarioDto ParaDto(Linha l) => new()
    {
        Id = l.Id,
        NomeUsuario = l.NomeUsuario,
        NomeCompleto = l.NomeCompleto,
        Email = l.Email,
        Cargo = l.Cargo,
        Ativo = l.Ativo,
        Perfil = new PerfilReferenciaDto { Id = l.PerfilId, Nome = l.PerfilNome },
        ProfissionalId = l.ProfissionalId,
        ProfissionalNome = l.ProfissionalNome,
        Bloqueado = l.BloqueadoEm.HasValue,
        BloqueadoEm = DataBrasil.Utc(l.BloqueadoEm),
        UltimoLoginEm = DataBrasil.Utc(l.UltimoLoginEm),
        CriadoEm = DataBrasil.Utc(l.CriadoEm),
        CriadoPor = l.CriadoPorId.HasValue
            ? new UsuarioReferenciaDto { Id = l.CriadoPorId.Value, NomeCompleto = l.CriadoPorNome ?? string.Empty }
            : null,
        AtualizadoEm = DataBrasil.Utc(l.AtualizadoEm),
        AtualizadoPor = l.AtualizadoPorId.HasValue
            ? new UsuarioReferenciaDto { Id = l.AtualizadoPorId.Value, NomeCompleto = l.AtualizadoPorNome ?? string.Empty }
            : null
    };
}
