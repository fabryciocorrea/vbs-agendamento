using Dapper;
using VBS.Agendamento.Api.Features.Auth;
using VBS.Agendamento.Api.Infra.Db;

namespace VBS.Agendamento.Api.Infra.Seguranca;

public interface IPermissaoService
{
    /// <summary>Confere uma exigência "tela:ação" (por exemplo paciente.cadastro:incluir) para o perfil. RNF08.</summary>
    Task<bool> TemAsync(int perfilId, bool administrador, string exigencia, CancellationToken ct);

    /// <summary>Liberações do perfil por tela e por campo, para GET /auth/eu.</summary>
    Task<PermissoesSaida> ListarAsync(int perfilId, bool administrador, CancellationToken ct);
}

public sealed class PermissaoService : IPermissaoService
{
    private readonly IConexaoFactory _db;
    public PermissaoService(IConexaoFactory db) => _db = db;

    // As cinco ações que a tabela PERFIL_TELA_PERMISSAO sabe guardar.
    private static string? Coluna(string acao) => acao switch
    {
        "visualizar" => "PODE_VISUALIZAR",
        "incluir" => "PODE_INCLUIR",
        "editar" => "PODE_EDITAR",
        "excluir" => "PODE_EXCLUIR",
        "exportar" => "PODE_EXPORTAR",
        _ => null
    };

    public async Task<bool> TemAsync(int perfilId, bool administrador, string exigencia, CancellationToken ct)
    {
        // O perfil Administrador (protegido) tem acesso total ao sistema.
        if (administrador) return true;

        var posicao = exigencia.IndexOf(':');
        if (posicao <= 0) return false;
        var tela = exigencia[..posicao];
        var coluna = Coluna(exigencia[(posicao + 1)..]);
        if (coluna is null) return false;

        // "coluna" vem da lista fechada acima, nunca do cliente.
        var sql = $@"
SELECT COUNT(1)
FROM DBO.PERFIL_TELA_PERMISSAO PT
JOIN DBO.TELA_SISTEMA T ON T.ID_TELA_SISTEMA = PT.CODIGO_TELA
WHERE PT.CODIGO_PERFIL = @Perfil AND T.CODIGO = @Tela AND PT.{coluna} = 1;";

        await using var con = await _db.AbrirAsync(ct);
        return await con.ExecuteScalarAsync<int>(Sql.Cmd(sql, new { Perfil = perfilId, Tela = tela }, ct: ct)) > 0;
    }

    public async Task<PermissoesSaida> ListarAsync(int perfilId, bool administrador, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);

        IEnumerable<PermissaoTelaDto> telas;
        IEnumerable<PermissaoCampoDto> campos;

        if (administrador)
        {
            telas = await con.QueryAsync<PermissaoTelaDto>(Sql.Cmd(@"
SELECT ID_TELA_SISTEMA AS TelaId, CODIGO AS TelaCodigo, CAST(1 AS BIT) AS PodeVisualizar, CAST(1 AS BIT) AS PodeIncluir,
       CAST(1 AS BIT) AS PodeEditar, CAST(1 AS BIT) AS PodeExcluir, CAST(1 AS BIT) AS PodeExportar
FROM DBO.TELA_SISTEMA ORDER BY ID_TELA_SISTEMA;", ct: ct));

            campos = await con.QueryAsync<PermissaoCampoDto>(Sql.Cmd(@"
SELECT ID_CAMPO_SISTEMA AS CampoId, CODIGO AS CampoCodigo, CAST(1 AS BIT) AS PodeVisualizar, CAST(1 AS BIT) AS PodeEditar
FROM DBO.CAMPO_SISTEMA ORDER BY ID_CAMPO_SISTEMA;", ct: ct));
        }
        else
        {
            telas = await con.QueryAsync<PermissaoTelaDto>(Sql.Cmd(@"
SELECT PT.CODIGO_TELA AS TelaId, T.CODIGO AS TelaCodigo, PT.PODE_VISUALIZAR AS PodeVisualizar, PT.PODE_INCLUIR AS PodeIncluir,
       PT.PODE_EDITAR AS PodeEditar, PT.PODE_EXCLUIR AS PodeExcluir, PT.PODE_EXPORTAR AS PodeExportar
FROM DBO.PERFIL_TELA_PERMISSAO PT
JOIN DBO.TELA_SISTEMA T ON T.ID_TELA_SISTEMA = PT.CODIGO_TELA
WHERE PT.CODIGO_PERFIL = @Perfil ORDER BY PT.CODIGO_TELA;", new { Perfil = perfilId }, ct: ct));

            campos = await con.QueryAsync<PermissaoCampoDto>(Sql.Cmd(@"
SELECT PC.CODIGO_CAMPO AS CampoId, C.CODIGO AS CampoCodigo, PC.PODE_VISUALIZAR AS PodeVisualizar, PC.PODE_EDITAR AS PodeEditar
FROM DBO.PERFIL_CAMPO_PERMISSAO PC
JOIN DBO.CAMPO_SISTEMA C ON C.ID_CAMPO_SISTEMA = PC.CODIGO_CAMPO
WHERE PC.CODIGO_PERFIL = @Perfil ORDER BY PC.CODIGO_CAMPO;", new { Perfil = perfilId }, ct: ct));
        }

        return new PermissoesSaida { Telas = telas.ToList(), Campos = campos.ToList() };
    }
}
