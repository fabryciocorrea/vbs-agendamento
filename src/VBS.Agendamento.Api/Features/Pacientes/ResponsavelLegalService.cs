using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Pacientes;

/// <summary>
/// Responsável legal do paciente menor de idade (E4, E5): cadastro, busca por CPF, consulta e alteração.
/// A alteração vale para todos os pacientes vinculados (E5). O responsável precisa ter 18 anos ou mais.
/// </summary>
public sealed class ResponsavelLegalService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private static readonly Regex Digitos11 = new(@"^[0-9]{11}$", RegexOptions.Compiled);

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public ResponsavelLegalService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private sealed class Linha
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string Cpf { get; set; } = string.Empty;
        public string? Rg { get; set; }
        public DateTime DataNascimento { get; set; }
        public string Telefone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Escolaridade { get; set; }
        public string? Endereco { get; set; }
        public string? Numero { get; set; }
        public string? Complemento { get; set; }
        public string? Cidade { get; set; }
        public string? Uf { get; set; }
        public int QuantidadePacientes { get; set; }
    }

    private sealed class ConsentimentoLinha
    {
        public string Finalidade { get; set; } = string.Empty;
        public bool Concedido { get; set; }
        public string Origem { get; set; } = string.Empty;
        public DateTime DataRegistro { get; set; }
        public int? RegistradoPorId { get; set; }
        public string? RegistradoPorNome { get; set; }
    }

    private const string SelectResponsavel = @"
SELECT R.CODIGO_RESPONSAVEL AS Id, R.NOME AS Nome, R.CPF AS Cpf, R.RG AS Rg, R.DATA_NASCIMENTO AS DataNascimento,
       R.TELEFONE AS Telefone, R.EMAIL AS Email, R.ESCOLARIDADE AS Escolaridade, R.ENDERECO AS Endereco, R.NUMERO AS Numero,
       R.COMPLEMENTO AS Complemento, R.CIDADE AS Cidade, R.UF AS Uf,
       (SELECT COUNT(1) FROM DBO.PACIENTE X WHERE X.CODIGO_RESPONSAVEL_LEGAL = R.CODIGO_RESPONSAVEL) AS QuantidadePacientes
