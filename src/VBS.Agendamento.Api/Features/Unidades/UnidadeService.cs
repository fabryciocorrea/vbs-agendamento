using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Unidades;

/// <summary>
/// Bloco 3 do contrato: unidades. Regras: RN13 (CNPJ único e válido), RN14 (inativar, não excluir),
/// F4 (não inativa com consulta futura marcada). Ainda não feito: liberação campo a campo (E2).
/// </summary>
public sealed class UnidadeService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public UnidadeService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private const string SelectUnidade = @"
SELECT U.CODIGO_UNIDADE AS Id, U.ATIVO AS Ativo, U.RAZAO_SOCIAL AS RazaoSocial, U.NOME_FANTASIA AS NomeFantasia,
       U.CNPJ AS Cnpj, U.CEP AS Cep, U.ENDERECO AS Endereco, U.NUMERO AS Numero, U.COMPLEMENTO AS Complemento, U.CIDADE AS Cidade, U.UF AS Uf,
       U.DATA_CRIACAO AS CriadoEm, U.CRIACAO_USUARIO AS CriadoPorId, UC.NOME_COMPLETO AS CriadoPorNome,
       U.DATA_ATUALIZACAO AS AtualizadoEm, U.ATUALIZACAO_USUARIO AS AtualizadoPorId, UA.NOME_COMPLETO AS AtualizadoPorNome
