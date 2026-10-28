using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Colaboradores;

/// <summary>
/// Bloco 4 do contrato: colaboradores (CLT) e prestadores (MEI). Regras: RN13 (CPF e CNPJ únicos e válidos),
/// RN14 (inativar, não excluir). A matrícula é o CODIGO_CLB_PRT e não é aceita no corpo das requisições.
/// Ainda não feito: liberação campo a campo (E2).
/// </summary>
public sealed class ColaboradorService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private static readonly Regex Digitos11 = new(@"^[0-9]{11}$", RegexOptions.Compiled);
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public ColaboradorService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private const string SelectColaborador = @"
SELECT C.CODIGO_CLB_PRT AS Id, C.ATIVO AS Ativo, C.TIPO_VINCULO AS TipoVinculo, C.NOME AS Nome, C.CPF AS Cpf,
       C.RAZAO_SOCIAL AS RazaoSocial, C.NOME_FANTASIA AS NomeFantasia, C.CNPJ AS Cnpj,
       C.TELEFONE AS Telefone, C.EMAIL AS Email, C.ENDERECO AS Endereco, C.NUMERO AS Numero, C.COMPLEMENTO AS Complemento,
       C.CIDADE AS Cidade, C.UF AS Uf, C.CARGO AS Cargo,
       C.CTPS_NUMERO AS CtpsNumero, C.CTPS_SERIE AS CtpsSerie, C.CTPS_UF AS CtpsUf, C.CTPS_DATA_EMISSAO AS CtpsDataEmissao,
       C.PIS_PASEP AS PisPasep, C.DATA_ADMISSAO AS DataAdmissao,
       C.DATA_CRIACAO AS CriadoEm, C.CRIACAO_USUARIO AS CriadoPorId, UC.NOME_COMPLETO AS CriadoPorNome,
       C.DATA_ATUALIZACAO AS AtualizadoEm, C.ATUALIZACAO_USUARIO AS AtualizadoPorId, UA.NOME_COMPLETO AS AtualizadoPorNome
