using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace VBS.Agendamento.Api.Infra.Db;

public interface IAuditoria
{
    /// <summary>
    /// Grava uma linha em LOG_AUDITORIA (somente inclusão: o banco recusa UPDATE e DELETE).
    /// ACAO aceita: VISUALIZAR, INCLUIR, EDITAR, EXCLUIR, EXPORTAR, LOGIN, LOGIN_FALHA, CORRECAO_PRONTUARIO.
    /// Passe a conexão e a transação quando o log precisa entrar na mesma transação da operação.
    /// </summary>
    Task RegistrarAsync(
        string acao,
        string entidade,
        long? codigoEntidade,
        int? codigoPaciente,
        int? codigoUsuario,
        object? detalhes = null,
        SqlConnection? conexao = null,
        SqlTransaction? tx = null,
        CancellationToken ct = default);
}

public sealed class Auditoria : IAuditoria
{
    private readonly IConexaoFactory _db;
    private readonly IHttpContextAccessor _http;

    public Auditoria(IConexaoFactory db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task RegistrarAsync(
        string acao,
        string entidade,
        long? codigoEntidade,
        int? codigoPaciente,
        int? codigoUsuario,
        object? detalhes = null,
        SqlConnection? conexao = null,
        SqlTransaction? tx = null,
        CancellationToken ct = default)
    {
        const string sql = @"
INSERT INTO DBO.LOG_AUDITORIA (CODIGO_USUARIO, ACAO, ENTIDADE, CODIGO_ENTIDADE, CODIGO_PACIENTE, DETALHES, IP)
VALUES (@Usuario, @Acao, @Entidade, @CodigoEntidade, @Paciente, @Detalhes, @Ip);";

        var ip = _http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        if (ip is { Length: > 45 }) ip = ip[..45];

        var parametros = new
        {
            Usuario = codigoUsuario,
            Acao = acao,
            Entidade = entidade,
            CodigoEntidade = codigoEntidade,
            Paciente = codigoPaciente,
            Detalhes = detalhes is null ? null : JsonSerializer.Serialize(detalhes),
            Ip = ip
        };

        if (conexao is not null)
        {
            await conexao.ExecuteAsync(Sql.Cmd(sql, parametros, tx, ct));
            return;
        }

        await using var nova = await _db.AbrirAsync(ct);
        await nova.ExecuteAsync(Sql.Cmd(sql, parametros, null, ct));
    }
}