FROM DBO.UNIDADE U
LEFT JOIN DBO.USUARIO UC ON UC.CODIGO_USUARIO = U.CRIACAO_USUARIO
LEFT JOIN DBO.USUARIO UA ON UA.CODIGO_USUARIO = U.ATUALIZACAO_USUARIO";

    private sealed class UnidadeLinha
    {
        public int Id { get; set; }
        public bool Ativo { get; set; }
        public string RazaoSocial { get; set; } = string.Empty;
        public string NomeFantasia { get; set; } = string.Empty;
        public string Cnpj { get; set; } = string.Empty;
        public string Cep { get; set; } = string.Empty;
        public string Endereco { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string? Complemento { get; set; }
        public string Cidade { get; set; } = string.Empty;
        public string Uf { get; set; } = string.Empty;
        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }
        public string? CriadoPorNome { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public int? AtualizadoPorId { get; set; }
        public string? AtualizadoPorNome { get; set; }
    }

    // ---------------------------------------------------------------- lista
    public async Task<UnidadePaginaDto> ListarAsync(FiltroUnidades f, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");

        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            onde.Append(" AND (U.NOME_FANTASIA LIKE @Busca OR U.RAZAO_SOCIAL LIKE @Busca)");
            p.Add("Busca", "%" + Formatos.EscaparLike(f.Busca.Trim()) + "%");
        }
        if (f.Ativo.HasValue)
        {
            onde.Append(" AND U.ATIVO = @Ativo");
            p.Add("Ativo", f.Ativo.Value);
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.UNIDADE U" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<UnidadeLinha>(Sql.Cmd(
            SelectUnidade + onde + " ORDER BY U.NOME_FANTASIA, U.CODIGO_UNIDADE OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new UnidadePaginaDto
        {
            Itens = linhas.Select(ParaDto).ToList(),
            Pagina = f.Pagina,
            Tamanho = f.Tamanho,
            Total = total
        };
    }

    // ------------------------------------------------------------- consulta
    public async Task<UnidadeDto> ObterAsync(int id, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var linha = await CarregarAsync(con, null, id, ct) ?? throw Erro.NaoEncontrado("Unidade não encontrada.");
        return ParaDto(linha);
    }

    // ------------------------------------------------------------- cadastro
    public async Task<UnidadeDto> CriarAsync(UnidadeEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = Validar(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await ConferirCnpjDuplicadoAsync(con, tx, dados.Cnpj, null, ct);

        int id;
        try
        {
            id = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.UNIDADE (RAZAO_SOCIAL, NOME_FANTASIA, CNPJ, CEP, ENDERECO, NUMERO, COMPLEMENTO, CIDADE, UF, CRIACAO_USUARIO, ATUALIZACAO_USUARIO)
VALUES (@RazaoSocial, @NomeFantasia, @Cnpj, @Cep, @Endereco, @Numero, @Complemento, @Cidade, @Uf, @Usuario, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);", Parametros(dados, usuario.Id, null), tx, ct));
        }
        catch (SqlException ex) when (EhCnpjDuplicado(ex))
        {
            throw CnpjDuplicado();
        }

        await _auditoria.RegistrarAsync("INCLUIR", "UNIDADE", id, null, usuario.Id, null, con, tx, ct);

        var linha = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Unidade recém-criada não encontrada.");
        await tx.CommitAsync(ct);
        return ParaDto(linha);
    }

    // ------------------------------------------------------------ alteração
    public async Task<UnidadeDto> AtualizarAsync(int id, UnidadeEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = Validar(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Unidade não encontrada.");
        await ConferirCnpjDuplicadoAsync(con, tx, dados.Cnpj, id, ct);

        try
        {
            await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.UNIDADE SET RAZAO_SOCIAL = @RazaoSocial, NOME_FANTASIA = @NomeFantasia, CNPJ = @Cnpj, CEP = @Cep,
       ENDERECO = @Endereco, NUMERO = @Numero, COMPLEMENTO = @Complemento, CIDADE = @Cidade, UF = @Uf, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_UNIDADE = @Id;", Parametros(dados, usuario.Id, id), tx, ct));
        }
        catch (SqlException ex) when (EhCnpjDuplicado(ex))
        {
            throw CnpjDuplicado();
        }

        var alterados = new List<string>();
        void Dif(string campo, string? antes, string? depois) { if (!string.Equals(antes, depois, StringComparison.Ordinal)) alterados.Add(campo); }
        Dif("razaoSocial", atual.RazaoSocial, dados.RazaoSocial);
        Dif("nomeFantasia", atual.NomeFantasia, dados.NomeFantasia);
        Dif("cnpj", atual.Cnpj, dados.Cnpj);
        Dif("cep", atual.Cep, dados.Cep);
        Dif("endereco", atual.Endereco, dados.Endereco);
        Dif("numero", atual.Numero, dados.Numero);
        Dif("complemento", atual.Complemento, dados.Complemento);
        Dif("cidade", atual.Cidade, dados.Cidade);
        Dif("uf", atual.Uf, dados.Uf);

        await _auditoria.RegistrarAsync("EDITAR", "UNIDADE", id, null, usuario.Id, new { campos = alterados }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Unidade não encontrada após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // --------------------------------------------------------------- status
    public async Task<UnidadeDto> AlterarStatusAsync(int id, StatusEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d?.Ativo is null) throw Erro.Validacao("ativo", NaoPreenchido);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        _ = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Unidade não encontrada.");

        if (!d.Ativo.Value)
        {
            // F4: não inativa a unidade com consulta marcada ou em andamento.
            var quantidade = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.CONSULTA
WHERE CODIGO_UNIDADE = @Id
  AND STATUS IN ('AGENDADA', 'CONFIRMADA', 'CHECK_IN', 'EM_ATENDIMENTO')
  AND DATA_FIM > SYSUTCDATETIME();", new { Id = id }, tx, ct));

            if (quantidade > 0)
                throw Erro.Conflito(
                    $"A unidade tem {quantidade} consulta(s) marcada(s) ou em andamento. Remarque ou cancele antes de inativar.",
                    "UNIDADE_COM_CONSULTAS",
                    new Dictionary<string, object?> { ["quantidadeConsultas"] = quantidade });
        }

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.UNIDADE SET ATIVO = @Ativo, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_UNIDADE = @Id;", new { Ativo = d.Ativo.Value, Usuario = usuario.Id, Id = id }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "UNIDADE", id, null, usuario.Id,
            new { campos = new[] { "ativo" }, ativo = d.Ativo.Value }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Unidade não encontrada após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // =========================================================== apoio
    private sealed record DadosUnidade(string RazaoSocial, string NomeFantasia, string Cnpj, string Cep, string Endereco, string Numero, string? Complemento, string Cidade, string Uf);

    private static DadosUnidade Validar(UnidadeEntrada? d)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");

        var e = new List<ErroCampo>();
        void Obrigatorio(string campo, string? valor, int max)
        {
            if (string.IsNullOrWhiteSpace(valor)) e.Add(new ErroCampo(campo, NaoPreenchido));
            else if (valor.Trim().Length > max) e.Add(new ErroCampo(campo, $"Máximo de {max} caracteres"));
        }

        Obrigatorio("razaoSocial", d.RazaoSocial, 150);
        Obrigatorio("nomeFantasia", d.NomeFantasia, 150);
        Obrigatorio("endereco", d.Endereco, 250);
        Obrigatorio("numero", d.Numero, 10);
        if (!string.IsNullOrWhiteSpace(d.Complemento) && d.Complemento.Trim().Length > 60) e.Add(new ErroCampo("complemento", "Máximo de 60 caracteres"));
        Obrigatorio("cidade", d.Cidade, 80);

        if (string.IsNullOrWhiteSpace(d.Cnpj)) e.Add(new ErroCampo("cnpj", NaoPreenchido));
        else if (!CnpjUtil.Valido(d.Cnpj.Trim())) e.Add(new ErroCampo("cnpj", "CNPJ inválido"));

        if (string.IsNullOrWhiteSpace(d.Cep)) e.Add(new ErroCampo("cep", NaoPreenchido));
        else if (!Formatos.Cep.IsMatch(d.Cep.Trim())) e.Add(new ErroCampo("cep", "CEP inválido (8 dígitos)"));

        if (string.IsNullOrWhiteSpace(d.Uf)) e.Add(new ErroCampo("uf", NaoPreenchido));
        else if (!Formatos.Ufs.Contains(d.Uf)) e.Add(new ErroCampo("uf", "UF inválida"));

        if (e.Count > 0) throw Erro.Validacao(e);

        return new DadosUnidade(d.RazaoSocial!.Trim(), d.NomeFantasia!.Trim(), d.Cnpj!.Trim(), d.Cep!.Trim(),
            d.Endereco!.Trim(), d.Numero!.Trim().ToUpperInvariant(), string.IsNullOrWhiteSpace(d.Complemento) ? null : d.Complemento.Trim(), d.Cidade!.Trim(), d.Uf!);
    }

    private static object Parametros(DadosUnidade d, int usuarioId, int? id) => new
    {
        Id = id,
        d.RazaoSocial,
        d.NomeFantasia,
        Cnpj = Sql.Char(d.Cnpj, 14),
        Cep = Sql.Char(d.Cep, 8),
        d.Endereco,
        d.Numero,
        d.Complemento,
        d.Cidade,
        d.Uf,
        Usuario = usuarioId
    };

    private static async Task ConferirCnpjDuplicadoAsync(SqlConnection con, SqlTransaction tx, string cnpj, int? ignorarId, CancellationToken ct)
    {
        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.UNIDADE WHERE CNPJ = @Cnpj AND (@Ignorar IS NULL OR CODIGO_UNIDADE <> @Ignorar);",
            new { Cnpj = Sql.Char(cnpj, 14), Ignorar = ignorarId }, tx, ct));
        if (existe > 0) throw CnpjDuplicado();
    }

    private static ApiException CnpjDuplicado() => Erro.Conflito("Este CNPJ já está cadastrado.", "CNPJ_DUPLICADO");

    private static bool EhCnpjDuplicado(SqlException ex) =>
        ex.Number is 2601 or 2627 && ex.Message.Contains("UQ_UNIDADE_CNPJ", StringComparison.OrdinalIgnoreCase);

    private static Task<UnidadeLinha?> CarregarAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<UnidadeLinha>(Sql.Cmd(SelectUnidade + " WHERE U.CODIGO_UNIDADE = @Id;", new { Id = id }, tx, ct));

    private static UnidadeDto ParaDto(UnidadeLinha l) => new()
    {
        Id = l.Id,
        Ativo = l.Ativo,
        RazaoSocial = l.RazaoSocial,
        NomeFantasia = l.NomeFantasia,
        Cnpj = l.Cnpj,
        Cep = l.Cep,
        Endereco = l.Endereco,
        Numero = l.Numero,
        Complemento = l.Complemento,
        Cidade = l.Cidade,
        Uf = l.Uf,
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
