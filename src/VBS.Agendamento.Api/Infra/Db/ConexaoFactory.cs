using Dapper;
using Microsoft.Data.SqlClient;

namespace VBS.Agendamento.Api.Infra.Db;

public interface IConexaoFactory
{
    /// <summary>Abre uma conexão com o VBSAGENDA (o pool do SqlClient reaproveita as conexões).</summary>
    Task<SqlConnection> AbrirAsync(CancellationToken ct = default);
}

public sealed class ConexaoFactory : IConexaoFactory
{
    private readonly string _cadeia;

    public ConexaoFactory(IConfiguration configuracao)
    {
        _cadeia = configuracao.GetConnectionString("VBSAGENDA") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_cadeia))
            throw new InvalidOperationException(
                "Falta a cadeia de conexão ConnectionStrings:VBSAGENDA (veja appsettings.Development.json).");
    }

    public async Task<SqlConnection> AbrirAsync(CancellationToken ct = default)
    {
        var conexao = new SqlConnection(_cadeia);
        try
        {
            await conexao.OpenAsync(ct);
            return conexao;
        }
        catch
        {
            await conexao.DisposeAsync();
            throw;
        }
    }
}

public static class Sql
{
    /// <summary>Monta o comando do Dapper com o token de cancelamento.</summary>
    public static CommandDefinition Cmd(string sql, object? parametros = null, SqlTransaction? tx = null, CancellationToken ct = default) =>
        new(sql, parametros, tx, cancellationToken: ct);

    /// <summary>Parâmetro CHAR/VARCHAR (não Unicode). Evita conversão implícita que impede o uso do índice (CPF, por exemplo).</summary>
    public static DbString Char(string? valor, int tamanho) =>
        new() { Value = valor, Length = tamanho, IsAnsi = true, IsFixedLength = true };
}
