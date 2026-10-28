using System.Text;
using Dapper;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Db;

namespace VBS.Agendamento.Api.Features.Apoio;

/// <summary>
/// Listas curtas para as caixas de seleção das telas (GET /apoio/{tipo}). Sem dados pessoais e sem paginação.
/// Exige apenas a liberação apoio.listas:visualizar, para o perfil não precisar abrir o cadastro completo.
/// </summary>
public sealed class ApoioService
{
    private readonly IConexaoFactory _db;
    public ApoioService(IConexaoFactory db) => _db = db;

    public async Task<List<ItemApoioDto>> ListarAsync(string tipo, FiltroApoio f, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");
        string sql;

        switch (tipo)
        {
            case "profissionais":
                sql = @"SELECT X.CODIGO_PROFISSIONAL AS Id, X.NOME AS Nome, X.ATIVO AS Ativo,
       X.REGISTRO_PROFISSIONAL + ' ' + X.NUMERO_CONSELHO + '/' + X.UF_CONSELHO AS Complemento
FROM DBO.PROFISSIONAL X";
                if (f.EspecialidadeId.HasValue)
                {
                    onde.Append(" AND EXISTS (SELECT 1 FROM DBO.PROFISSIONAL_ESPECIALIDADE PE WHERE PE.CODIGO_PROFISSIONAL = X.CODIGO_PROFISSIONAL AND PE.CODIGO_ESPECIALIDADE = @EspecialidadeId)");
                    p.Add("EspecialidadeId", f.EspecialidadeId.Value);
                }
                if (f.UnidadeId.HasValue)
                {
                    onde.Append(" AND EXISTS (SELECT 1 FROM DBO.PROFISSIONAL_UNIDADE PU WHERE PU.CODIGO_PROFISSIONAL = X.CODIGO_PROFISSIONAL AND PU.CODIGO_UNIDADE = @UnidadeId)");
                    p.Add("UnidadeId", f.UnidadeId.Value);
                }
                break;
            case "unidades":
                sql = "SELECT X.CODIGO_UNIDADE AS Id, X.NOME_FANTASIA AS Nome, X.ATIVO AS Ativo, CAST(NULL AS NVARCHAR(100)) AS Complemento FROM DBO.UNIDADE X";
                break;
            case "salas":
                sql = @"SELECT X.CODIGO_SALA AS Id, X.NUMERO AS Nome, X.ATIVO AS Ativo,
       U.NOME_FANTASIA + ' - andar ' + X.ANDAR AS Complemento
FROM DBO.SALA X JOIN DBO.UNIDADE U ON U.CODIGO_UNIDADE = X.CODIGO_UNIDADE";
                if (f.UnidadeId.HasValue)
                {
                    onde.Append(" AND X.CODIGO_UNIDADE = @UnidadeId");
                    p.Add("UnidadeId", f.UnidadeId.Value);
                }
                break;
            case "especialidades":
                sql = "SELECT X.CODIGO_ESPECIALIDADE AS Id, X.NOME AS Nome, X.ATIVO AS Ativo, CAST(NULL AS NVARCHAR(100)) AS Complemento FROM DBO.ESPECIALIDADE X";
                break;
            case "convenios":
                sql = "SELECT X.CODIGO_CONVENIO AS Id, COALESCE(X.NOME_FANTASIA, X.RAZAO_SOCIAL) AS Nome, X.ATIVO AS Ativo, CAST(NULL AS NVARCHAR(100)) AS Complemento FROM DBO.CONVENIO X";
                break;
            case "formas-pagamento":
                sql = "SELECT X.CODIGO_PAGAMENTO AS Id, X.DESCRICAO AS Nome, X.ATIVO AS Ativo, X.TIPO AS Complemento FROM DBO.FORMA_PAGAMENTO X";
                break;
            default:
                throw Erro.NaoEncontrado("Tipo de lista não encontrado.");
        }

        if (!f.IncluirInativos) onde.Append(" AND X.ATIVO = 1");

        await using var con = await _db.AbrirAsync(ct);
        var itens = await con.QueryAsync<ItemApoioDto>(Sql.Cmd(sql + onde + " ORDER BY 2;", p, ct: ct));
        return itens.ToList();
    }
}