FROM DBO.COLABORADOR_PRESTADOR C
LEFT JOIN DBO.USUARIO UC ON UC.CODIGO_USUARIO = C.CRIACAO_USUARIO
LEFT JOIN DBO.USUARIO UA ON UA.CODIGO_USUARIO = C.ATUALIZACAO_USUARIO";

    private sealed class ColaboradorLinha
    {
        public int Id { get; set; }
        public bool Ativo { get; set; }
        public string TipoVinculo { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
        public string? Cpf { get; set; }
        public string? RazaoSocial { get; set; }
        public string? NomeFantasia { get; set; }
        public string? Cnpj { get; set; }
        public string Telefone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Endereco { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string? Complemento { get; set; }
        public string Cidade { get; set; } = string.Empty;
        public string Uf { get; set; } = string.Empty;
        public string Cargo { get; set; } = string.Empty;
        public string? CtpsNumero { get; set; }
        public string? CtpsSerie { get; set; }
        public string? CtpsUf { get; set; }
        public DateTime? CtpsDataEmissao { get; set; }
        public string? PisPasep { get; set; }
        public DateTime? DataAdmissao { get; set; }
        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }
        public string? CriadoPorNome { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public int? AtualizadoPorId { get; set; }
        public string? AtualizadoPorNome { get; set; }
    }

    // ---------------------------------------------------------------- lista
    public async Task<ColaboradorPaginaDto> ListarAsync(FiltroColaboradores f, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (!string.IsNullOrWhiteSpace(f.TipoVinculo) && f.TipoVinculo is not ("CLT" or "MEI"))
            erros.Add(new ErroCampo("tipoVinculo", "Informe CLT ou MEI"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");

        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            var termo = f.Busca.Trim();
            onde.Append(" AND (C.NOME LIKE @Busca OR C.RAZAO_SOCIAL LIKE @Busca OR C.NOME_FANTASIA LIKE @Busca");
            if (int.TryParse(termo, out var matricula))
            {
                onde.Append(" OR C.CODIGO_CLB_PRT = @Matricula");
                p.Add("Matricula", matricula);
            }
            onde.Append(')');
            p.Add("Busca", "%" + Formatos.EscaparLike(termo) + "%");
        }
        if (!string.IsNullOrWhiteSpace(f.TipoVinculo))
        {
            onde.Append(" AND C.TIPO_VINCULO = @Tipo");
            p.Add("Tipo", f.TipoVinculo);
        }
        if (f.Ativo.HasValue)
        {
            onde.Append(" AND C.ATIVO = @Ativo");
            p.Add("Ativo", f.Ativo.Value);
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.COLABORADOR_PRESTADOR C" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<ColaboradorLinha>(Sql.Cmd(
            SelectColaborador + onde + " ORDER BY C.NOME, C.CODIGO_CLB_PRT OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new ColaboradorPaginaDto
        {
            Itens = linhas.Select(ParaDto).ToList(),
            Pagina = f.Pagina,
            Tamanho = f.Tamanho,
            Total = total
        };
    }

    // ------------------------------------------------------------- consulta
    public async Task<ColaboradorDto> ObterAsync(int id, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var linha = await CarregarAsync(con, null, id, ct) ?? throw Erro.NaoEncontrado("Colaborador ou prestador não encontrado.");
        return ParaDto(linha);
    }

    // ------------------------------------------------------------- cadastro
    public async Task<ColaboradorDto> CriarAsync(ColaboradorEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = Validar(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await ConferirDuplicidadesAsync(con, tx, dados, null, ct);

        int id;
        try
        {
            id = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.COLABORADOR_PRESTADOR (TIPO_VINCULO, NOME, CPF, RAZAO_SOCIAL, NOME_FANTASIA, CNPJ, TELEFONE, EMAIL,
       ENDERECO, NUMERO, COMPLEMENTO, CIDADE, UF, CARGO, CTPS_NUMERO, CTPS_SERIE, CTPS_UF, CTPS_DATA_EMISSAO, PIS_PASEP, DATA_ADMISSAO,
       CRIACAO_USUARIO, ATUALIZACAO_USUARIO)
VALUES (@TipoVinculo, @Nome, @Cpf, @RazaoSocial, @NomeFantasia, @Cnpj, @Telefone, @Email,
       @Endereco, @Numero, @Complemento, @Cidade, @Uf, @Cargo, @CtpsNumero, @CtpsSerie, @CtpsUf, @CtpsDataEmissao, @PisPasep, @DataAdmissao,
       @Usuario, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);", Parametros(dados, usuario.Id, null), tx, ct));
        }
        catch (SqlException ex) when (EhDuplicado(ex, out var erro))
        {
            throw erro!;
        }

        await _auditoria.RegistrarAsync("INCLUIR", "COLABORADOR_PRESTADOR", id, null, usuario.Id, null, con, tx, ct);

        var linha = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Registro recém-criado não encontrado.");
        await tx.CommitAsync(ct);
        return ParaDto(linha);
    }

    // ------------------------------------------------------------ alteração
    public async Task<ColaboradorDto> AtualizarAsync(int id, ColaboradorEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = Validar(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Colaborador ou prestador não encontrado.");
        await ConferirDuplicidadesAsync(con, tx, dados, id, ct);

        try
        {
            await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.COLABORADOR_PRESTADOR SET TIPO_VINCULO = @TipoVinculo, NOME = @Nome, CPF = @Cpf, RAZAO_SOCIAL = @RazaoSocial,
       NOME_FANTASIA = @NomeFantasia, CNPJ = @Cnpj, TELEFONE = @Telefone, EMAIL = @Email, ENDERECO = @Endereco, NUMERO = @Numero,
       COMPLEMENTO = @Complemento, CIDADE = @Cidade, UF = @Uf, CARGO = @Cargo,
       CTPS_NUMERO = @CtpsNumero, CTPS_SERIE = @CtpsSerie, CTPS_UF = @CtpsUf, CTPS_DATA_EMISSAO = @CtpsDataEmissao,
       PIS_PASEP = @PisPasep, DATA_ADMISSAO = @DataAdmissao,
       DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_CLB_PRT = @Id;", Parametros(dados, usuario.Id, id), tx, ct));
        }
        catch (SqlException ex) when (EhDuplicado(ex, out var erro))
        {
            throw erro!;
        }

        var alterados = new List<string>();
        void Dif(string campo, string? antes, string? depois) { if (!string.Equals(antes, depois, StringComparison.Ordinal)) alterados.Add(campo); }
        Dif("tipoVinculo", atual.TipoVinculo, dados.TipoVinculo);
        Dif("nome", atual.Nome, dados.Nome);
        Dif("cpf", atual.Cpf, dados.Cpf);
        Dif("razaoSocial", atual.RazaoSocial, dados.RazaoSocial);
        Dif("nomeFantasia", atual.NomeFantasia, dados.NomeFantasia);
        Dif("cnpj", atual.Cnpj, dados.Cnpj);
        Dif("telefone", atual.Telefone, dados.Telefone);
        Dif("email", atual.Email, dados.Email);
        Dif("endereco", atual.Endereco, dados.Endereco);
        Dif("numero", atual.Numero, dados.Numero);
        Dif("complemento", atual.Complemento, dados.Complemento);
        Dif("cidade", atual.Cidade, dados.Cidade);
        Dif("uf", atual.Uf, dados.Uf);
        Dif("cargo", atual.Cargo, dados.Cargo);
        Dif("ctpsNumero", atual.CtpsNumero, dados.CtpsNumero);
        Dif("ctpsSerie", atual.CtpsSerie, dados.CtpsSerie);
        Dif("ctpsUf", atual.CtpsUf, dados.CtpsUf);
        Dif("ctpsDataEmissao", Data(atual.CtpsDataEmissao), Data(dados.CtpsDataEmissao));
        Dif("pisPasep", atual.PisPasep, dados.PisPasep);
        Dif("dataAdmissao", Data(atual.DataAdmissao), Data(dados.DataAdmissao));

        await _auditoria.RegistrarAsync("EDITAR", "COLABORADOR_PRESTADOR", id, null, usuario.Id, new { campos = alterados }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Registro não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // --------------------------------------------------------------- status
    public async Task<ColaboradorDto> AlterarStatusAsync(int id, StatusEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d?.Ativo is null) throw Erro.Validacao("ativo", NaoPreenchido);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        _ = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Colaborador ou prestador não encontrado.");

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.COLABORADOR_PRESTADOR SET ATIVO = @Ativo, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_CLB_PRT = @Id;", new { Ativo = d.Ativo.Value, Usuario = usuario.Id, Id = id }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "COLABORADOR_PRESTADOR", id, null, usuario.Id,
            new { campos = new[] { "ativo" }, ativo = d.Ativo.Value }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Registro não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // =========================================================== apoio
    private sealed record Dados(string TipoVinculo, string Nome, string? Cpf, string? RazaoSocial, string? NomeFantasia, string? Cnpj,
        string Telefone, string Email, string Endereco, string Numero, string? Complemento, string Cidade, string Uf, string Cargo,
        string? CtpsNumero, string? CtpsSerie, string? CtpsUf, DateOnly? CtpsDataEmissao, string? PisPasep, DateOnly? DataAdmissao);

    private static string? Data(DateOnly? d) => d?.ToString("O");
    private static string? Data(DateTime? d) => d is null ? null : DateOnly.FromDateTime(d.Value).ToString("O");

    /// <summary>PIS/PASEP: 11 dígitos com dígito verificador (pesos 3,2,9,8,7,6,5,4,3,2).</summary>
    private static bool PisValido(string pis)
    {
        if (!Digitos11.IsMatch(pis) || pis.Distinct().Count() == 1) return false;
        int[] pesos = { 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var soma = 0;
        for (var i = 0; i < 10; i++) soma += (pis[i] - '0') * pesos[i];
        var dv = 11 - (soma % 11);
        if (dv >= 10) dv = 0;
        return dv == pis[10] - '0';
    }

    private static string? Opcional(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static Dados Validar(ColaboradorEntrada? d)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");

        var e = new List<ErroCampo>();
        void Obrigatorio(string campo, string? valor, int max)
        {
            if (string.IsNullOrWhiteSpace(valor)) e.Add(new ErroCampo(campo, NaoPreenchido));
            else if (valor.Trim().Length > max) e.Add(new ErroCampo(campo, $"Máximo de {max} caracteres"));
        }
        void Limite(string campo, string? valor, int max)
        {
            if (!string.IsNullOrWhiteSpace(valor) && valor.Trim().Length > max) e.Add(new ErroCampo(campo, $"Máximo de {max} caracteres"));
        }

        var vinculo = d.TipoVinculo?.Trim();
        if (string.IsNullOrWhiteSpace(vinculo)) e.Add(new ErroCampo("tipoVinculo", NaoPreenchido));
        else if (vinculo is not ("CLT" or "MEI")) e.Add(new ErroCampo("tipoVinculo", "Informe CLT ou MEI"));

        Obrigatorio("nome", d.Nome, 150);
        Obrigatorio("endereco", d.Endereco, 250);
        Obrigatorio("numero", d.Numero, 10);
        Limite("complemento", d.Complemento, 60);
        Obrigatorio("cidade", d.Cidade, 80);
        Obrigatorio("cargo", d.Cargo, 80);
        Limite("razaoSocial", d.RazaoSocial, 150);
        Limite("nomeFantasia", d.NomeFantasia, 150);

        // CPF: obrigatório para CLT; para MEI é opcional, mas se vier precisa ser válido.
        if (string.IsNullOrWhiteSpace(d.Cpf))
        {
            if (vinculo == "CLT") e.Add(new ErroCampo("cpf", NaoPreenchido));
        }
        else if (!Digitos11.IsMatch(d.Cpf.Trim()) || !CpfUtil.Valido(d.Cpf.Trim())) e.Add(new ErroCampo("cpf", "CPF inválido"));

        if (vinculo == "MEI")
        {
            if (string.IsNullOrWhiteSpace(d.RazaoSocial)) e.Add(new ErroCampo("razaoSocial", NaoPreenchido));
            if (string.IsNullOrWhiteSpace(d.Cnpj)) e.Add(new ErroCampo("cnpj", NaoPreenchido));
            else if (!CnpjUtil.Valido(d.Cnpj.Trim())) e.Add(new ErroCampo("cnpj", "CNPJ inválido"));
        }
        else if (vinculo == "CLT")
        {
            if (!string.IsNullOrWhiteSpace(d.RazaoSocial)) e.Add(new ErroCampo("razaoSocial", "Não é aceita para vínculo CLT"));
            if (!string.IsNullOrWhiteSpace(d.Cnpj)) e.Add(new ErroCampo("cnpj", "Não é aceito para vínculo CLT"));
        }

        if (string.IsNullOrWhiteSpace(d.Telefone)) e.Add(new ErroCampo("telefone", NaoPreenchido));
        else if (!Formatos.Telefone.IsMatch(d.Telefone)) e.Add(new ErroCampo("telefone", "Informe somente dígitos, com DDD (10 a 13 números)"));

        if (string.IsNullOrWhiteSpace(d.Email)) e.Add(new ErroCampo("email", NaoPreenchido));
        else if (d.Email.Trim().Length > 150 || !Formatos.Email.IsMatch(d.Email.Trim())) e.Add(new ErroCampo("email", "E-mail inválido"));

        if (string.IsNullOrWhiteSpace(d.Uf)) e.Add(new ErroCampo("uf", NaoPreenchido));
        else if (!Formatos.Ufs.Contains(d.Uf)) e.Add(new ErroCampo("uf", "UF inválida"));

        // Dados da carteira de trabalho: só para CLT, e obrigatórios nesse vínculo.
        var temDadosClt = !string.IsNullOrWhiteSpace(d.CtpsNumero) || !string.IsNullOrWhiteSpace(d.CtpsSerie) || !string.IsNullOrWhiteSpace(d.CtpsUf)
            || d.CtpsDataEmissao is not null || !string.IsNullOrWhiteSpace(d.PisPasep) || d.DataAdmissao is not null;
        if (vinculo == "MEI" && temDadosClt)
            e.Add(new ErroCampo("tipoVinculo", "Os dados da carteira de trabalho só valem para vínculo CLT"));
        if (vinculo == "CLT")
        {
            Obrigatorio("ctpsNumero", d.CtpsNumero, 20);
            Obrigatorio("ctpsSerie", d.CtpsSerie, 10);
            if (string.IsNullOrWhiteSpace(d.CtpsUf)) e.Add(new ErroCampo("ctpsUf", NaoPreenchido));
            else if (!Formatos.Ufs.Contains(d.CtpsUf)) e.Add(new ErroCampo("ctpsUf", "UF inválida"));

            if (d.CtpsDataEmissao is null) e.Add(new ErroCampo("ctpsDataEmissao", NaoPreenchido));
            else if (d.CtpsDataEmissao.Value > DataBrasil.Hoje() || d.CtpsDataEmissao.Value < new DateOnly(1900, 1, 1))
                e.Add(new ErroCampo("ctpsDataEmissao", "Data inválida"));

            if (string.IsNullOrWhiteSpace(d.PisPasep)) e.Add(new ErroCampo("pisPasep", NaoPreenchido));
            else if (!PisValido(d.PisPasep.Trim())) e.Add(new ErroCampo("pisPasep", "PIS/PASEP inválido"));

            if (d.DataAdmissao is null) e.Add(new ErroCampo("dataAdmissao", NaoPreenchido));
            else if (d.DataAdmissao.Value < new DateOnly(1900, 1, 1)) e.Add(new ErroCampo("dataAdmissao", "Data inválida"));
        }

        if (e.Count > 0) throw Erro.Validacao(e);

        var mei = vinculo == "MEI";
        return new Dados(vinculo!, d.Nome!.Trim(), Opcional(d.Cpf), mei ? Opcional(d.RazaoSocial) : null, mei ? Opcional(d.NomeFantasia) : null,
            mei ? Opcional(d.Cnpj) : null, d.Telefone!, d.Email!.Trim(), d.Endereco!.Trim(), d.Numero!.Trim().ToUpperInvariant(),
            Opcional(d.Complemento), d.Cidade!.Trim(), d.Uf!, d.Cargo!.Trim(),
            mei ? null : Opcional(d.CtpsNumero), mei ? null : Opcional(d.CtpsSerie), mei ? null : Opcional(d.CtpsUf),
            mei ? null : d.CtpsDataEmissao, mei ? null : Opcional(d.PisPasep), mei ? null : d.DataAdmissao);
    }

    private static object Parametros(Dados d, int usuarioId, int? id) => new
    {
        Id = id,
        d.TipoVinculo,
        d.Nome,
        Cpf = d.Cpf is null ? null : Sql.Char(d.Cpf, 11),
        d.RazaoSocial,
        d.NomeFantasia,
        Cnpj = d.Cnpj is null ? null : Sql.Char(d.Cnpj, 14),
        d.Telefone,
        d.Email,
        d.Endereco,
        d.Numero,
        d.Complemento,
        d.Cidade,
        d.Uf,
        d.Cargo,
        d.CtpsNumero,
        d.CtpsSerie,
        CtpsUf = d.CtpsUf is null ? null : Sql.Char(d.CtpsUf, 2),
        CtpsDataEmissao = d.CtpsDataEmissao?.ToDateTime(TimeOnly.MinValue),
        PisPasep = d.PisPasep is null ? null : Sql.Char(d.PisPasep, 11),
        DataAdmissao = d.DataAdmissao?.ToDateTime(TimeOnly.MinValue),
        Usuario = usuarioId
    };

    private static async Task ConferirDuplicidadesAsync(SqlConnection con, SqlTransaction tx, Dados d, int? ignorarId, CancellationToken ct)
    {
        if (d.Cpf is not null)
        {
            var cpf = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.COLABORADOR_PRESTADOR WHERE CPF = @Cpf AND (@Ignorar IS NULL OR CODIGO_CLB_PRT <> @Ignorar);",
                new { Cpf = Sql.Char(d.Cpf, 11), Ignorar = ignorarId }, tx, ct));
            if (cpf > 0) throw CpfDuplicado();
        }
        if (d.PisPasep is not null)
        {
            var pis = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.COLABORADOR_PRESTADOR WHERE PIS_PASEP = @Pis AND (@Ignorar IS NULL OR CODIGO_CLB_PRT <> @Ignorar);",
                new { Pis = Sql.Char(d.PisPasep, 11), Ignorar = ignorarId }, tx, ct));
            if (pis > 0) throw PisDuplicado();
        }
        if (d.Cnpj is not null)
        {
            var cnpj = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.COLABORADOR_PRESTADOR WHERE CNPJ = @Cnpj AND (@Ignorar IS NULL OR CODIGO_CLB_PRT <> @Ignorar);",
                new { Cnpj = Sql.Char(d.Cnpj, 14), Ignorar = ignorarId }, tx, ct));
            if (cnpj > 0) throw CnpjDuplicado();
        }
    }

    private static ApiException CpfDuplicado() => Erro.Conflito("Este CPF já está cadastrado.", "CPF_DUPLICADO");
    private static ApiException PisDuplicado() => Erro.Conflito("Este PIS/PASEP já está cadastrado.", "PIS_DUPLICADO");
    private static ApiException CnpjDuplicado() => Erro.Conflito("Este CNPJ já está cadastrado.", "CNPJ_DUPLICADO");

    private static bool EhDuplicado(SqlException ex, out ApiException? erro)
    {
        erro = null;
        if (ex.Number is not (2601 or 2627)) return false;
        if (ex.Message.Contains("UX_COLABORADOR_PRESTADOR_CPF", StringComparison.OrdinalIgnoreCase)) erro = CpfDuplicado();
        else if (ex.Message.Contains("UX_COLABORADOR_PRESTADOR_PIS", StringComparison.OrdinalIgnoreCase)) erro = PisDuplicado();
        else if (ex.Message.Contains("UX_COLABORADOR_PRESTADOR_CNPJ", StringComparison.OrdinalIgnoreCase)) erro = CnpjDuplicado();
        return erro is not null;
    }

    private static Task<ColaboradorLinha?> CarregarAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<ColaboradorLinha>(Sql.Cmd(SelectColaborador + " WHERE C.CODIGO_CLB_PRT = @Id;", new { Id = id }, tx, ct));

    private static ColaboradorDto ParaDto(ColaboradorLinha l) => new()
    {
        Id = l.Id,
        Ativo = l.Ativo,
        TipoVinculo = l.TipoVinculo,
        Nome = l.Nome,
        Cpf = l.Cpf,
        RazaoSocial = l.RazaoSocial,
        NomeFantasia = l.NomeFantasia,
        Cnpj = l.Cnpj,
        Telefone = l.Telefone,
        Email = l.Email,
        Endereco = l.Endereco,
        Numero = l.Numero,
        Complemento = l.Complemento,
        Cidade = l.Cidade,
        Uf = l.Uf,
        Cargo = l.Cargo,
        CtpsNumero = l.CtpsNumero,
        CtpsSerie = l.CtpsSerie,
        CtpsUf = l.CtpsUf,
        CtpsDataEmissao = l.CtpsDataEmissao is null ? null : DateOnly.FromDateTime(l.CtpsDataEmissao.Value),
        PisPasep = l.PisPasep,
        DataAdmissao = l.DataAdmissao is null ? null : DateOnly.FromDateTime(l.DataAdmissao.Value),
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
