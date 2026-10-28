using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Salas;

/// <summary>
/// Bloco 4 do contrato: salas. Regras: número único dentro da unidade, unidade ativa para cadastrar,
/// unidade não muda na alteração, RN14 (inativar, não excluir) e F4 (não inativa com consulta futura marcada).
/// </summary>
public sealed class SalaService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public SalaService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private const string SelectSala = @"
SELECT S.CODIGO_SALA AS Id, S.ATIVO AS Ativo, S.CODIGO_UNIDADE AS UnidadeId, UN.NOME_FANTASIA AS UnidadeNome,
       S.NUMERO AS Numero, S.ANDAR AS Andar,
       S.DATA_CRIACAO AS CriadoEm, S.CRIACAO_USUARIO AS CriadoPorId, UC.NOME_COMPLETO AS CriadoPorNome,
       S.DATA_ATUALIZACAO AS AtualizadoEm, S.ATUALIZACAO_USUARIO AS AtualizadoPorId, UA.NOME_COMPLETO AS AtualizadoPorNome
FROM DBO.SALA S
JOIN DBO.UNIDADE UN ON UN.CODIGO_UNIDADE = S.CODIGO_UNIDADE
LEFT JOIN DBO.USUARIO UC ON UC.CODIGO_USUARIO = S.CRIACAO_USUARIO
LEFT JOIN DBO.USUARIO UA ON UA.CODIGO_USUARIO = S.ATUALIZACAO_USUARIO";

    private sealed class SalaLinha
    {
        public int Id { get; set; }
        public bool Ativo { get; set; }
        public int UnidadeId { get; set; }
        public string UnidadeNome { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string Andar { get; set; } = string.Empty;
        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }
        public string? CriadoPorNome { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public int? AtualizadoPorId { get; set; }
        public string? AtualizadoPorNome { get; set; }
    }

    private sealed class UnidadeSituacao
    {
        public bool Ativo { get; set; }
    }

    // ---------------------------------------------------------------- lista
    public async Task<SalaPaginaDto> ListarAsync(FiltroSalas f, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");

        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            onde.Append(" AND (S.NUMERO LIKE @Busca OR S.ANDAR LIKE @Busca OR UN.NOME_FANTASIA LIKE @Busca)");
            p.Add("Busca", "%" + Formatos.EscaparLike(f.Busca.Trim()) + "%");
        }
        if (f.UnidadeId.HasValue)
        {
            onde.Append(" AND S.CODIGO_UNIDADE = @UnidadeId");
            p.Add("UnidadeId", f.UnidadeId.Value);
        }
        if (f.Ativo.HasValue)
        {
            onde.Append(" AND S.ATIVO = @Ativo");
            p.Add("Ativo", f.Ativo.Value);
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.SALA S JOIN DBO.UNIDADE UN ON UN.CODIGO_UNIDADE = S.CODIGO_UNIDADE" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<SalaLinha>(Sql.Cmd(
            SelectSala + onde + " ORDER BY UN.NOME_FANTASIA, S.ANDAR, S.NUMERO, S.CODIGO_SALA OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new SalaPaginaDto
        {
            Itens = linhas.Select(ParaDto).ToList(),
            Pagina = f.Pagina,
            Tamanho = f.Tamanho,
            Total = total
        };
    }

    // ------------------------------------------------------------- consulta
    public async Task<SalaDto> ObterAsync(int id, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var linha = await CarregarAsync(con, null, id, ct) ?? throw Erro.NaoEncontrado("Sala não encontrada.");
        return ParaDto(linha);
    }

    // ------------------------------------------------------------- cadastro
    public async Task<SalaDto> CriarAsync(SalaEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = Validar(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var unidade = await con.QueryFirstOrDefaultAsync<UnidadeSituacao>(Sql.Cmd(
            "SELECT ATIVO AS Ativo FROM DBO.UNIDADE WHERE CODIGO_UNIDADE = @Id;", new { Id = dados.UnidadeId }, tx, ct));
        if (unidade is null) throw Erro.Validacao("unidadeId", "Unidade não encontrada");
        if (!unidade.Ativo) throw Erro.Validacao("unidadeId", "A unidade está inativa");

        await ConferirDuplicadaAsync(con, tx, dados.UnidadeId, dados.Numero, null, ct);

        int id;
        try
        {
            id = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.SALA (CODIGO_UNIDADE, NUMERO, ANDAR, CRIACAO_USUARIO, ATUALIZACAO_USUARIO)
VALUES (@UnidadeId, @Numero, @Andar, @Usuario, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);", new { dados.UnidadeId, dados.Numero, dados.Andar, Usuario = usuario.Id }, tx, ct));
        }
        catch (SqlException ex) when (EhDuplicada(ex))
        {
            throw Duplicada();
        }

        await _auditoria.RegistrarAsync("INCLUIR", "SALA", id, null, usuario.Id, null, con, tx, ct);

        var linha = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Sala recém-criada não encontrada.");
        await tx.CommitAsync(ct);
        return ParaDto(linha);
    }

    // ------------------------------------------------------------ alteração
    public async Task<SalaDto> AtualizarAsync(int id, SalaEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = Validar(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Sala não encontrada.");
        if (atual.UnidadeId != dados.UnidadeId)
            throw Erro.Validacao("unidadeId", "A unidade de uma sala não pode ser alterada. Cadastre uma nova sala na outra unidade");

        await ConferirDuplicadaAsync(con, tx, atual.UnidadeId, dados.Numero, id, ct);

        try
        {
            await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.SALA SET NUMERO = @Numero, ANDAR = @Andar, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_SALA = @Id;", new { dados.Numero, dados.Andar, Usuario = usuario.Id, Id = id }, tx, ct));
        }
        catch (SqlException ex) when (EhDuplicada(ex))
        {
            throw Duplicada();
        }

        var alterados = new List<string>();
        if (!string.Equals(atual.Numero, dados.Numero, StringComparison.Ordinal)) alterados.Add("numero");
        if (!string.Equals(atual.Andar, dados.Andar, StringComparison.Ordinal)) alterados.Add("andar");

        await _auditoria.RegistrarAsync("EDITAR", "SALA", id, null, usuario.Id, new { campos = alterados }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Sala não encontrada após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // --------------------------------------------------------------- status
    public async Task<SalaDto> AlterarStatusAsync(int id, StatusEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d?.Ativo is null) throw Erro.Validacao("ativo", NaoPreenchido);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        _ = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Sala não encontrada.");

        if (!d.Ativo.Value)
        {
            // F4: não inativa a sala com consulta marcada ou em andamento.
            var quantidade = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.CONSULTA
WHERE CODIGO_SALA = @Id
  AND STATUS IN ('AGENDADA', 'CONFIRMADA', 'CHECK_IN', 'EM_ATENDIMENTO')
  AND DATA_FIM > SYSUTCDATETIME();", new { Id = id }, tx, ct));

            if (quantidade > 0)
                throw Erro.Conflito(
                    $"A sala tem {quantidade} consulta(s) marcada(s) ou em andamento. Remarque ou cancele antes de inativar.",
                    "SALA_COM_CONSULTAS",
                    new Dictionary<string, object?> { ["quantidadeConsultas"] = quantidade });
        }

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.SALA SET ATIVO = @Ativo, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_SALA = @Id;", new { Ativo = d.Ativo.Value, Usuario = usuario.Id, Id = id }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "SALA", id, null, usuario.Id,
            new { campos = new[] { "ativo" }, ativo = d.Ativo.Value }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Sala não encontrada após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // =========================================================== apoio
    private sealed record DadosSala(int UnidadeId, string Numero, string Andar);

    private static DadosSala Validar(SalaEntrada? d)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");

        var e = new List<ErroCampo>();
        if (d.UnidadeId is null or <= 0) e.Add(new ErroCampo("unidadeId", NaoPreenchido));

        if (string.IsNullOrWhiteSpace(d.Numero)) e.Add(new ErroCampo("numero", NaoPreenchido));
        else if (d.Numero.Trim().Length > 40) e.Add(new ErroCampo("numero", "Máximo de 40 caracteres"));

        if (string.IsNullOrWhiteSpace(d.Andar)) e.Add(new ErroCampo("andar", NaoPreenchido));
        else if (d.Andar.Trim().Length > 10) e.Add(new ErroCampo("andar", "Máximo de 10 caracteres"));

        if (e.Count > 0) throw Erro.Validacao(e);

        return new DadosSala(d.UnidadeId!.Value, d.Numero!.Trim(), d.Andar!.Trim());
    }

    private static async Task ConferirDuplicadaAsync(SqlConnection con, SqlTransaction tx, int unidadeId, string numero, int? ignorarId, CancellationToken ct)
    {
        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.SALA WHERE CODIGO_UNIDADE = @UnidadeId AND NUMERO = @Numero AND (@Ignorar IS NULL OR CODIGO_SALA <> @Ignorar);",
            new { UnidadeId = unidadeId, Numero = numero, Ignorar = ignorarId }, tx, ct));
        if (existe > 0) throw Duplicada();
    }

    private static ApiException Duplicada() =>
        Erro.Conflito("Já existe uma sala com este número nesta unidade.", "NUMERO_SALA_DUPLICADO");

    private static bool EhDuplicada(SqlException ex) =>
        ex.Number is 2601 or 2627 && ex.Message.Contains("UQ_SALA_UNIDADE_NUMERO", StringComparison.OrdinalIgnoreCase);

    private static Task<SalaLinha?> CarregarAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<SalaLinha>(Sql.Cmd(SelectSala + " WHERE S.CODIGO_SALA = @Id;", new { Id = id }, tx, ct));

    private static SalaDto ParaDto(SalaLinha l) => new()
    {
        Id = l.Id,
        Ativo = l.Ativo,
        Unidade = new UnidadeSalaDto { Id = l.UnidadeId, Nome = l.UnidadeNome },
        Numero = l.Numero,
        Andar = l.Andar,
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
