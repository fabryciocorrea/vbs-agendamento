using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Profissionais;

/// <summary>
/// Bloco 3 do contrato: profissionais e expediente. Regras: RN11 (especialidades e unidades do profissional),
/// RN13 (CPF e conselho únicos), RN14 (inativar, não excluir), F3/RN03 (grade de horários), F4 (não inativa com consulta marcada).
/// O cadastro não cria usuário: o acesso é dado no cadastro de usuário, que aponta para o profissional.
/// </summary>
public sealed class ProfissionalService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private static readonly Regex Hora = new(@"^([01][0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.Compiled);
    private static readonly Regex Digitos11 = new(@"^[0-9]{11}$", RegexOptions.Compiled);
    private const string StatusAtivos = "'AGENDADA', 'CONFIRMADA', 'CHECK_IN', 'EM_ATENDIMENTO'";

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public ProfissionalService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private const string SelectProfissional = @"
SELECT P.CODIGO_PROFISSIONAL AS Id, P.ATIVO AS Ativo, P.NOME AS Nome, P.CPF AS Cpf, P.RG AS Rg,
       P.DATA_NASCIMENTO AS DataNascimento, P.TELEFONE AS Telefone, P.EMAIL AS Email, P.ENDERECO AS Endereco, P.NUMERO AS Numero, P.COMPLEMENTO AS Complemento,
       P.CIDADE AS Cidade, P.UF AS Uf, P.ESCOLARIDADE AS Escolaridade, P.REGISTRO_PROFISSIONAL AS RegistroProfissional,
       P.NUMERO_CONSELHO AS NumeroConselho, P.UF_CONSELHO AS UfConselho,
       P.DATA_CRIACAO AS CriadoEm, P.CRIACAO_USUARIO AS CriadoPorId, UC.NOME_COMPLETO AS CriadoPorNome,
       P.DATA_ATUALIZACAO AS AtualizadoEm, P.ATUALIZACAO_USUARIO AS AtualizadoPorId, UA.NOME_COMPLETO AS AtualizadoPorNome
FROM DBO.PROFISSIONAL P
LEFT JOIN DBO.USUARIO UC ON UC.CODIGO_USUARIO = P.CRIACAO_USUARIO
LEFT JOIN DBO.USUARIO UA ON UA.CODIGO_USUARIO = P.ATUALIZACAO_USUARIO";

    private sealed class Linha
    {
        public int Id { get; set; }
        public bool Ativo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string Cpf { get; set; } = string.Empty;
        public string? Rg { get; set; }
        public DateTime DataNascimento { get; set; }
        public string Telefone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Endereco { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string? Complemento { get; set; }
        public string Cidade { get; set; } = string.Empty;
        public string Uf { get; set; } = string.Empty;
        public string? Escolaridade { get; set; }
        public string RegistroProfissional { get; set; } = string.Empty;
        public string NumeroConselho { get; set; } = string.Empty;
        public string UfConselho { get; set; } = string.Empty;
        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }
        public string? CriadoPorNome { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public int? AtualizadoPorId { get; set; }
        public string? AtualizadoPorNome { get; set; }
    }

    private sealed class ResumoLinha
    {
        public int Id { get; set; }
        public bool Ativo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string Registro { get; set; } = string.Empty;
        public string RegistroProfissional { get; set; } = string.Empty;
        public string NumeroConselho { get; set; } = string.Empty;
        public string UfConselho { get; set; } = string.Empty;
    }

    private sealed class VinculoLinha
    {
        public int ProfissionalId { get; set; }
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
    }

    private sealed class HorarioLinha
    {
        public int UnidadeId { get; set; }
        public int DiaSemana { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public TimeSpan HoraInicio { get; set; }
        public TimeSpan HoraFim { get; set; }
    }

    private sealed class ForaLinha
    {
        public Guid Id { get; set; }
        public DateTime DataInicio { get; set; }
        public int Total { get; set; }
    }

    private sealed class ReferenciaLinha
    {
        public int Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public bool Ativo { get; set; }
    }

    private sealed class ContagemLinha
    {
        public string Nome { get; set; } = string.Empty;
        public int Quantidade { get; set; }
    }

    // ---------------------------------------------------------------- lista
    public async Task<ProfissionalPaginaDto> ListarAsync(FiltroProfissionais f, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            onde.Append(" AND (P.NOME LIKE @Busca OR P.NUMERO_CONSELHO LIKE @Busca)");
            p.Add("Busca", "%" + Formatos.EscaparLike(f.Busca.Trim()) + "%");
        }
        if (f.EspecialidadeId.HasValue)
        {
            onde.Append(" AND EXISTS (SELECT 1 FROM DBO.PROFISSIONAL_ESPECIALIDADE PE WHERE PE.CODIGO_PROFISSIONAL = P.CODIGO_PROFISSIONAL AND PE.CODIGO_ESPECIALIDADE = @EspecialidadeId)");
            p.Add("EspecialidadeId", f.EspecialidadeId.Value);
        }
        if (f.UnidadeId.HasValue)
        {
            onde.Append(" AND EXISTS (SELECT 1 FROM DBO.PROFISSIONAL_UNIDADE PU WHERE PU.CODIGO_PROFISSIONAL = P.CODIGO_PROFISSIONAL AND PU.CODIGO_UNIDADE = @UnidadeId)");
            p.Add("UnidadeId", f.UnidadeId.Value);
        }
        if (f.Ativo.HasValue)
        {
            onde.Append(" AND P.ATIVO = @Ativo");
            p.Add("Ativo", f.Ativo.Value);
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.PROFISSIONAL P" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = (await con.QueryAsync<ResumoLinha>(Sql.Cmd(@"
SELECT P.CODIGO_PROFISSIONAL AS Id, P.ATIVO AS Ativo, P.NOME AS Nome,
       P.REGISTRO_PROFISSIONAL + ' ' + P.NUMERO_CONSELHO + '/' + P.UF_CONSELHO AS Registro,
       P.REGISTRO_PROFISSIONAL AS RegistroProfissional, P.NUMERO_CONSELHO AS NumeroConselho, P.UF_CONSELHO AS UfConselho
FROM DBO.PROFISSIONAL P" + onde + " ORDER BY P.NOME, P.CODIGO_PROFISSIONAL OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct))).ToList();

        var ids = linhas.Select(l => l.Id).ToList();
        var especialidades = await CarregarVinculosAsync(con, null, "ESPECIALIDADE", ids, ct);
        var unidades = await CarregarVinculosAsync(con, null, "UNIDADE", ids, ct);

        return new ProfissionalPaginaDto
        {
            Itens = linhas.Select(l => new ProfissionalResumoDto
            {
                Id = l.Id,
                Ativo = l.Ativo,
                Nome = l.Nome,
                Registro = l.Registro,
                RegistroProfissional = l.RegistroProfissional,
                NumeroConselho = l.NumeroConselho,
                UfConselho = l.UfConselho,
                Especialidades = especialidades.TryGetValue(l.Id, out var e) ? e : new List<ReferenciaDto>(),
                Unidades = unidades.TryGetValue(l.Id, out var u) ? u : new List<ReferenciaDto>()
            }).ToList(),
            Pagina = f.Pagina,
            Tamanho = f.Tamanho,
            Total = total
        };
    }

    // ------------------------------------------------------------- consulta
    public async Task<ProfissionalDto> ObterAsync(int id, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        return await MontarAsync(con, null, id, ct) ?? throw Erro.NaoEncontrado("Profissional não encontrado.");
    }

    // ------------------------------------------------------------- cadastro
    public async Task<ProfissionalDto> CriarAsync(ProfissionalEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = ValidarDados(d);
        var horarios = ValidarHorarios(d!.Horarios, dados.UnidadeIds.ToHashSet(), exigeAtendimento: true);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await ConferirDuplicidadesAsync(con, tx, dados, null, ct);
        await ConferirReferenciasAsync(con, tx, dados, new HashSet<int>(), new HashSet<int>(), ct);

        int id;
        try
        {
            id = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.PROFISSIONAL (NOME, CPF, RG, DATA_NASCIMENTO, TELEFONE, EMAIL, ENDERECO, NUMERO, COMPLEMENTO, CIDADE, UF, ESCOLARIDADE,
                              REGISTRO_PROFISSIONAL, NUMERO_CONSELHO, UF_CONSELHO, CRIACAO_USUARIO, ATUALIZACAO_USUARIO)
VALUES (@Nome, @Cpf, @Rg, @DataNascimento, @Telefone, @Email, @Endereco, @Numero, @Complemento, @Cidade, @Uf, @Escolaridade,
        @RegistroProfissional, @NumeroConselho, @UfConselho, @Usuario, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);", Parametros(dados, usuario.Id, null), tx, ct));
        }
        catch (SqlException ex) when (EhDuplicado(ex, out var duplicado))
        {
            throw duplicado!;
        }

        foreach (var e in dados.EspecialidadeIds)
            await con.ExecuteAsync(Sql.Cmd("INSERT INTO DBO.PROFISSIONAL_ESPECIALIDADE (CODIGO_PROFISSIONAL, CODIGO_ESPECIALIDADE) VALUES (@P, @V);", new { P = id, V = e }, tx, ct));
        foreach (var u in dados.UnidadeIds)
            await con.ExecuteAsync(Sql.Cmd("INSERT INTO DBO.PROFISSIONAL_UNIDADE (CODIGO_PROFISSIONAL, CODIGO_UNIDADE) VALUES (@P, @V);", new { P = id, V = u }, tx, ct));
        await GravarHorariosAsync(con, tx, id, horarios, ct);

        await _auditoria.RegistrarAsync("INCLUIR", "PROFISSIONAL", id, null, usuario.Id, null, con, tx, ct);

        var criado = await MontarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Profissional recém-criado não encontrado.");
        await tx.CommitAsync(ct);
        return criado;
    }

    // ------------------------------------------------------------ alteração
    public async Task<ProfissionalDto> AtualizarAsync(int id, ProfissionalAtualizacao? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var dados = ValidarDados(d);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Profissional não encontrado.");
        await ConferirDuplicidadesAsync(con, tx, dados, id, ct);

        var espAtuais = (await con.QueryAsync<int>(Sql.Cmd("SELECT CODIGO_ESPECIALIDADE FROM DBO.PROFISSIONAL_ESPECIALIDADE WHERE CODIGO_PROFISSIONAL = @Id;", new { Id = id }, tx, ct))).ToHashSet();
        var uniAtuais = (await con.QueryAsync<int>(Sql.Cmd("SELECT CODIGO_UNIDADE FROM DBO.PROFISSIONAL_UNIDADE WHERE CODIGO_PROFISSIONAL = @Id;", new { Id = id }, tx, ct))).ToHashSet();

        await ConferirReferenciasAsync(con, tx, dados, espAtuais, uniAtuais, ct);

        var espRemovidas = espAtuais.Except(dados.EspecialidadeIds).ToList();
        var uniRemovidas = uniAtuais.Except(dados.UnidadeIds).ToList();
        var espNovas = dados.EspecialidadeIds.Except(espAtuais).ToList();
        var uniNovas = dados.UnidadeIds.Except(uniAtuais).ToList();

        await ConferirVinculosEmUsoAsync(con, tx, id, espRemovidas, uniRemovidas, ct);

        try
        {
            await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.PROFISSIONAL SET NOME = @Nome, CPF = @Cpf, RG = @Rg, DATA_NASCIMENTO = @DataNascimento, TELEFONE = @Telefone,
       EMAIL = @Email, ENDERECO = @Endereco, NUMERO = @Numero, COMPLEMENTO = @Complemento, CIDADE = @Cidade, UF = @Uf, ESCOLARIDADE = @Escolaridade,
       REGISTRO_PROFISSIONAL = @RegistroProfissional, NUMERO_CONSELHO = @NumeroConselho, UF_CONSELHO = @UfConselho,
       DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_PROFISSIONAL = @Id;", Parametros(dados, usuario.Id, id), tx, ct));
        }
        catch (SqlException ex) when (EhDuplicado(ex, out var duplicado))
        {
            throw duplicado!;
        }

        try
        {
            foreach (var e in espRemovidas)
                await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.PROFISSIONAL_ESPECIALIDADE WHERE CODIGO_PROFISSIONAL = @P AND CODIGO_ESPECIALIDADE = @V;", new { P = id, V = e }, tx, ct));
            foreach (var u in uniRemovidas)
                await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.PROFISSIONAL_UNIDADE WHERE CODIGO_PROFISSIONAL = @P AND CODIGO_UNIDADE = @V;", new { P = id, V = u }, tx, ct));
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            // O banco não deixa retirar um vínculo que já foi usado em alguma consulta (mesmo antiga).
            throw new ApiException(409, "Conflito",
                "Uma especialidade ou unidade retirada já foi usada em consultas deste profissional e não pode ser desvinculada. Para deixar de usá-la, inative o profissional ou o cadastro dela.",
                "VINCULO_EM_USO");
        }

        foreach (var e in espNovas)
            await con.ExecuteAsync(Sql.Cmd("INSERT INTO DBO.PROFISSIONAL_ESPECIALIDADE (CODIGO_PROFISSIONAL, CODIGO_ESPECIALIDADE) VALUES (@P, @V);", new { P = id, V = e }, tx, ct));
        foreach (var u in uniNovas)
            await con.ExecuteAsync(Sql.Cmd("INSERT INTO DBO.PROFISSIONAL_UNIDADE (CODIGO_PROFISSIONAL, CODIGO_UNIDADE) VALUES (@P, @V);", new { P = id, V = u }, tx, ct));

        var alterados = new List<string>();
        void Dif(string campo, string? antes, string? depois) { if (!string.Equals(antes, depois, StringComparison.Ordinal)) alterados.Add(campo); }
        Dif("nome", atual.Nome, dados.Nome);
        Dif("cpf", atual.Cpf, dados.Cpf);
        Dif("rg", atual.Rg, dados.Rg);
        Dif("dataNascimento", DateOnly.FromDateTime(atual.DataNascimento).ToString("O"), dados.DataNascimento.ToString("O"));
        Dif("telefone", atual.Telefone, dados.Telefone);
        Dif("email", atual.Email, dados.Email);
        Dif("endereco", atual.Endereco, dados.Endereco);
        Dif("numero", atual.Numero, dados.Numero);
        Dif("complemento", atual.Complemento, dados.Complemento);
        Dif("cidade", atual.Cidade, dados.Cidade);
        Dif("uf", atual.Uf, dados.Uf);
        Dif("escolaridade", atual.Escolaridade, dados.Escolaridade);
        Dif("registroProfissional", atual.RegistroProfissional, dados.RegistroProfissional);
        Dif("numeroConselho", atual.NumeroConselho, dados.NumeroConselho);
        Dif("ufConselho", atual.UfConselho, dados.UfConselho);
        if (espRemovidas.Count > 0 || espNovas.Count > 0) alterados.Add("especialidadeIds");
        if (uniRemovidas.Count > 0 || uniNovas.Count > 0) alterados.Add("unidadeIds");

        await _auditoria.RegistrarAsync("EDITAR", "PROFISSIONAL", id, null, usuario.Id, new { campos = alterados }, con, tx, ct);

        var nova = await MontarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Profissional não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return nova;
    }

    // --------------------------------------------------------------- status
    public async Task<ProfissionalDto> AlterarStatusAsync(int id, StatusEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d?.Ativo is null) throw Erro.Validacao("ativo", NaoPreenchido);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        _ = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Profissional não encontrado.");

        if (!d.Ativo.Value)
        {
            // F4: não inativa o profissional com consulta marcada ou em andamento.
            var quantidade = await con.ExecuteScalarAsync<int>(Sql.Cmd(
                "SELECT COUNT(1) FROM DBO.CONSULTA WHERE CODIGO_PROFISSIONAL = @Id AND STATUS IN (" + StatusAtivos + ") AND DATA_FIM > SYSUTCDATETIME();",
                new { Id = id }, tx, ct));
            if (quantidade > 0)
                throw Erro.Conflito(
                    $"O profissional tem {quantidade} consulta(s) marcada(s) ou em andamento. Remarque ou cancele antes de inativar.",
                    "PROFISSIONAL_COM_CONSULTAS",
                    new Dictionary<string, object?> { ["quantidadeConsultas"] = quantidade });
        }

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.PROFISSIONAL SET ATIVO = @Ativo, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_PROFISSIONAL = @Id;", new { Ativo = d.Ativo.Value, Usuario = usuario.Id, Id = id }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "PROFISSIONAL", id, null, usuario.Id,
            new { campos = new[] { "ativo" }, ativo = d.Ativo.Value }, con, tx, ct);

        var nova = await MontarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Profissional não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return nova;
    }

    // ------------------------------------------------------------- horários
    public async Task<List<HorarioDto>> ObterHorariosAsync(int id, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        _ = await CarregarAsync(con, null, id, ct) ?? throw Erro.NaoEncontrado("Profissional não encontrado.");
        return await LerHorariosAsync(con, null, id, ct);
    }

    public async Task<HorariosSaidaDto> SubstituirHorariosAsync(int id, HorariosEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        _ = await CarregarAsync(con, tx, id, ct) ?? throw Erro.NaoEncontrado("Profissional não encontrado.");
        var unidades = (await con.QueryAsync<int>(Sql.Cmd("SELECT CODIGO_UNIDADE FROM DBO.PROFISSIONAL_UNIDADE WHERE CODIGO_PROFISSIONAL = @Id;", new { Id = id }, tx, ct))).ToHashSet();

        var horarios = ValidarHorarios(d.Horarios, unidades, exigeAtendimento: true);

        await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.PROFISSIONAL_HORARIO WHERE CODIGO_PROFISSIONAL = @Id;", new { Id = id }, tx, ct));
        await GravarHorariosAsync(con, tx, id, horarios, ct);

        await _auditoria.RegistrarAsync("EDITAR", "PROFISSIONAL", id, null, usuario.Id, new { campos = new[] { "horarios" } }, con, tx, ct);

        var saida = new HorariosSaidaDto
        {
            Horarios = await LerHorariosAsync(con, tx, id, ct),
            ConsultasForaDoExpediente = await ConsultasForaDoExpedienteAsync(con, tx, id, ct)
        };
        await tx.CommitAsync(ct);
        return saida;
    }

    // =========================================================== apoio
    private sealed record Dados(string Nome, string Cpf, string? Rg, DateOnly DataNascimento, string Telefone, string Email,
        string Endereco, string Numero, string? Complemento, string Cidade, string Uf, string? Escolaridade, string RegistroProfissional, string NumeroConselho,
        string UfConselho, List<int> EspecialidadeIds, List<int> UnidadeIds);

    private static string? Opcional(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static Dados ValidarDados(ProfissionalAtualizacao? d)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");

        var e = new List<ErroCampo>();
        void Obrigatorio(string campo, string? valor, int max)
        {
            if (string.IsNullOrWhiteSpace(valor)) e.Add(new ErroCampo(campo, NaoPreenchido));
            else if (valor.Trim().Length > max) e.Add(new ErroCampo(campo, $"Máximo de {max} caracteres"));
        }

        Obrigatorio("nome", d.Nome, 150);
        Obrigatorio("endereco", d.Endereco, 250);
        Obrigatorio("numero", d.Numero, 10);
        if (!string.IsNullOrWhiteSpace(d.Complemento) && d.Complemento.Trim().Length > 60) e.Add(new ErroCampo("complemento", "Máximo de 60 caracteres"));
        Obrigatorio("cidade", d.Cidade, 80);
        Obrigatorio("numeroConselho", d.NumeroConselho, 20);
        if (!string.IsNullOrWhiteSpace(d.Rg) && d.Rg.Trim().Length > 20) e.Add(new ErroCampo("rg", "Máximo de 20 caracteres"));
        if (!string.IsNullOrWhiteSpace(d.Escolaridade) && d.Escolaridade.Trim().Length > 40) e.Add(new ErroCampo("escolaridade", "Máximo de 40 caracteres"));

        if (string.IsNullOrWhiteSpace(d.Cpf)) e.Add(new ErroCampo("cpf", NaoPreenchido));
        else if (!Digitos11.IsMatch(d.Cpf.Trim()) || !CpfUtil.Valido(d.Cpf.Trim())) e.Add(new ErroCampo("cpf", "CPF inválido"));

        var hoje = DataBrasil.Hoje();
        if (d.DataNascimento is null) e.Add(new ErroCampo("dataNascimento", NaoPreenchido));
        else if (d.DataNascimento.Value > hoje || d.DataNascimento.Value < new DateOnly(1900, 1, 1)) e.Add(new ErroCampo("dataNascimento", "Data inválida"));

        if (string.IsNullOrWhiteSpace(d.Telefone)) e.Add(new ErroCampo("telefone", NaoPreenchido));
        else if (!Formatos.Telefone.IsMatch(d.Telefone)) e.Add(new ErroCampo("telefone", "Informe somente dígitos, com DDD (10 a 13 números)"));

        if (string.IsNullOrWhiteSpace(d.Email)) e.Add(new ErroCampo("email", NaoPreenchido));
        else if (d.Email.Trim().Length > 150 || !Formatos.Email.IsMatch(d.Email.Trim())) e.Add(new ErroCampo("email", "E-mail inválido"));

        if (string.IsNullOrWhiteSpace(d.Uf)) e.Add(new ErroCampo("uf", NaoPreenchido));
        else if (!Formatos.Ufs.Contains(d.Uf)) e.Add(new ErroCampo("uf", "UF inválida"));

        if (string.IsNullOrWhiteSpace(d.UfConselho)) e.Add(new ErroCampo("ufConselho", NaoPreenchido));
        else if (!Formatos.Ufs.Contains(d.UfConselho)) e.Add(new ErroCampo("ufConselho", "UF inválida"));

        if (string.IsNullOrWhiteSpace(d.RegistroProfissional)) e.Add(new ErroCampo("registroProfissional", NaoPreenchido));
        else if (d.RegistroProfissional is not ("CRM" or "CRP")) e.Add(new ErroCampo("registroProfissional", "Informe CRM ou CRP"));

        var especialidades = (d.EspecialidadeIds ?? new List<int>()).Distinct().ToList();
        var unidades = (d.UnidadeIds ?? new List<int>()).Distinct().ToList();
        if (especialidades.Count == 0) e.Add(new ErroCampo("especialidadeIds", "Informe ao menos uma especialidade"));
        if (unidades.Count == 0) e.Add(new ErroCampo("unidadeIds", "Informe ao menos uma unidade"));

        if (e.Count > 0) throw Erro.Validacao(e);

        return new Dados(d.Nome!.Trim(), d.Cpf!.Trim(), Opcional(d.Rg), d.DataNascimento!.Value, d.Telefone!, d.Email!.Trim(),
            d.Endereco!.Trim(), d.Numero!.Trim().ToUpperInvariant(), Opcional(d.Complemento), d.Cidade!.Trim(), d.Uf!, Opcional(d.Escolaridade), d.RegistroProfissional!, d.NumeroConselho!.Trim(),
            d.UfConselho!, especialidades, unidades);
    }

    private static object Parametros(Dados d, int usuarioId, int? id) => new
    {
        Id = id,
        d.Nome,
        Cpf = Sql.Char(d.Cpf, 11),
        d.Rg,
        DataNascimento = d.DataNascimento.ToDateTime(TimeOnly.MinValue),
        d.Telefone,
        d.Email,
        d.Endereco,
        d.Numero,
        d.Complemento,
        d.Cidade,
        d.Uf,
        d.Escolaridade,
        d.RegistroProfissional,
        d.NumeroConselho,
        UfConselho = Sql.Char(d.UfConselho, 2),
        Usuario = usuarioId
    };

    private sealed record HorarioValido(int Indice, int UnidadeId, int Dia, string Tipo, TimeSpan Inicio, TimeSpan Fim);

    /// <summary>Regras da grade (RN03, F3). Qualquer problema vira 400 com codigo HORARIO_INVALIDO e o detalhe em erros.</summary>
    private static List<HorarioValido> ValidarHorarios(IReadOnlyList<HorarioDto>? lista, ISet<int> unidades, bool exigeAtendimento)
    {
        if (lista is null || lista.Count == 0) throw Erro.Validacao("horarios", NaoPreenchido);

        var erros = new List<ErroCampo>();
        var ok = new List<HorarioValido>();

        for (var i = 0; i < lista.Count; i++)
        {
            var h = lista[i];
            var n = $"horarios[{i}]";
            var valido = true;

            if (h.UnidadeId is null) { erros.Add(new ErroCampo($"{n}.unidadeId", NaoPreenchido)); valido = false; }
            else if (!unidades.Contains(h.UnidadeId.Value)) { erros.Add(new ErroCampo($"{n}.unidadeId", "A unidade não é uma das unidades do profissional")); valido = false; }

            if (h.DiaSemana is null) { erros.Add(new ErroCampo($"{n}.diaSemana", NaoPreenchido)); valido = false; }
            else if (h.DiaSemana is < 1 or > 5) { erros.Add(new ErroCampo($"{n}.diaSemana", "A clínica atende de segunda (1) a sexta-feira (5)")); valido = false; }

            if (string.IsNullOrWhiteSpace(h.Tipo)) { erros.Add(new ErroCampo($"{n}.tipo", NaoPreenchido)); valido = false; }
            else if (h.Tipo is not ("ATENDIMENTO" or "INTERVALO")) { erros.Add(new ErroCampo($"{n}.tipo", "Informe ATENDIMENTO ou INTERVALO")); valido = false; }

            var inicioOk = h.HoraInicio is not null && Hora.IsMatch(h.HoraInicio);
            var fimOk = h.HoraFim is not null && Hora.IsMatch(h.HoraFim);
            if (!inicioOk) { erros.Add(new ErroCampo($"{n}.horaInicio", h.HoraInicio is null ? NaoPreenchido : "Use o formato HH:mm")); valido = false; }
            if (!fimOk) { erros.Add(new ErroCampo($"{n}.horaFim", h.HoraFim is null ? NaoPreenchido : "Use o formato HH:mm")); valido = false; }

            if (inicioOk && fimOk && TimeSpan.Parse(h.HoraFim!) <= TimeSpan.Parse(h.HoraInicio!))
            {
                erros.Add(new ErroCampo($"{n}.horaFim", "A hora final deve ser maior que a inicial"));
                valido = false;
            }

            if (valido)
                ok.Add(new HorarioValido(i, h.UnidadeId!.Value, h.DiaSemana!.Value, h.Tipo!, TimeSpan.Parse(h.HoraInicio!), TimeSpan.Parse(h.HoraFim!)));
        }

        // Regras entre os itens.
        static bool Sobrepoe(HorarioValido a, HorarioValido b) => a.Dia == b.Dia && a.Inicio < b.Fim && b.Inicio < a.Fim;

        var atendimentos = ok.Where(x => x.Tipo == "ATENDIMENTO").ToList();
        var intervalos = ok.Where(x => x.Tipo == "INTERVALO").ToList();

        for (var a = 0; a < atendimentos.Count; a++)
            for (var b = a + 1; b < atendimentos.Count; b++)
                if (Sobrepoe(atendimentos[a], atendimentos[b]))
                    erros.Add(new ErroCampo($"horarios[{atendimentos[b].Indice}]", "Sobrepõe outro período de atendimento do mesmo dia (o profissional não está em dois lugares ao mesmo tempo)"));

        for (var a = 0; a < intervalos.Count; a++)
            for (var b = a + 1; b < intervalos.Count; b++)
                if (Sobrepoe(intervalos[a], intervalos[b]))
                    erros.Add(new ErroCampo($"horarios[{intervalos[b].Indice}]", "Sobrepõe outro intervalo do mesmo dia"));

        foreach (var iv in intervalos)
        {
            var dentro = atendimentos.Any(a => a.Dia == iv.Dia && a.UnidadeId == iv.UnidadeId && a.Inicio <= iv.Inicio && a.Fim >= iv.Fim);
            if (!dentro)
                erros.Add(new ErroCampo($"horarios[{iv.Indice}]", "O intervalo precisa ficar dentro de um período de atendimento do mesmo dia e da mesma unidade"));
        }

        if (exigeAtendimento && erros.Count == 0 && atendimentos.Count == 0)
            erros.Add(new ErroCampo("horarios", "Informe ao menos um horário de ATENDIMENTO"));

        if (erros.Count > 0)
            throw Erro.Regra("HORARIO_INVALIDO", "Há horários inválidos. Confira os itens indicados.", erros);

        return ok;
    }

    private static async Task GravarHorariosAsync(SqlConnection con, SqlTransaction tx, int profissionalId, List<HorarioValido> horarios, CancellationToken ct)
    {
        foreach (var h in horarios)
            await con.ExecuteAsync(Sql.Cmd(@"
INSERT INTO DBO.PROFISSIONAL_HORARIO (CODIGO_PROFISSIONAL, CODIGO_UNIDADE, DIA_SEMANA, TIPO, HORA_INICIO, HORA_FIM)
VALUES (@P, @Unidade, @Dia, @Tipo, @Inicio, @Fim);",
                new { P = profissionalId, Unidade = h.UnidadeId, Dia = h.Dia, h.Tipo, h.Inicio, h.Fim }, tx, ct));
    }

    private static async Task<List<HorarioDto>> LerHorariosAsync(SqlConnection con, SqlTransaction? tx, int profissionalId, CancellationToken ct)
    {
        var linhas = await con.QueryAsync<HorarioLinha>(Sql.Cmd(@"
SELECT CODIGO_UNIDADE AS UnidadeId, DIA_SEMANA AS DiaSemana, TIPO AS Tipo, HORA_INICIO AS HoraInicio, HORA_FIM AS HoraFim
FROM DBO.PROFISSIONAL_HORARIO WHERE CODIGO_PROFISSIONAL = @Id
ORDER BY DIA_SEMANA, HORA_INICIO, CODIGO_UNIDADE;", new { Id = profissionalId }, tx, ct));

        return linhas.Select(l => new HorarioDto
        {
            UnidadeId = l.UnidadeId,
            DiaSemana = l.DiaSemana,
            Tipo = l.Tipo,
            HoraInicio = l.HoraInicio.ToString(@"hh\:mm"),
            HoraFim = l.HoraFim.ToString(@"hh\:mm")
        }).ToList();
    }

    /// <summary>
    /// Consultas futuras (agendadas ou confirmadas) que ficaram fora da nova grade: fora de um período de atendimento
    /// da unidade no dia da semana, ou dentro de um intervalo. As consultas não são alteradas (F3). Horários da grade
    /// são de Brasília; as consultas ficam em UTC no banco.
    /// </summary>
    private static async Task<ForaDoExpedienteDto> ConsultasForaDoExpedienteAsync(SqlConnection con, SqlTransaction tx, int profissionalId, CancellationToken ct)
    {
        var linhas = (await con.QueryAsync<ForaLinha>(Sql.Cmd(@"
;WITH HORA_LOCAL AS (
    SELECT C.CODIGO_PUBLIC AS Id, C.DATA_INICIO AS DataInicio, C.CODIGO_UNIDADE AS Unidade,
           CAST((C.DATA_INICIO AT TIME ZONE 'UTC') AT TIME ZONE 'E. South America Standard Time' AS DATETIME2(0)) AS Ini,
           CAST((C.DATA_FIM AT TIME ZONE 'UTC') AT TIME ZONE 'E. South America Standard Time' AS DATETIME2(0)) AS Fim
    FROM DBO.CONSULTA C
    WHERE C.CODIGO_PROFISSIONAL = @Id AND C.STATUS IN ('AGENDADA', 'CONFIRMADA') AND C.DATA_INICIO > SYSUTCDATETIME()
), DIA AS (
    SELECT L.Id, L.DataInicio, L.Unidade,
           ((DATEPART(WEEKDAY, L.Ini) + @@DATEFIRST - 2) % 7) + 1 AS Dia,
           CAST(L.Ini AS TIME(0)) AS HIni, CAST(L.Fim AS TIME(0)) AS HFim
    FROM HORA_LOCAL L
)
SELECT D.Id AS Id, D.DataInicio AS DataInicio, COUNT(1) OVER () AS Total
FROM DIA D
WHERE NOT EXISTS (SELECT 1 FROM DBO.PROFISSIONAL_HORARIO H
                  WHERE H.CODIGO_PROFISSIONAL = @Id AND H.TIPO = 'ATENDIMENTO' AND H.CODIGO_UNIDADE = D.Unidade
                    AND H.DIA_SEMANA = D.Dia AND H.HORA_INICIO <= D.HIni AND H.HORA_FIM >= D.HFim)
   OR EXISTS (SELECT 1 FROM DBO.PROFISSIONAL_HORARIO H
              WHERE H.CODIGO_PROFISSIONAL = @Id AND H.TIPO = 'INTERVALO' AND H.CODIGO_UNIDADE = D.Unidade
                AND H.DIA_SEMANA = D.Dia AND H.HORA_INICIO < D.HFim AND H.HORA_FIM > D.HIni)
ORDER BY D.DataInicio
OFFSET 0 ROWS FETCH NEXT 50 ROWS ONLY;", new { Id = profissionalId }, tx, ct))).ToList();

        return new ForaDoExpedienteDto
        {
            Quantidade = linhas.Count == 0 ? 0 : linhas[0].Total,
            Itens = linhas.Select(l => new ConsultaForaExpedienteDto { ConsultaId = l.Id, DataInicio = DataBrasil.Utc(l.DataInicio) }).ToList()
        };
    }

    private static async Task ConferirDuplicidadesAsync(SqlConnection con, SqlTransaction tx, Dados d, int? ignorarId, CancellationToken ct)
    {
        var cpf = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.PROFISSIONAL WHERE CPF = @Cpf AND (@Ignorar IS NULL OR CODIGO_PROFISSIONAL <> @Ignorar);",
            new { Cpf = Sql.Char(d.Cpf, 11), Ignorar = ignorarId }, tx, ct));
        if (cpf > 0) throw CpfDuplicado();

        var conselho = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.PROFISSIONAL
WHERE REGISTRO_PROFISSIONAL = @Registro AND NUMERO_CONSELHO = @Numero AND UF_CONSELHO = @Uf
  AND (@Ignorar IS NULL OR CODIGO_PROFISSIONAL <> @Ignorar);",
            new { Registro = d.RegistroProfissional, Numero = d.NumeroConselho, Uf = Sql.Char(d.UfConselho, 2), Ignorar = ignorarId }, tx, ct));
        if (conselho > 0) throw ConselhoDuplicado();
    }

    private static ApiException CpfDuplicado() => Erro.Conflito("Este CPF já está cadastrado.", "CPF_DUPLICADO");
    private static ApiException ConselhoDuplicado() => Erro.Conflito("Este registro no conselho já está cadastrado.", "CONSELHO_DUPLICADO");

    private static bool EhDuplicado(SqlException ex, out ApiException? erro)
    {
        erro = null;
        if (ex.Number is not (2601 or 2627)) return false;
        if (ex.Message.Contains("UQ_PROFISSIONAL_CPF", StringComparison.OrdinalIgnoreCase)) erro = CpfDuplicado();
        else if (ex.Message.Contains("UQ_PROFISSIONAL_CONSELHO", StringComparison.OrdinalIgnoreCase)) erro = ConselhoDuplicado();
        return erro is not null;
    }

    /// <summary>Confere se as especialidades e unidades existem e se as novas estão ativas (RN11).</summary>
    private static async Task ConferirReferenciasAsync(SqlConnection con, SqlTransaction tx, Dados d,
        ISet<int> especialidadesAtuais, ISet<int> unidadesAtuais, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();

        var esp = (await con.QueryAsync<ReferenciaLinha>(Sql.Cmd(
            "SELECT CODIGO_ESPECIALIDADE AS Id, NOME AS Nome, ATIVO AS Ativo FROM DBO.ESPECIALIDADE WHERE CODIGO_ESPECIALIDADE IN @Ids;",
            new { Ids = d.EspecialidadeIds }, tx, ct))).ToDictionary(x => x.Id);
        foreach (var id in d.EspecialidadeIds)
        {
            if (!esp.TryGetValue(id, out var r)) erros.Add(new ErroCampo("especialidadeIds", $"Especialidade {id} não encontrada"));
            else if (!r.Ativo && !especialidadesAtuais.Contains(id)) erros.Add(new ErroCampo("especialidadeIds", $"A especialidade \"{r.Nome}\" está inativa"));
        }

        var uni = (await con.QueryAsync<ReferenciaLinha>(Sql.Cmd(
            "SELECT CODIGO_UNIDADE AS Id, NOME_FANTASIA AS Nome, ATIVO AS Ativo FROM DBO.UNIDADE WHERE CODIGO_UNIDADE IN @Ids;",
            new { Ids = d.UnidadeIds }, tx, ct))).ToDictionary(x => x.Id);
        foreach (var id in d.UnidadeIds)
        {
            if (!uni.TryGetValue(id, out var r)) erros.Add(new ErroCampo("unidadeIds", $"Unidade {id} não encontrada"));
            else if (!r.Ativo && !unidadesAtuais.Contains(id)) erros.Add(new ErroCampo("unidadeIds", $"A unidade \"{r.Nome}\" está inativa"));
        }

        if (erros.Count > 0) throw Erro.Validacao(erros);
    }

    /// <summary>RN11: não retira especialidade ou unidade com consulta marcada que a use, nem unidade que ainda tenha expediente.</summary>
    private static async Task ConferirVinculosEmUsoAsync(SqlConnection con, SqlTransaction tx, int profissionalId,
        List<int> especialidades, List<int> unidades, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();

        if (especialidades.Count > 0)
        {
            var linhas = await con.QueryAsync<ContagemLinha>(Sql.Cmd(
                @"SELECT E.NOME AS Nome, COUNT(1) AS Quantidade
FROM DBO.CONSULTA C JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = C.CODIGO_ESPECIALIDADE
WHERE C.CODIGO_PROFISSIONAL = @P AND C.CODIGO_ESPECIALIDADE IN @Ids
  AND C.STATUS IN (" + StatusAtivos + @") AND C.DATA_FIM > SYSUTCDATETIME()
GROUP BY E.NOME;", new { P = profissionalId, Ids = especialidades }, tx, ct));
            foreach (var l in linhas)
                erros.Add(new ErroCampo("especialidadeIds", $"A especialidade \"{l.Nome}\" tem {l.Quantidade} consulta(s) marcada(s) com este profissional"));
        }

        if (unidades.Count > 0)
        {
            var linhas = await con.QueryAsync<ContagemLinha>(Sql.Cmd(
                @"SELECT U.NOME_FANTASIA AS Nome, COUNT(1) AS Quantidade
FROM DBO.CONSULTA C JOIN DBO.UNIDADE U ON U.CODIGO_UNIDADE = C.CODIGO_UNIDADE
WHERE C.CODIGO_PROFISSIONAL = @P AND C.CODIGO_UNIDADE IN @Ids
  AND C.STATUS IN (" + StatusAtivos + @") AND C.DATA_FIM > SYSUTCDATETIME()
GROUP BY U.NOME_FANTASIA;", new { P = profissionalId, Ids = unidades }, tx, ct));
            foreach (var l in linhas)
                erros.Add(new ErroCampo("unidadeIds", $"A unidade \"{l.Nome}\" tem {l.Quantidade} consulta(s) marcada(s) com este profissional"));

            var comExpediente = await con.QueryAsync<string>(Sql.Cmd(
                @"SELECT DISTINCT U.NOME_FANTASIA
FROM DBO.PROFISSIONAL_HORARIO H JOIN DBO.UNIDADE U ON U.CODIGO_UNIDADE = H.CODIGO_UNIDADE
WHERE H.CODIGO_PROFISSIONAL = @P AND H.CODIGO_UNIDADE IN @Ids;", new { P = profissionalId, Ids = unidades }, tx, ct));
            foreach (var nome in comExpediente)
                erros.Add(new ErroCampo("unidadeIds", $"Remova o expediente da unidade \"{nome}\" antes de retirá-la"));
        }

        if (erros.Count > 0)
            throw new ApiException(409, "Conflito", "Há vínculos em uso. Confira os itens indicados.", "VINCULO_EM_USO", erros);
    }

    private static Task<Linha?> CarregarAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(SelectProfissional + " WHERE P.CODIGO_PROFISSIONAL = @Id;", new { Id = id }, tx, ct));

    private static async Task<Dictionary<int, List<ReferenciaDto>>> CarregarVinculosAsync(SqlConnection con, SqlTransaction? tx,
        string tipo, List<int> profissionalIds, CancellationToken ct)
    {
        var resultado = new Dictionary<int, List<ReferenciaDto>>();
        if (profissionalIds.Count == 0) return resultado;

        var sql = tipo == "ESPECIALIDADE"
            ? @"SELECT PE.CODIGO_PROFISSIONAL AS ProfissionalId, E.CODIGO_ESPECIALIDADE AS Id, E.NOME AS Nome
FROM DBO.PROFISSIONAL_ESPECIALIDADE PE JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = PE.CODIGO_ESPECIALIDADE
WHERE PE.CODIGO_PROFISSIONAL IN @Ids ORDER BY E.NOME;"
            : @"SELECT PU.CODIGO_PROFISSIONAL AS ProfissionalId, U.CODIGO_UNIDADE AS Id, U.NOME_FANTASIA AS Nome
FROM DBO.PROFISSIONAL_UNIDADE PU JOIN DBO.UNIDADE U ON U.CODIGO_UNIDADE = PU.CODIGO_UNIDADE
WHERE PU.CODIGO_PROFISSIONAL IN @Ids ORDER BY U.NOME_FANTASIA;";

        var linhas = await con.QueryAsync<VinculoLinha>(Sql.Cmd(sql, new { Ids = profissionalIds }, tx, ct));
        foreach (var l in linhas)
        {
            if (!resultado.TryGetValue(l.ProfissionalId, out var lista))
                resultado[l.ProfissionalId] = lista = new List<ReferenciaDto>();
            lista.Add(new ReferenciaDto { Id = l.Id, Nome = l.Nome });
        }
        return resultado;
    }

    private static async Task<ProfissionalDto?> MontarAsync(SqlConnection con, SqlTransaction? tx, int id, CancellationToken ct)
    {
        var l = await CarregarAsync(con, tx, id, ct);
        if (l is null) return null;

        var ids = new List<int> { id };
        var esp = await CarregarVinculosAsync(con, tx, "ESPECIALIDADE", ids, ct);
        var uni = await CarregarVinculosAsync(con, tx, "UNIDADE", ids, ct);
        var pacientes = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.PACIENTE WHERE CODIGO_PROFISSIONAL_RESPONSAVEL = @Id AND ATIVO = 1;", new { Id = id }, tx, ct));

        return new ProfissionalDto
        {
            Id = l.Id,
            Ativo = l.Ativo,
            Nome = l.Nome,
            Cpf = l.Cpf,
            Rg = l.Rg,
            DataNascimento = DateOnly.FromDateTime(l.DataNascimento),
            Telefone = l.Telefone,
            Email = l.Email,
            Endereco = l.Endereco,
            Numero = l.Numero,
            Complemento = l.Complemento,
            Cidade = l.Cidade,
            Uf = l.Uf,
            Escolaridade = l.Escolaridade,
            RegistroProfissional = l.RegistroProfissional,
            NumeroConselho = l.NumeroConselho,
            UfConselho = l.UfConselho,
            Especialidades = esp.TryGetValue(id, out var e) ? e : new List<ReferenciaDto>(),
            Unidades = uni.TryGetValue(id, out var u) ? u : new List<ReferenciaDto>(),
            QuantidadePacientes = pacientes,
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
}
