using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Auth;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Perfis;

/// <summary>
/// Perfis de acesso e catálogo de telas. Regras: nome único (409), RN21 (perfil Administrador protegido: 403;
/// perfil com usuário não se exclui: 409), RN23 (avisos de campo obrigatório sem liberação de edição).
/// </summary>
public sealed class PerfilService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public PerfilService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private sealed class LinhaPerfil
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? Descricao { get; set; }
        public bool Protegido { get; set; }
        public int QuantidadeUsuarios { get; set; }
    }

    private sealed class LinhaTela
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
        public string Modulo { get; set; } = string.Empty;
    }

    private sealed class LinhaCampo
    {
        public int Id { get; set; }
        public int TelaId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
        public bool Obrigatorio { get; set; }
    }

    private const string SelectPerfil = @"
SELECT P.CODIGO_PERFIL AS Id, P.NOME AS Nome, P.DESCRICAO AS Descricao, P.PROTEGIDO AS Protegido,
       (SELECT COUNT(1) FROM DBO.USUARIO U WHERE U.CODIGO_PERFIL = P.CODIGO_PERFIL) AS QuantidadeUsuarios
FROM DBO.PERFIL_ACESSO P";

    // ---------------------------------------------------------------- lista e consulta
    public async Task<List<PerfilResumoDto>> ListarAsync(CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var linhas = await con.QueryAsync<LinhaPerfil>(Sql.Cmd(SelectPerfil + " ORDER BY P.PROTEGIDO DESC, P.NOME;", ct: ct));
        return linhas.Select(l => new PerfilResumoDto
        {
            Id = l.Id, Nome = l.Nome, Descricao = l.Descricao, Protegido = l.Protegido, QuantidadeUsuarios = l.QuantidadeUsuarios
        }).ToList();
    }

    public async Task<PerfilDto> ObterAsync(int id, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        return await MontarPerfilAsync(con, null, id, ct) ?? throw Erro.NaoEncontrado("Perfil não encontrado.");
    }

    public async Task<List<TelaDto>> ListarTelasAsync(CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var (telas, campos) = await CarregarCatalogoAsync(con, null, ct);
        return telas.Values
            .OrderBy(t => t.Modulo, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(t => new TelaDto
            {
                Id = t.Id, Codigo = t.Codigo, Nome = t.Nome, Modulo = t.Modulo,
                Campos = campos.Where(c => c.TelaId == t.Id)
                    .OrderBy(c => c.Id)
                    .Select(c => new CampoDto { Id = c.Id, Codigo = c.Codigo, Nome = c.Nome, Obrigatorio = c.Obrigatorio })
                    .ToList()
            }).ToList();
    }

    // ---------------------------------------------------------------- validação sem salvar
    public async Task<ValidacaoPerfilDto> ValidarAsync(PerfilEntrada? d, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var (telas, campos) = await CarregarCatalogoAsync(con, null, ct);
        ValidarEntrada(d, telas, campos, out _, out _, out var avisos);
        return new ValidacaoPerfilDto { Avisos = avisos };
    }

    // ---------------------------------------------------------------- criar
    public async Task<PerfilComAvisosDto> CriarAsync(PerfilEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var (telas, campos) = await CarregarCatalogoAsync(con, tx, ct);
        var (nome, descricao) = ValidarEntrada(d, telas, campos, out _, out _, out var avisos);
        await ConferirNomeDuplicadoAsync(con, tx, nome, null, ct);

        int id;
        try
        {
            id = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.PERFIL_ACESSO (NOME, DESCRICAO) VALUES (@Nome, @Descricao);
SELECT CAST(SCOPE_IDENTITY() AS INT);", new { Nome = nome, Descricao = descricao }, tx, ct));
        }
        catch (SqlException ex) when (EhNomeDuplicado(ex))
        {
            throw NomeDuplicado();
        }

        await GravarPermissoesAsync(con, tx, id, d!.Permissoes!, ct);
        await _auditoria.RegistrarAsync("INCLUIR", "PERFIL_ACESSO", id, null, usuario.Id, new { nome }, con, tx, ct);

        var perfil = await MontarPerfilAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Perfil recém-criado não encontrado.");
        await tx.CommitAsync(ct);
        return ComAvisos(perfil, avisos);
    }

    // ---------------------------------------------------------------- atualizar
    public async Task<PerfilComAvisosDto> AtualizarAsync(int id, PerfilEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarLinhaAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Perfil não encontrado.");
        if (atual.Protegido) throw PerfilProtegido("alterado");

        var (telas, campos) = await CarregarCatalogoAsync(con, tx, ct);
        var (nome, descricao) = ValidarEntrada(d, telas, campos, out _, out _, out var avisos);
        await ConferirNomeDuplicadoAsync(con, tx, nome, id, ct);

        try
        {
            await con.ExecuteAsync(Sql.Cmd(
                "UPDATE DBO.PERFIL_ACESSO SET NOME = @Nome, DESCRICAO = @Descricao WHERE CODIGO_PERFIL = @Id;",
                new { Nome = nome, Descricao = descricao, Id = id }, tx, ct));
        }
        catch (SqlException ex) when (EhNomeDuplicado(ex))
        {
            throw NomeDuplicado();
        }

        await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.PERFIL_TELA_PERMISSAO WHERE CODIGO_PERFIL = @Id;", new { Id = id }, tx, ct));
        await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.PERFIL_CAMPO_PERMISSAO WHERE CODIGO_PERFIL = @Id;", new { Id = id }, tx, ct));
        await GravarPermissoesAsync(con, tx, id, d!.Permissoes!, ct);

        await _auditoria.RegistrarAsync("EDITAR", "PERFIL_ACESSO", id, null, usuario.Id,
            new { nome, nomeAnterior = atual.Nome, liberacoes = true }, con, tx, ct);

        var perfil = await MontarPerfilAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Perfil não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return ComAvisos(perfil, avisos);
    }

    // ---------------------------------------------------------------- excluir
    public async Task ExcluirAsync(int id, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarLinhaAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Perfil não encontrado.");
        if (atual.Protegido) throw PerfilProtegido("excluído");

        if (atual.QuantidadeUsuarios > 0)
            throw Erro.Conflito(
                $"O perfil tem {atual.QuantidadeUsuarios} usuário(s) vinculado(s). Troque o perfil desses usuários antes de excluir.",
                "PERFIL_COM_USUARIOS",
                new Dictionary<string, object?> { ["quantidadeUsuarios"] = atual.QuantidadeUsuarios });

        try
        {
            await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.PERFIL_TELA_PERMISSAO WHERE CODIGO_PERFIL = @Id;", new { Id = id }, tx, ct));
            await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.PERFIL_CAMPO_PERMISSAO WHERE CODIGO_PERFIL = @Id;", new { Id = id }, tx, ct));
            await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.PERFIL_ACESSO WHERE CODIGO_PERFIL = @Id;", new { Id = id }, tx, ct));
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            throw Erro.Conflito("O perfil está em uso e não pode ser excluído.", "PERFIL_EM_USO");
        }

        await _auditoria.RegistrarAsync("EXCLUIR", "PERFIL_ACESSO", id, null, usuario.Id, new { nome = atual.Nome }, con, tx, ct);
        await tx.CommitAsync(ct);
    }

    // =========================================================== apoio
    private static (string Nome, string? Descricao) ValidarEntrada(
        PerfilEntrada? d,
        Dictionary<int, LinhaTela> telas,
        List<LinhaCampo> campos,
        out List<PermissaoTelaDto> telasValidas,
        out List<PermissaoCampoDto> camposValidos,
        out List<AvisoPerfilDto> avisos)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");

        var erros = new List<ErroCampo>();

        var nome = d.Nome?.Trim() ?? string.Empty;
        if (nome.Length == 0) erros.Add(new ErroCampo("nome", NaoPreenchido));
        else if (nome.Length > 60) erros.Add(new ErroCampo("nome", "Máximo de 60 caracteres"));

        string? descricao = string.IsNullOrWhiteSpace(d.Descricao) ? null : d.Descricao.Trim();
        if (descricao is { Length: > 250 }) erros.Add(new ErroCampo("descricao", "Máximo de 250 caracteres"));

        telasValidas = new List<PermissaoTelaDto>();
        camposValidos = new List<PermissaoCampoDto>();
        avisos = new List<AvisoPerfilDto>();

        if (d.Permissoes is null)
        {
            erros.Add(new ErroCampo("permissoes", NaoPreenchido));
            throw Erro.Validacao(erros);
        }

        var telasEnviadas = d.Permissoes.Telas ?? new List<PermissaoTelaDto>();
        var camposEnviados = d.Permissoes.Campos ?? new List<PermissaoCampoDto>();
        d.Permissoes.Telas = telasEnviadas;
        d.Permissoes.Campos = camposEnviados;

        var vistasTelas = new HashSet<int>();
        for (var i = 0; i < telasEnviadas.Count; i++)
        {
            var t = telasEnviadas[i];
            if (!telas.ContainsKey(t.TelaId)) erros.Add(new ErroCampo($"permissoes.telas[{i}].telaId", "Tela não encontrada"));
            else if (!vistasTelas.Add(t.TelaId)) erros.Add(new ErroCampo($"permissoes.telas[{i}].telaId", "Tela repetida"));
            else telasValidas.Add(t);
        }

        var catalogoCampos = campos.ToDictionary(c => c.Id);
        var vistosCampos = new HashSet<int>();
        for (var i = 0; i < camposEnviados.Count; i++)
        {
            var c = camposEnviados[i];
            if (!catalogoCampos.ContainsKey(c.CampoId)) erros.Add(new ErroCampo($"permissoes.campos[{i}].campoId", "Campo não encontrado"));
            else if (!vistosCampos.Add(c.CampoId)) erros.Add(new ErroCampo($"permissoes.campos[{i}].campoId", "Campo repetido"));
            else if (c.PodeEditar && !c.PodeVisualizar) erros.Add(new ErroCampo($"permissoes.campos[{i}].podeEditar", "Para editar, libere também visualizar"));
            else camposValidos.Add(c);
        }

        if (erros.Count > 0) throw Erro.Validacao(erros);

        // RN23: campo obrigatório de tela com incluir ou editar liberado, sem liberação de edição.
        foreach (var t in telasValidas.Where(x => x.PodeIncluir || x.PodeEditar))
        {
            foreach (var campo in campos.Where(c => c.TelaId == t.TelaId && c.Obrigatorio).OrderBy(c => c.Id))
            {
                var liberado = camposValidos.Any(c => c.CampoId == campo.Id && c.PodeEditar);
                if (liberado) continue;
                avisos.Add(new AvisoPerfilDto
                {
                    TelaId = t.TelaId,
                    CampoId = campo.Id,
                    Mensagem = $"O campo \"{campo.Nome}\" da tela \"{telas[t.TelaId].Nome}\" é obrigatório e não está liberado para edição. O perfil não conseguirá salvar o cadastro."
                });
            }
        }

        return (nome, descricao);
    }

    private static async Task GravarPermissoesAsync(SqlConnection con, SqlTransaction tx, int perfilId, PermissoesEntrada p, CancellationToken ct)
    {
        // Só grava linha quando há alguma liberação: tela ou campo sem linha significa "não liberado".
        foreach (var t in p.Telas!.Where(t => t.PodeVisualizar || t.PodeIncluir || t.PodeEditar || t.PodeExcluir || t.PodeExportar))
        {
            await con.ExecuteAsync(Sql.Cmd(@"
INSERT INTO DBO.PERFIL_TELA_PERMISSAO (CODIGO_PERFIL, CODIGO_TELA, PODE_VISUALIZAR, PODE_INCLUIR, PODE_EDITAR, PODE_EXCLUIR, PODE_EXPORTAR)
VALUES (@Perfil, @Tela, @Visualizar, @Incluir, @Editar, @Excluir, @Exportar);",
                new
                {
                    Perfil = perfilId, Tela = t.TelaId, Visualizar = t.PodeVisualizar, Incluir = t.PodeIncluir,
                    Editar = t.PodeEditar, Excluir = t.PodeExcluir, Exportar = t.PodeExportar
                }, tx, ct));
        }

        foreach (var c in p.Campos!.Where(c => c.PodeVisualizar || c.PodeEditar))
        {
            await con.ExecuteAsync(Sql.Cmd(@"
INSERT INTO DBO.PERFIL_CAMPO_PERMISSAO (CODIGO_PERFIL, CODIGO_CAMPO, PODE_VISUALIZAR, PODE_EDITAR)
VALUES (@Perfil, @Campo, @Visualizar, @Editar);",
                new { Perfil = perfilId, Campo = c.CampoId, Visualizar = c.PodeVisualizar, Editar = c.PodeEditar }, tx, ct));
        }
    }

    private static async Task<(Dictionary<int, LinhaTela> Telas, List<LinhaCampo> Campos)> CarregarCatalogoAsync(
        SqlConnection con, SqlTransaction? tx, CancellationToken ct)
    {
        var telas = (await con.QueryAsync<LinhaTela>(Sql.Cmd(
            "SELECT ID_TELA_SISTEMA AS Id, CODIGO AS Codigo, NOME AS Nome, MODULO AS Modulo FROM DBO.TELA_SISTEMA;", null, tx, ct))).ToDictionary(t => t.Id);
        var campos = (await con.QueryAsync<LinhaCampo>(Sql.Cmd(
            "SELECT ID_CAMPO_SISTEMA AS Id, TELA_ID AS TelaId, CODIGO AS Codigo, NOME AS Nome, OBRIGATORIO AS Obrigatorio FROM DBO.CAMPO_SISTEMA;", null, tx, ct))).ToList();
        return (telas, campos);
    }

    private static Task<LinhaPerfil?> CarregarLinhaAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<LinhaPerfil>(Sql.Cmd(SelectPerfil + " WHERE P.CODIGO_PERFIL = @Id;", new { Id = id }, tx, ct));

    private static async Task<PerfilDto?> MontarPerfilAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct)
    {
        var l = await CarregarLinhaAsync(con, tx, id, ct);
        if (l is null) return null;

        IEnumerable<PermissaoTelaDto> telas;
        IEnumerable<PermissaoCampoDto> campos;

        if (l.Protegido)
        {
            // O Administrador tem acesso total, inclusive às telas criadas depois (RN21).
            telas = await con.QueryAsync<PermissaoTelaDto>(Sql.Cmd(@"
SELECT ID_TELA_SISTEMA AS TelaId, CODIGO AS TelaCodigo, CAST(1 AS BIT) AS PodeVisualizar, CAST(1 AS BIT) AS PodeIncluir,
       CAST(1 AS BIT) AS PodeEditar, CAST(1 AS BIT) AS PodeExcluir, CAST(1 AS BIT) AS PodeExportar
FROM DBO.TELA_SISTEMA ORDER BY ID_TELA_SISTEMA;", null, tx, ct));
            campos = await con.QueryAsync<PermissaoCampoDto>(Sql.Cmd(@"
SELECT ID_CAMPO_SISTEMA AS CampoId, CODIGO AS CampoCodigo, CAST(1 AS BIT) AS PodeVisualizar, CAST(1 AS BIT) AS PodeEditar
FROM DBO.CAMPO_SISTEMA ORDER BY ID_CAMPO_SISTEMA;", null, tx, ct));
        }
        else
        {
            telas = await con.QueryAsync<PermissaoTelaDto>(Sql.Cmd(@"
SELECT PT.CODIGO_TELA AS TelaId, T.CODIGO AS TelaCodigo, PT.PODE_VISUALIZAR AS PodeVisualizar, PT.PODE_INCLUIR AS PodeIncluir,
       PT.PODE_EDITAR AS PodeEditar, PT.PODE_EXCLUIR AS PodeExcluir, PT.PODE_EXPORTAR AS PodeExportar
FROM DBO.PERFIL_TELA_PERMISSAO PT
JOIN DBO.TELA_SISTEMA T ON T.ID_TELA_SISTEMA = PT.CODIGO_TELA
WHERE PT.CODIGO_PERFIL = @Id ORDER BY PT.CODIGO_TELA;", new { Id = id }, tx, ct));
            campos = await con.QueryAsync<PermissaoCampoDto>(Sql.Cmd(@"
SELECT PC.CODIGO_CAMPO AS CampoId, C.CODIGO AS CampoCodigo, PC.PODE_VISUALIZAR AS PodeVisualizar, PC.PODE_EDITAR AS PodeEditar
FROM DBO.PERFIL_CAMPO_PERMISSAO PC
JOIN DBO.CAMPO_SISTEMA C ON C.ID_CAMPO_SISTEMA = PC.CODIGO_CAMPO
WHERE PC.CODIGO_PERFIL = @Id ORDER BY PC.CODIGO_CAMPO;", new { Id = id }, tx, ct));
        }

        return new PerfilDto
        {
            Id = l.Id, Nome = l.Nome, Descricao = l.Descricao, Protegido = l.Protegido, QuantidadeUsuarios = l.QuantidadeUsuarios,
            Permissoes = new PermissoesSaida { Telas = telas.ToList(), Campos = campos.ToList() }
        };
    }

    private static PerfilComAvisosDto ComAvisos(PerfilDto p, List<AvisoPerfilDto> avisos) => new()
    {
        Id = p.Id, Nome = p.Nome, Descricao = p.Descricao, Protegido = p.Protegido, QuantidadeUsuarios = p.QuantidadeUsuarios,
        Permissoes = p.Permissoes, Avisos = avisos
    };

    private static async Task ConferirNomeDuplicadoAsync(SqlConnection con, SqlTransaction tx, string nome, int? ignorarId, CancellationToken ct)
    {
        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.PERFIL_ACESSO WHERE NOME = @Nome AND (@Ignorar IS NULL OR CODIGO_PERFIL <> @Ignorar);",
            new { Nome = nome, Ignorar = ignorarId }, tx, ct));
        if (existe > 0) throw NomeDuplicado();
    }

    private static ApiException NomeDuplicado() => Erro.Conflito("Já existe um perfil com este nome.", "PERFIL_DUPLICADO");

    private static bool EhNomeDuplicado(SqlException ex) =>
        ex.Number is 2601 or 2627 && ex.Message.Contains("UQ_PERFIL_ACESSO_NOME", StringComparison.OrdinalIgnoreCase);

    private static ApiException PerfilProtegido(string acao) =>
        Erro.SemPermissao($"O perfil Administrador é protegido e não pode ser {acao}.", "PERFIL_PROTEGIDO");
}