FROM DBO.RESPONSAVEL_LEGAL R";

    private sealed record Dados(string Nome, string Cpf, string? Rg, DateOnly DataNascimento, string Telefone, string Email,
        string? Escolaridade, string Endereco, string Numero, string? Complemento, string Cidade, string Uf,
        List<ConsentimentoEntrada>? Consentimentos);

    // ------------------------------------------------------------------ cadastro
    public async Task<ResponsavelLegalDto> CriarAsync(ResponsavelLegalEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = Validar(d, exigirConsentimentos: true);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await ConferirCpfDuplicadoAsync(con, tx, dados.Cpf, null, ct);

        int id;
        try
        {
            id = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.RESPONSAVEL_LEGAL (NOME, CPF, RG, DATA_NASCIMENTO, TELEFONE, EMAIL, ESCOLARIDADE, ENDERECO, NUMERO, COMPLEMENTO, CIDADE, UF)
VALUES (@Nome, @Cpf, @Rg, @DataNascimento, @Telefone, @Email, @Escolaridade, @Endereco, @Numero, @Complemento, @Cidade, @Uf);
SELECT CAST(SCOPE_IDENTITY() AS INT);", Parametros(dados, null), tx, ct));
        }
        catch (SqlException ex) when (EhDuplicado(ex))
        {
            throw CpfDuplicado();
        }

        await GravarConsentimentosAsync(con, tx, id, dados.Consentimentos, usuario.Id, ct);
        await _auditoria.RegistrarAsync("INCLUIR", "RESPONSAVEL_LEGAL", id, null, usuario.Id, null, con, tx, ct);
        var saida = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Responsável recém-criado não encontrado.");
        await tx.CommitAsync(ct);
        return saida;
    }

    // ------------------------------------------------------------- busca por CPF
    public async Task<ResponsavelLegalDto> BuscarPorCpfAsync(BuscaCpfEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var cpf = d?.Cpf?.Trim();
        if (string.IsNullOrEmpty(cpf)) throw Erro.Validacao("cpf", NaoPreenchido);
        if (!Digitos11.IsMatch(cpf) || !CpfUtil.Valido(cpf)) throw Erro.Validacao("cpf", "CPF inválido");

        await using var con = await _db.AbrirAsync(ct);
        var linha = await con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(
            SelectResponsavel + " WHERE R.CPF = @Cpf;", new { Cpf = Sql.Char(cpf, 11) }, ct: ct));
        if (linha is null)
            throw Erro.NaoEncontrado("Este responsável não está cadastrado. Cadastre-o para concluir o cadastro do paciente.",
                "RESPONSAVEL_NAO_CADASTRADO");
        return await MontarAsync(con, null, linha, ct);
    }

    // ------------------------------------------------------------------ consulta
    public async Task<ResponsavelLegalDto> ObterAsync(int id, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);

        // RN24: o médico só consulta o responsável de um paciente seu; nos demais casos, 404.
        if (usuario.EhMedico)
        {
            var pode = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.PACIENTE
WHERE CODIGO_RESPONSAVEL_LEGAL = @Id AND CODIGO_PROFISSIONAL_RESPONSAVEL = @Medico;",
                new { Id = id, Medico = usuario.ProfissionalId }, ct: ct));
            if (pode == 0) throw Erro.NaoEncontrado("Responsável legal não encontrado.");
        }

        return await CarregarAsync(con, null, id, ct) ?? throw Erro.NaoEncontrado("Responsável legal não encontrado.");
    }

    // ------------------------------------------------------------------ alteração
    public async Task<ResponsavelLegalDto> AtualizarAsync(int id, ResponsavelLegalEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = Validar(d, exigirConsentimentos: false);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(
            SelectResponsavel + " WHERE R.CODIGO_RESPONSAVEL = @Id;", new { Id = id }, tx, ct))
            ?? throw Erro.NaoEncontrado("Responsável legal não encontrado.");

        await ConferirCpfDuplicadoAsync(con, tx, dados.Cpf, id, ct);

        // Sem consentimento de tratamento de dados registrado, a alteração precisa trazer os consentimentos (LGPD).
        if (dados.Consentimentos is null)
        {
            var tem = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.CONSENTIMENTO_LGPD_RESPONSAVEL WHERE CODIGO_RESPONSAVEL = @Id AND FINALIDADE = 'TRATAMENTO_DADOS' AND CONCEDIDO = 1;",
                new { Id = id }, tx, ct));
            if (tem == 0)
                throw Erro.Validacao("consentimentos", "Informe o consentimento de tratamento de dados do responsável legal");
        }

        try
        {
            await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.RESPONSAVEL_LEGAL SET NOME = @Nome, CPF = @Cpf, RG = @Rg, DATA_NASCIMENTO = @DataNascimento,
       TELEFONE = @Telefone, EMAIL = @Email, ESCOLARIDADE = @Escolaridade, ENDERECO = @Endereco, NUMERO = @Numero,
       COMPLEMENTO = @Complemento, CIDADE = @Cidade, UF = @Uf
WHERE CODIGO_RESPONSAVEL = @Id;", Parametros(dados, id), tx, ct));
        }
        catch (SqlException ex) when (EhDuplicado(ex))
        {
            throw CpfDuplicado();
        }

        var campos = new List<string>();
        void Dif(string campo, object? antes, object? depois) { if (!Equals(antes, depois)) campos.Add(campo); }
        Dif("nome", atual.Nome, dados.Nome);
        Dif("cpf", atual.Cpf, dados.Cpf);
        Dif("rg", atual.Rg, dados.Rg);
        Dif("dataNascimento", DateOnly.FromDateTime(atual.DataNascimento), dados.DataNascimento);
        Dif("telefone", atual.Telefone, dados.Telefone);
        Dif("email", atual.Email, dados.Email);
        Dif("escolaridade", atual.Escolaridade, dados.Escolaridade);
        Dif("endereco", atual.Endereco, dados.Endereco);
        Dif("numero", atual.Numero, dados.Numero);
        Dif("complemento", atual.Complemento, dados.Complemento);
        Dif("cidade", atual.Cidade, dados.Cidade);
        Dif("uf", atual.Uf, dados.Uf);
        if (dados.Consentimentos is not null) campos.Add("consentimentos");

        await GravarConsentimentosAsync(con, tx, id, dados.Consentimentos, usuario.Id, ct);
        await _auditoria.RegistrarAsync("EDITAR", "RESPONSAVEL_LEGAL", id, null, usuario.Id, new { campos }, con, tx, ct);
        var saida = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Responsável não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return saida;
    }

    // ===================================================================== apoio
    private static Dados Validar(ResponsavelLegalEntrada? d, bool exigirConsentimentos)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var e = new List<ErroCampo>();

        if (string.IsNullOrWhiteSpace(d.Nome)) e.Add(new ErroCampo("nome", NaoPreenchido));
        else if (d.Nome.Trim().Length > 150) e.Add(new ErroCampo("nome", "Máximo de 150 caracteres"));

        if (string.IsNullOrWhiteSpace(d.Cpf)) e.Add(new ErroCampo("cpf", NaoPreenchido));
        else if (!Digitos11.IsMatch(d.Cpf.Trim()) || !CpfUtil.Valido(d.Cpf.Trim())) e.Add(new ErroCampo("cpf", "CPF inválido"));

        if (!string.IsNullOrWhiteSpace(d.Rg) && d.Rg.Trim().Length > 20) e.Add(new ErroCampo("rg", "Máximo de 20 caracteres"));

        // E5: o responsável deve ter 18 anos ou mais.
        var hoje = DataBrasil.Hoje();
        if (d.DataNascimento is null) e.Add(new ErroCampo("dataNascimento", NaoPreenchido));
        else if (d.DataNascimento.Value > hoje || d.DataNascimento.Value < new DateOnly(1900, 1, 1)) e.Add(new ErroCampo("dataNascimento", "Data inválida"));
        else if (DataBrasil.Idade(d.DataNascimento.Value, hoje) < 18) e.Add(new ErroCampo("dataNascimento", "O responsável legal deve ter 18 anos ou mais"));

        if (string.IsNullOrWhiteSpace(d.Telefone)) e.Add(new ErroCampo("telefone", NaoPreenchido));
        else if (!Formatos.Telefone.IsMatch(d.Telefone)) e.Add(new ErroCampo("telefone", "Informe somente dígitos, com DDD (10 a 13 números)"));

        if (string.IsNullOrWhiteSpace(d.Email)) e.Add(new ErroCampo("email", NaoPreenchido));
        else if (d.Email.Trim().Length > 150 || !Formatos.Email.IsMatch(d.Email.Trim())) e.Add(new ErroCampo("email", "E-mail inválido"));

        if (!string.IsNullOrWhiteSpace(d.Escolaridade) && d.Escolaridade.Trim().Length > 40) e.Add(new ErroCampo("escolaridade", "Máximo de 40 caracteres"));
        void Obrigatorio(string campo, string? valor, int max)
        {
            if (string.IsNullOrWhiteSpace(valor)) e.Add(new ErroCampo(campo, NaoPreenchido));
            else if (valor.Trim().Length > max) e.Add(new ErroCampo(campo, $"Máximo de {max} caracteres"));
        }
        Obrigatorio("endereco", d.Endereco, 250);
        Obrigatorio("numero", d.Numero, 10);
        Obrigatorio("cidade", d.Cidade, 80);
        if (!string.IsNullOrWhiteSpace(d.Complemento) && d.Complemento.Trim().Length > 60) e.Add(new ErroCampo("complemento", "Máximo de 60 caracteres"));
        if (string.IsNullOrWhiteSpace(d.Uf)) e.Add(new ErroCampo("uf", NaoPreenchido));
        else if (!Formatos.Ufs.Contains(d.Uf)) e.Add(new ErroCampo("uf", "UF inválida"));

        // Consentimentos LGPD do responsável: um item por finalidade; o tratamento de dados precisa estar concedido.
        if (d.Consentimentos is null)
        {
            if (exigirConsentimentos) e.Add(new ErroCampo("consentimentos", "Informe os dois consentimentos: TRATAMENTO_DADOS e CONTATO_SMS_WHATSAPP"));
        }
        else if (d.Consentimentos.Count != 2)
        {
            e.Add(new ErroCampo("consentimentos", "Informe os dois consentimentos: TRATAMENTO_DADOS e CONTATO_SMS_WHATSAPP"));
        }
        else
        {
            var finalidades = new HashSet<string>();
            for (var i = 0; i < d.Consentimentos.Count; i++)
            {
                var c = d.Consentimentos[i];
                var prefixo = $"consentimentos[{i}]";
                if (c.Finalidade is not ("TRATAMENTO_DADOS" or "CONTATO_SMS_WHATSAPP")) e.Add(new ErroCampo($"{prefixo}.finalidade", "Finalidade inválida"));
                else if (!finalidades.Add(c.Finalidade)) e.Add(new ErroCampo($"{prefixo}.finalidade", "Finalidade repetida"));
                if (c.Concedido is null) e.Add(new ErroCampo($"{prefixo}.concedido", NaoPreenchido));
                if (c.Origem is not ("PRESENCIAL" or "TELEFONE")) e.Add(new ErroCampo($"{prefixo}.origem", "Informe PRESENCIAL ou TELEFONE"));
            }
            var tratamento = d.Consentimentos.FirstOrDefault(c => c.Finalidade == "TRATAMENTO_DADOS");
            if (tratamento is not null && tratamento.Concedido != true)
                e.Add(new ErroCampo("consentimentos", "O consentimento de tratamento de dados é obrigatório"));
        }

        if (e.Count > 0) throw Erro.Validacao(e);

        string? Opc(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        return new Dados(d.Nome!.Trim(), d.Cpf!.Trim(), Opc(d.Rg), d.DataNascimento!.Value, d.Telefone!, d.Email!.Trim(),
            Opc(d.Escolaridade), d.Endereco!.Trim(), d.Numero!.Trim().ToUpperInvariant(), Opc(d.Complemento), d.Cidade!.Trim(), d.Uf!,
            d.Consentimentos);
    }

    private static object Parametros(Dados d, int? id) => new
    {
        Id = id,
        d.Nome,
        Cpf = Sql.Char(d.Cpf, 11),
        d.Rg,
        DataNascimento = d.DataNascimento.ToDateTime(TimeOnly.MinValue),
        d.Telefone,
        d.Email,
        d.Escolaridade,
        d.Endereco,
        d.Numero,
        d.Complemento,
        d.Cidade,
        d.Uf
    };

    private static async Task ConferirCpfDuplicadoAsync(SqlConnection con, SqlTransaction tx, string cpf, int? ignorar, CancellationToken ct)
    {
        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.RESPONSAVEL_LEGAL WHERE CPF = @Cpf AND (@Ignorar IS NULL OR CODIGO_RESPONSAVEL <> @Ignorar);",
            new { Cpf = Sql.Char(cpf, 11), Ignorar = ignorar }, tx, ct));
        if (existe > 0) throw CpfDuplicado();
    }

    private static ApiException CpfDuplicado() =>
        Erro.Conflito("Este CPF já está cadastrado como responsável legal", "CPF_DUPLICADO");

    private static bool EhDuplicado(SqlException ex) =>
        ex.Number is 2601 or 2627 && ex.Message.Contains("UQ_RESPONSAVEL_LEGAL_CPF", StringComparison.OrdinalIgnoreCase);

    private static async Task GravarConsentimentosAsync(SqlConnection con, SqlTransaction tx, int id, List<ConsentimentoEntrada>? consentimentos, int usuarioId, CancellationToken ct)
    {
        if (consentimentos is null) return;
        foreach (var c in consentimentos)
        {
            await con.ExecuteAsync(Sql.Cmd(@"
INSERT INTO DBO.CONSENTIMENTO_LGPD_RESPONSAVEL (CODIGO_RESPONSAVEL, FINALIDADE, CONCEDIDO, ORIGEM, REGISTRO_USUARIO)
VALUES (@Responsavel, @Finalidade, @Concedido, @Origem, @Usuario);",
                new { Responsavel = id, c.Finalidade, Concedido = c.Concedido!.Value, c.Origem, Usuario = usuarioId }, tx, ct));
        }
    }

    private static async Task<ResponsavelLegalDto?> CarregarAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct)
    {
        var l = await con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(SelectResponsavel + " WHERE R.CODIGO_RESPONSAVEL = @Id;", new { Id = id }, tx, ct));
        return l is null ? null : await MontarAsync(con, tx, l, ct);
    }

    private static async Task<ResponsavelLegalDto> MontarAsync(SqlConnection con, SqlTransaction? tx, Linha l, CancellationToken ct)
    {
        // Estado atual de cada finalidade: a linha mais recente.
        var consentimentos = (await con.QueryAsync<ConsentimentoLinha>(Sql.Cmd(@"
SELECT X.FINALIDADE AS Finalidade, X.CONCEDIDO AS Concedido, X.ORIGEM AS Origem, X.DATA_REGISTRO AS DataRegistro,
       X.REGISTRO_USUARIO AS RegistradoPorId, U.NOME_COMPLETO AS RegistradoPorNome
FROM (SELECT C.*, ROW_NUMBER() OVER (PARTITION BY C.FINALIDADE ORDER BY C.DATA_REGISTRO DESC, C.CODIGO_CONSENTIMENTO DESC) AS ORDEM
      FROM DBO.CONSENTIMENTO_LGPD_RESPONSAVEL C WHERE C.CODIGO_RESPONSAVEL = @Id) X
LEFT JOIN DBO.USUARIO U ON U.CODIGO_USUARIO = X.REGISTRO_USUARIO
WHERE X.ORDEM = 1
ORDER BY X.FINALIDADE;", new { l.Id }, tx, ct))).ToList();

        var dto = new ResponsavelLegalDto
        {
            Id = l.Id,
            Nome = l.Nome,
            Cpf = l.Cpf,
            Rg = l.Rg,
            DataNascimento = DateOnly.FromDateTime(l.DataNascimento),
            Telefone = l.Telefone,
            Email = l.Email,
            Escolaridade = l.Escolaridade,
            Endereco = l.Endereco,
            Numero = l.Numero,
            Complemento = l.Complemento,
            Cidade = l.Cidade,
            Uf = l.Uf,
            ConsentimentosAtuais = consentimentos.Select(c => new ConsentimentoDto
            {
                Finalidade = c.Finalidade,
                Concedido = c.Concedido,
                Origem = c.Origem,
                DataRegistro = DataBrasil.Utc(c.DataRegistro),
                RegistradoPor = c.RegistradoPorId.HasValue
                    ? new UsuarioReferenciaDto { Id = c.RegistradoPorId.Value, NomeCompleto = c.RegistradoPorNome ?? string.Empty }
                    : null
            }).ToList(),
            QuantidadePacientes = l.QuantidadePacientes
        };

        return dto;
    }
}
