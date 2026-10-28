using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Especialidades;

/// <summary>
/// Bloco 3 do contrato: especialidades. Regras: nome único (409), RN14 (inativar, não excluir),
/// F4 (não inativa com consulta futura marcada).
/// </summary>
public sealed class EspecialidadeService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public EspecialidadeService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private const string SelectEspecialidade = @"
SELECT E.CODIGO_ESPECIALIDADE AS Id, E.ATIVO AS Ativo, E.NOME AS Nome,
       E.DATA_CRIACAO AS CriadoEm, E.CRIACAO_USUARIO AS CriadoPorId, UC.NOME_COMPLETO AS CriadoPorNome,
       E.DATA_ATUALIZACAO AS AtualizadoEm, E.ATUALIZACAO_USUARIO AS AtualizadoPorId, UA.NOME_COMPLETO AS AtualizadoPorNome
FROM DBO.ESPECIALIDADE E
LEFT JOIN DBO.USUARIO UC ON UC.CODIGO_USUARIO = E.CRIACAO_USUARIO
LEFT JOIN DBO.USUARIO UA ON UA.CODIGO_USUARIO = E.ATUALIZACAO_USUARIO";

    private sealed class Linha
    {
        public int Id { get; set; }
        public bool Ativo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }
        public string? CriadoPorNome { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public int? AtualizadoPorId { get; set; }
        public string? AtualizadoPorNome { get; set; }
    }

    public async Task<EspecialidadePaginaDto> ListarAsync(FiltroEspecialidades f, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            onde.Append(" AND E.NOME LIKE @Busca");
            p.Add("Busca", "%" + Formatos.EscaparLike(f.Busca.Trim()) + "%");
        }
        if (f.Ativo.HasValue)
        {
            onde.Append(" AND E.ATIVO = @Ativo");
            p.Add("Ativo", f.Ativo.Value);
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.ESPECIALIDADE E" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(
            SelectEspecialidade + onde + " ORDER BY E.NOME, E.CODIGO_ESPECIALIDADE OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new EspecialidadePaginaDto { Itens = linhas.Select(ParaDto).ToList(), Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total };
    }

    public async Task<EspecialidadeDto> ObterAsync(int id, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var linha = await CarregarAsync(con, null, id, ct) ?? throw Erro.NaoEncontrado("Especialidade não encontrada.");
        return ParaDto(linha);
    }

    public async Task<EspecialidadeDto> CriarAsync(EspecialidadeEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var nome = ValidarNome(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await ConferirNomeDuplicadoAsync(con, tx, nome, null, ct);

        int id;
        try
        {
            id = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.ESPECIALIDADE (NOME, CRIACAO_USUARIO, ATUALIZACAO_USUARIO) VALUES (@Nome, @Usuario, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);", new { Nome = nome, Usuario = usuario.Id }, tx, ct));
        }
        catch (SqlException ex) when (EhNomeDuplicado(ex))
        {
            throw NomeDuplicado();
        }

        await _auditoria.RegistrarAsync("INCLUIR", "ESPECIALIDADE", id, null, usuario.Id, null, con, tx, ct);
        var linha = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Especialidade recém-criada não encontrada.");
        await tx.CommitAsync(ct);
        return ParaDto(linha);
    }

    public async Task<EspecialidadeDto> AtualizarAsync(int id, EspecialidadeEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var nome = ValidarNome(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Especialidade não encontrada.");
        await ConferirNomeDuplicadoAsync(con, tx, nome, id, ct);

        try
        {
            await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.ESPECIALIDADE SET NOME = @Nome, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_ESPECIALIDADE = @Id;", new { Nome = nome, Usuario = usuario.Id, Id = id }, tx, ct));
        }
        catch (SqlException ex) when (EhNomeDuplicado(ex))
        {
            throw NomeDuplicado();
        }

        var alterados = string.Equals(atual.Nome, nome, StringComparison.Ordinal) ? new List<string>() : new List<string> { "nome" };
        await _auditoria.RegistrarAsync("EDITAR", "ESPECIALIDADE", id, null, usuario.Id, new { campos = alterados }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Especialidade não encontrada após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    public async Task<EspecialidadeDto> AlterarStatusAsync(int id, StatusEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d?.Ativo is null) throw Erro.Validacao("ativo", NaoPreenchido);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        _ = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Especialidade não encontrada.");

        if (!d.Ativo.Value)
        {
            // F4: não inativa a especialidade com consulta marcada ou em andamento.
            var quantidade = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.CONSULTA
WHERE CODIGO_ESPECIALIDADE = @Id
  AND STATUS IN ('AGENDADA', 'CONFIRMADA', 'CHECK_IN', 'EM_ATENDIMENTO')
  AND DATA_FIM > SYSUTCDATETIME();", new { Id = id }, tx, ct));

            if (quantidade > 0)
                throw Erro.Conflito(
                    $"A especialidade tem {quantidade} consulta(s) marcada(s) ou em andamento. Remarque ou cancele antes de inativar.",
                    "ESPECIALIDADE_COM_CONSULTAS",
                    new Dictionary<string, object?> { ["quantidadeConsultas"] = quantidade });
        }

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.ESPECIALIDADE SET ATIVO = @Ativo, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_ESPECIALIDADE = @Id;", new { Ativo = d.Ativo.Value, Usuario = usuario.Id, Id = id }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "ESPECIALIDADE", id, null, usuario.Id,
            new { campos = new[] { "ativo" }, ativo = d.Ativo.Value }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Especialidade não encontrada após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // =========================================================== apoio
    private static string ValidarNome(EspecialidadeEntrada? d)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        if (string.IsNullOrWhiteSpace(d.Nome)) throw Erro.Validacao("nome", NaoPreenchido);
        var nome = d.Nome.Trim();
        if (nome.Length > 80) throw Erro.Validacao("nome", "Máximo de 80 caracteres");
        return nome;
    }

    private static async Task ConferirNomeDuplicadoAsync(SqlConnection con, SqlTransaction tx, string nome, int? ignorarId, CancellationToken ct)
    {
        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.ESPECIALIDADE WHERE NOME = @Nome AND (@Ignorar IS NULL OR CODIGO_ESPECIALIDADE <> @Ignorar);",
            new { Nome = nome, Ignorar = ignorarId }, tx, ct));
        if (existe > 0) throw NomeDuplicado();
    }

    private static ApiException NomeDuplicado() => Erro.Conflito("Esta especialidade já está cadastrada.", "ESPECIALIDADE_DUPLICADA");

    private static bool EhNomeDuplicado(SqlException ex) =>
        ex.Number is 2601 or 2627 && ex.Message.Contains("UQ_ESPECIALIDADE_NOME", StringComparison.OrdinalIgnoreCase);

    private static Task<Linha?> CarregarAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(SelectEspecialidade + " WHERE E.CODIGO_ESPECIALIDADE = @Id;", new { Id = id }, tx, ct));

    private static EspecialidadeDto ParaDto(Linha l) => new()
    {
        Id = l.Id,
        Ativo = l.Ativo,
        Nome = l.Nome,
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
