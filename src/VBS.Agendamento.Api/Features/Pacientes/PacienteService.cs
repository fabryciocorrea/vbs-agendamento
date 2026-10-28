using System.Data;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Pacientes;

/// <summary>
/// Bloco 2 do contrato (parte): lista, busca por CPF, consulta, cadastro, alteração e status do paciente.
/// Regras: RN14 (inativar, não excluir), RN16 (médico só vê os seus pacientes), RN18 (histórico do profissional
/// responsável), RN25 (CPF e documento do estrangeiro), E1 (CPF mascarado), E3 (consentimentos), E4 (menor).
/// Ainda não feito: liberação campo a campo (E2/RN24), que depende do catálogo de campos.
/// </summary>
public sealed partial class PacienteService
{
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public PacienteService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    // ------------------------------------------------------------------ SQL base
    private const string SelectPaciente = @"
SELECT P.CODIGO_PACIENTE AS Codigo, P.CODIGO_PUBLIC AS Id, P.ATIVO AS Ativo, P.NOME AS Nome, P.ESTRANGEIRO AS Estrangeiro,
       P.CPF AS Cpf, P.TIPO_DOCUMENTO_ESTRANGEIRO AS TipoDocumentoEstrangeiro, P.NUMERO_DOCUMENTO_ESTRANGEIRO AS NumeroDocumentoEstrangeiro,
       P.RG AS Rg, P.DATA_NASCIMENTO AS DataNascimento, P.TELEFONE AS Telefone, P.EMAIL AS Email, P.ENDERECO AS Endereco, P.NUMERO AS Numero, P.COMPLEMENTO AS Complemento,
       P.CIDADE AS Cidade, P.UF AS Uf, P.ESCOLARIDADE AS Escolaridade,
       P.CODIGO_CONVENIO AS ConvenioId, COALESCE(C.NOME_FANTASIA, C.RAZAO_SOCIAL) AS ConvenioNome,
       P.NUMERO_CARTEIRINHA AS NumeroCarteirinha, P.DATA_VALIDADE_CARTEIRINHA AS DataValidadeCarteirinha,
       P.CODIGO_PROFISSIONAL_RESPONSAVEL AS ProfissionalId, PR.NOME AS ProfissionalNome,
       P.CODIGO_RESPONSAVEL_LEGAL AS ResponsavelId,
       P.DATA_CRIACAO AS CriadoEm, P.CRIACAO_USUARIO AS CriadoPorId, UC.NOME_COMPLETO AS CriadoPorNome,
       P.DATA_ATUALIZACAO AS AtualizadoEm, P.ATUALIZACAO_USUARIO AS AtualizadoPorId, UA.NOME_COMPLETO AS AtualizadoPorNome
FROM DBO.PACIENTE P
LEFT JOIN DBO.CONVENIO C ON C.CODIGO_CONVENIO = P.CODIGO_CONVENIO
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = P.CODIGO_PROFISSIONAL_RESPONSAVEL
LEFT JOIN DBO.USUARIO UC ON UC.CODIGO_USUARIO = P.CRIACAO_USUARIO
LEFT JOIN DBO.USUARIO UA ON UA.CODIGO_USUARIO = P.ATUALIZACAO_USUARIO";

    private const string SelectResumo = @"
SELECT P.CODIGO_PACIENTE AS Codigo, P.CODIGO_PUBLIC AS Id, P.ATIVO AS Ativo, P.NOME AS Nome, P.CPF AS Cpf,
       P.DATA_NASCIMENTO AS DataNascimento, P.TELEFONE AS Telefone,
       P.CODIGO_CONVENIO AS ConvenioId, COALESCE(C.NOME_FANTASIA, C.RAZAO_SOCIAL) AS ConvenioNome,
       P.CODIGO_PROFISSIONAL_RESPONSAVEL AS ProfissionalId, PR.NOME AS ProfissionalNome
FROM DBO.PACIENTE P
LEFT JOIN DBO.CONVENIO C ON C.CODIGO_CONVENIO = P.CODIGO_CONVENIO
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = P.CODIGO_PROFISSIONAL_RESPONSAVEL";

    // ------------------------------------------------------------ linhas do banco
    private sealed class PacienteLinha
    {
        public int Codigo { get; set; }
        public Guid Id { get; set; }
        public bool Ativo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public bool Estrangeiro { get; set; }
        public string? Cpf { get; set; }
        public string? TipoDocumentoEstrangeiro { get; set; }
        public string? NumeroDocumentoEstrangeiro { get; set; }
        public string? Rg { get; set; }
        public DateTime DataNascimento { get; set; }
        public string? Telefone { get; set; }
        public string? Email { get; set; }
        public string Endereco { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string? Complemento { get; set; }
        public string Cidade { get; set; } = string.Empty;
        public string Uf { get; set; } = string.Empty;
        public string? Escolaridade { get; set; }
        public int? ConvenioId { get; set; }
        public string? ConvenioNome { get; set; }
        public string? NumeroCarteirinha { get; set; }
        public DateTime? DataValidadeCarteirinha { get; set; }
        public int ProfissionalId { get; set; }
        public string ProfissionalNome { get; set; } = string.Empty;
        public int? ResponsavelId { get; set; }
        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }
        public string? CriadoPorNome { get; set; }
        public DateTime AtualizadoEm { get; set; }
        public int? AtualizadoPorId { get; set; }
        public string? AtualizadoPorNome { get; set; }
    }

    private sealed class ResumoLinha
    {
        public int Codigo { get; set; }
        public Guid Id { get; set; }
        public bool Ativo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string? Cpf { get; set; }
        public DateTime DataNascimento { get; set; }
        public string? Telefone { get; set; }
        public int? ConvenioId { get; set; }
        public string? ConvenioNome { get; set; }
        public int ProfissionalId { get; set; }
        public string ProfissionalNome { get; set; } = string.Empty;
    }

    private sealed class ResponsavelLinha
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

    // --------------------------------------------------------------------- lista
    public async Task<PacientePaginaDto> ListarAsync(FiltroPacientes f, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");

        // RN16: o médico recebe somente os pacientes de que é o profissional responsável atual.
        if (usuario.EhMedico)
        {
            onde.Append(" AND P.CODIGO_PROFISSIONAL_RESPONSAVEL = @Medico");
            p.Add("Medico", usuario.ProfissionalId!.Value);
        }

        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            var termo = f.Busca.Trim();
            var condicao = "P.NOME LIKE @Nome";
            p.Add("Nome", "%" + Formatos.EscaparLike(termo) + "%");

            var soNumeros = termo.All(c => char.IsAsciiDigit(c) || c is ' ' or '-' or '(' or ')' or '+');
            var digitos = new string(termo.Where(char.IsAsciiDigit).ToArray());
            if (soNumeros && digitos.Length > 0)
            {
                condicao += " OR P.TELEFONE LIKE @Telefone";
                p.Add("Telefone", "%" + digitos + "%");
            }
            onde.Append(" AND (").Append(condicao).Append(')');
        }

        if (f.Ativo.HasValue) { onde.Append(" AND P.ATIVO = @Ativo"); p.Add("Ativo", f.Ativo.Value); }
        if (f.ProfissionalId.HasValue) { onde.Append(" AND P.CODIGO_PROFISSIONAL_RESPONSAVEL = @ProfissionalId"); p.Add("ProfissionalId", f.ProfissionalId.Value); }
        if (f.ConvenioId.HasValue) { onde.Append(" AND P.CODIGO_CONVENIO = @ConvenioId"); p.Add("ConvenioId", f.ConvenioId.Value); }
        if (f.MenorDeIdade.HasValue)
        {
            var limite = DataBrasil.Hoje().AddYears(-18).ToDateTime(TimeOnly.MinValue);
            onde.Append(f.MenorDeIdade.Value ? " AND P.DATA_NASCIMENTO > @Limite18" : " AND P.DATA_NASCIMENTO <= @Limite18");
            p.Add("Limite18", limite, DbType.Date);
        }

        await using var con = await _db.AbrirAsync(ct);

        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.PACIENTE P" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<ResumoLinha>(Sql.Cmd(
            SelectResumo + onde + " ORDER BY P.NOME, P.CODIGO_PACIENTE OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        var hoje = DataBrasil.Hoje();
        return new PacientePaginaDto
        {
            Itens = linhas.Select(l => ParaResumo(l, hoje)).ToList(),
            Pagina = f.Pagina,
            Tamanho = f.Tamanho,
            Total = total
        };
    }

    // ------------------------------------------------------------ busca por CPF
    public async Task<PacienteResumoDto> BuscarPorCpfAsync(BuscaCpfEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        var cpf = d?.Cpf?.Trim();
        if (string.IsNullOrEmpty(cpf)) throw Erro.Validacao("cpf", "Campo Não Preenchido");
        if (!CpfUtil.Valido(cpf)) throw Erro.Validacao("cpf", "CPF inválido");

        var sql = SelectResumo + " WHERE P.CPF = @Cpf" + (usuario.EhMedico ? " AND P.CODIGO_PROFISSIONAL_RESPONSAVEL = @Medico" : "") + ";";

        await using var con = await _db.AbrirAsync(ct);
        var linha = await con.QueryFirstOrDefaultAsync<ResumoLinha>(Sql.Cmd(sql,
            new { Cpf = Sql.Char(cpf, 11), Medico = usuario.ProfissionalId }, ct: ct));

        // Para o médico, paciente de outro profissional aparece como não cadastrado.
        if (linha is null)
            throw Erro.NaoEncontrado("Não há paciente cadastrado com este CPF.", "PACIENTE_NAO_CADASTRADO");

        await _auditoria.RegistrarAsync("VISUALIZAR", "PACIENTE", linha.Codigo, linha.Codigo, usuario.Id,
            new { origem = "busca-por-cpf" }, con, ct: ct);
        return ParaResumo(linha, DataBrasil.Hoje());
    }

    // ------------------------------------------------------------------- consulta
    public async Task<PacienteDto> ObterAsync(Guid id, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var linha = await CarregarLinhaAsync(con, null, id, usuario, ct)
            ?? throw Erro.NaoEncontrado("Paciente não encontrado.");

        await _auditoria.RegistrarAsync("VISUALIZAR", "PACIENTE", linha.Codigo, linha.Codigo, usuario.Id, null, con, ct: ct);
        return await MontarAsync(con, null, linha, ct);
    }

    // -------------------------------------------------------------------- cadastro
    public async Task<PacienteDto> CriarAsync(PacienteEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var erros = PacienteValidador.ValidarCadastro(d, DataBrasil.Hoje());
        if (erros.Count > 0) throw Erro.Validacao(erros);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await ConferirReferenciasAsync(con, tx, d, d.ProfissionalResponsavelId, ct);
        var cpf = Limpo(d.Cpf);
        await ConferirCpfDuplicadoAsync(con, tx, cpf, null, ct);

        int codigo;
        try
        {
            codigo = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.PACIENTE
  (NOME, ESTRANGEIRO, CPF, TIPO_DOCUMENTO_ESTRANGEIRO, NUMERO_DOCUMENTO_ESTRANGEIRO, RG, DATA_NASCIMENTO, TELEFONE, EMAIL,
   ENDERECO, NUMERO, COMPLEMENTO, CIDADE, UF, ESCOLARIDADE, CODIGO_CONVENIO, NUMERO_CARTEIRINHA, DATA_VALIDADE_CARTEIRINHA,
   CODIGO_PROFISSIONAL_RESPONSAVEL, CODIGO_RESPONSAVEL_LEGAL, CRIACAO_USUARIO, ATUALIZACAO_USUARIO)
VALUES
  (@Nome, @Estrangeiro, @Cpf, @TipoDoc, @NumeroDoc, @Rg, @DataNascimento, @Telefone, @Email,
   @Endereco, @Numero, @Complemento, @Cidade, @Uf, @Escolaridade, @ConvenioId, @Carteirinha, @ValidadeCarteirinha,
   @Profissional, @Responsavel, @Usuario, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                ParametrosGravacao(d, cpf, usuario.Id, null, d.ProfissionalResponsavelId), tx, ct));
        }
        catch (SqlException ex) when (EhCpfDuplicado(ex))
        {
            throw CpfDuplicado();
        }

        // RN18: a primeira linha do histórico do profissional responsável.
        await con.ExecuteAsync(Sql.Cmd(@"
INSERT INTO DBO.PACIENTE_PROFISSIONAL_HISTORICO (CODIGO_PACIENTE, CODIGO_PROFISSIONAL, DATA_INICIO, ALTERACAO_USUARIO)
VALUES (@Paciente, @Profissional, SYSUTCDATETIME(), @Usuario);",
            new { Paciente = codigo, Profissional = d.ProfissionalResponsavelId, Usuario = usuario.Id }, tx, ct));

        // E3: um registro por finalidade.
        foreach (var c in d.Consentimentos!)
        {
            await con.ExecuteAsync(Sql.Cmd(@"
INSERT INTO DBO.CONSENTIMENTO_LGPD (CODIGO_PACIENTE, FINALIDADE, CONCEDIDO, ORIGEM, REGISTRO_USUARIO)
VALUES (@Paciente, @Finalidade, @Concedido, @Origem, @Usuario);",
                new { Paciente = codigo, c.Finalidade, Concedido = c.Concedido!.Value, c.Origem, Usuario = usuario.Id }, tx, ct));
        }

        await _auditoria.RegistrarAsync("INCLUIR", "PACIENTE", codigo, codigo, usuario.Id, null, con, tx, ct);

        var linha = await CarregarLinhaPorCodigoAsync(con, tx, codigo, ct)
            ?? throw new InvalidOperationException("Paciente recém-criado não encontrado.");
        var resultado = await MontarAsync(con, tx, linha, ct);

        await tx.CommitAsync(ct);
        return resultado;
    }

    // ------------------------------------------------------------------- alteração
    public async Task<PacienteDto> AtualizarAsync(Guid id, PacienteBaseEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var erros = PacienteValidador.ValidarBase(d, DataBrasil.Hoje());
        if (erros.Count > 0) throw Erro.Validacao(erros);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarLinhaAsync(con, tx, id, usuario, ct)
            ?? throw Erro.NaoEncontrado("Paciente não encontrado.");

        await ConferirReferenciasAsync(con, tx, d, null, ct);
        var cpf = Limpo(d.Cpf);
        await ConferirCpfDuplicadoAsync(con, tx, cpf, atual.Codigo, ct);

        try
        {
            await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.PACIENTE SET
  NOME = @Nome, ESTRANGEIRO = @Estrangeiro, CPF = @Cpf, TIPO_DOCUMENTO_ESTRANGEIRO = @TipoDoc,
  NUMERO_DOCUMENTO_ESTRANGEIRO = @NumeroDoc, RG = @Rg, DATA_NASCIMENTO = @DataNascimento, TELEFONE = @Telefone,
  EMAIL = @Email, ENDERECO = @Endereco, NUMERO = @Numero, COMPLEMENTO = @Complemento, CIDADE = @Cidade, UF = @Uf, ESCOLARIDADE = @Escolaridade,
  CODIGO_CONVENIO = @ConvenioId, NUMERO_CARTEIRINHA = @Carteirinha, DATA_VALIDADE_CARTEIRINHA = @ValidadeCarteirinha,
  CODIGO_RESPONSAVEL_LEGAL = @Responsavel, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_PACIENTE = @Codigo;",
                ParametrosGravacao(d, cpf, usuario.Id, atual.Codigo, null), tx, ct));
        }
        catch (SqlException ex) when (EhCpfDuplicado(ex))
        {
            throw CpfDuplicado();
        }

        // O log guarda quais campos mudaram, sem copiar os dados pessoais.
        await _auditoria.RegistrarAsync("EDITAR", "PACIENTE", atual.Codigo, atual.Codigo, usuario.Id,
            new { campos = CamposAlterados(atual, d, cpf) }, con, tx, ct);

        var nova = await CarregarLinhaPorCodigoAsync(con, tx, atual.Codigo, ct)
            ?? throw new InvalidOperationException("Paciente não encontrado após a alteração.");
        var resultado = await MontarAsync(con, tx, nova, ct);

        await tx.CommitAsync(ct);
        return resultado;
    }

    // ---------------------------------------------------------------------- status
    public async Task<PacienteDto> AlterarStatusAsync(Guid id, StatusPacienteEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d?.Ativo is null) throw Erro.Validacao("ativo", "Campo Não Preenchido");

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarLinhaAsync(con, tx, id, usuario, ct)
            ?? throw Erro.NaoEncontrado("Paciente não encontrado.");

        if (!d.Ativo.Value)
        {
            // E7: não inativa com consulta marcada ou em andamento.
            var quantidade = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.CONSULTA
WHERE CODIGO_PACIENTE = @Codigo
  AND STATUS IN ('AGENDADA', 'CONFIRMADA', 'CHECK_IN', 'EM_ATENDIMENTO')
  AND DATA_FIM > SYSUTCDATETIME();", new { Codigo = atual.Codigo }, tx, ct));

            if (quantidade > 0)
                throw Erro.Conflito(
                    $"O paciente tem {quantidade} consulta(s) marcada(s) ou em andamento. Cancele ou conclua antes de inativar.",
                    "PACIENTE_COM_CONSULTAS",
                    new Dictionary<string, object?> { ["quantidadeConsultas"] = quantidade });
        }

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.PACIENTE SET ATIVO = @Ativo, DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_PACIENTE = @Codigo;", new { Ativo = d.Ativo.Value, Usuario = usuario.Id, Codigo = atual.Codigo }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "PACIENTE", atual.Codigo, atual.Codigo, usuario.Id,
            new { campos = new[] { "ativo" }, ativo = d.Ativo.Value }, con, tx, ct);

        var nova = await CarregarLinhaPorCodigoAsync(con, tx, atual.Codigo, ct)
            ?? throw new InvalidOperationException("Paciente não encontrado após a alteração.");
        var resultado = await MontarAsync(con, tx, nova, ct);

        await tx.CommitAsync(ct);
        return resultado;
    }

    // ============================================================== apoio
    private static string? Limpo(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static object ParametrosGravacao(PacienteBaseEntrada d, string? cpf, int usuarioId, int? codigo, int? profissionalId)
    {
        var estrangeiro = d.Estrangeiro ?? false;
        return new
        {
            Codigo = codigo,
            Nome = d.Nome!.Trim(),
            Estrangeiro = estrangeiro,
            Cpf = Sql.Char(cpf, 11),
            TipoDoc = estrangeiro ? Limpo(d.TipoDocumentoEstrangeiro) : null,
            NumeroDoc = estrangeiro ? Limpo(d.NumeroDocumentoEstrangeiro) : null,
            Rg = Limpo(d.Rg),
            DataNascimento = d.DataNascimento!.Value.ToDateTime(TimeOnly.MinValue),
            Telefone = Limpo(d.Telefone),
            Email = Limpo(d.Email),
            Endereco = d.Endereco!.Trim(),
            Numero = d.Numero!.Trim().ToUpperInvariant(),
            Complemento = Limpo(d.Complemento),
            Cidade = d.Cidade!.Trim(),
            Uf = d.Uf,
            Escolaridade = Limpo(d.Escolaridade),
            ConvenioId = d.Convenio?.ConvenioId,
            Carteirinha = d.Convenio is null ? null : d.Convenio.NumeroCarteirinha!.Trim(),
            ValidadeCarteirinha = d.Convenio?.DataValidadeCarteirinha?.ToDateTime(TimeOnly.MinValue),
            Profissional = profissionalId,
            Responsavel = d.ResponsavelLegalId,
            Usuario = usuarioId
        };
    }

    /// <summary>Profissional, responsável legal e convênio precisam existir (e estar ativos, quando há ATIVO).</summary>
    private static async Task ConferirReferenciasAsync(SqlConnection con, SqlTransaction tx, PacienteBaseEntrada d, int? profissionalId, CancellationToken ct)
    {
        if (profissionalId.HasValue)
        {
            var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(
                "SELECT COUNT(1) FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id AND ATIVO = 1;", new { Id = profissionalId }, tx, ct));
            if (existe == 0) throw Erro.Validacao("profissionalResponsavelId", "Profissional não encontrado ou inativo");
        }

        if (d.ResponsavelLegalId.HasValue)
        {
            var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(
                "SELECT COUNT(1) FROM DBO.RESPONSAVEL_LEGAL WHERE CODIGO_RESPONSAVEL = @Id;", new { Id = d.ResponsavelLegalId }, tx, ct));
            if (existe == 0)
                throw Erro.Regra("RESPONSAVEL_NAO_CADASTRADO",
                    "Este responsável não está cadastrado. Cadastre-o para concluir o cadastro do paciente.",
                    new[] { new ErroCampo("responsavelLegalId", "Responsável não cadastrado") });
        }

        if (d.Convenio?.ConvenioId is int convenioId)
        {
            var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(
                "SELECT COUNT(1) FROM DBO.CONVENIO WHERE CODIGO_CONVENIO = @Id AND ATIVO = 1;", new { Id = convenioId }, tx, ct));
            if (existe == 0) throw Erro.Validacao("convenio.convenioId", "Convênio não encontrado ou inativo");
        }
    }

    private static async Task ConferirCpfDuplicadoAsync(SqlConnection con, SqlTransaction tx, string? cpf, int? ignorarCodigo, CancellationToken ct)
    {
        if (cpf is null) return;
        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.PACIENTE WHERE CPF = @Cpf AND (@Ignorar IS NULL OR CODIGO_PACIENTE <> @Ignorar);",
            new { Cpf = Sql.Char(cpf, 11), Ignorar = ignorarCodigo }, tx, ct));
        if (existe > 0) throw CpfDuplicado();
    }

    private static ApiException CpfDuplicado() =>
        Erro.Conflito("Este CPF já está cadastrado.", "CPF_DUPLICADO");

    // 2601 e 2627: violação de índice ou restrição única (corrida entre duas gravações do mesmo CPF).
    private static bool EhCpfDuplicado(SqlException ex) =>
        ex.Number is 2601 or 2627 && ex.Message.Contains("UX_PACIENTE_CPF", StringComparison.OrdinalIgnoreCase);

    private static Task<PacienteLinha?> CarregarLinhaAsync(SqlConnection con, SqlTransaction? tx, Guid id, UsuarioAtual usuario, CancellationToken ct)
    {
        // RN16: para o médico, paciente de outro profissional "não existe" (404).
        var sql = SelectPaciente + " WHERE P.CODIGO_PUBLIC = @Id" +
                  (usuario.EhMedico ? " AND P.CODIGO_PROFISSIONAL_RESPONSAVEL = @Medico" : "") + ";";
        return con.QueryFirstOrDefaultAsync<PacienteLinha>(Sql.Cmd(sql, new { Id = id, Medico = usuario.ProfissionalId }, tx, ct));
    }

    private static Task<PacienteLinha?> CarregarLinhaPorCodigoAsync(SqlConnection con, SqlTransaction tx, int codigo, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<PacienteLinha>(Sql.Cmd(SelectPaciente + " WHERE P.CODIGO_PACIENTE = @Codigo;", new { Codigo = codigo }, tx, ct));

    private static async Task<PacienteDto> MontarAsync(SqlConnection con, SqlTransaction? tx, PacienteLinha l, CancellationToken ct)
    {
        ResponsavelLinha? responsavel = null;
        if (l.ResponsavelId.HasValue)
        {
            responsavel = await con.QueryFirstOrDefaultAsync<ResponsavelLinha>(Sql.Cmd(@"
SELECT R.CODIGO_RESPONSAVEL AS Id, R.NOME AS Nome, R.CPF AS Cpf, R.RG AS Rg, R.DATA_NASCIMENTO AS DataNascimento,
       R.TELEFONE AS Telefone, R.EMAIL AS Email, R.ESCOLARIDADE AS Escolaridade, R.ENDERECO AS Endereco, R.NUMERO AS Numero,
       R.COMPLEMENTO AS Complemento, R.CIDADE AS Cidade, R.UF AS Uf,
       (SELECT COUNT(1) FROM DBO.PACIENTE X WHERE X.CODIGO_RESPONSAVEL_LEGAL = R.CODIGO_RESPONSAVEL) AS QuantidadePacientes
FROM DBO.RESPONSAVEL_LEGAL R WHERE R.CODIGO_RESPONSAVEL = @Id;", new { Id = l.ResponsavelId }, tx, ct));
        }

        // Estado atual de cada finalidade: a linha mais recente.
        var consentimentos = (await con.QueryAsync<ConsentimentoLinha>(Sql.Cmd(@"
SELECT X.FINALIDADE AS Finalidade, X.CONCEDIDO AS Concedido, X.ORIGEM AS Origem, X.DATA_REGISTRO AS DataRegistro,
       X.REGISTRO_USUARIO AS RegistradoPorId, U.NOME_COMPLETO AS RegistradoPorNome
FROM (SELECT C.*, ROW_NUMBER() OVER (PARTITION BY C.FINALIDADE ORDER BY C.DATA_REGISTRO DESC, C.CODIGO_CONSENTIMENTO DESC) AS ORDEM
      FROM DBO.CONSENTIMENTO_LGPD C WHERE C.CODIGO_PACIENTE = @Codigo) X
LEFT JOIN DBO.USUARIO U ON U.CODIGO_USUARIO = X.REGISTRO_USUARIO
WHERE X.ORDEM = 1
ORDER BY X.FINALIDADE;", new { Codigo = l.Codigo }, tx, ct))).ToList();

        var hoje = DataBrasil.Hoje();
        var nascimento = DateOnly.FromDateTime(l.DataNascimento);

        var dto = new PacienteDto
        {
            Id = l.Id,
            Ativo = l.Ativo,
            Nome = l.Nome,
            Estrangeiro = l.Estrangeiro,
            Cpf = l.Cpf,
            TipoDocumentoEstrangeiro = l.TipoDocumentoEstrangeiro,
            NumeroDocumentoEstrangeiro = l.NumeroDocumentoEstrangeiro,
            Rg = l.Rg,
            DataNascimento = nascimento,
            MenorDeIdade = DataBrasil.Idade(nascimento, hoje) < 18,
            Telefone = l.Telefone,
            Email = l.Email,
            Endereco = l.Endereco,
            Numero = l.Numero,
            Complemento = l.Complemento,
            Cidade = l.Cidade,
            Uf = l.Uf,
            Escolaridade = l.Escolaridade,
            ProfissionalResponsavel = new ProfissionalReferenciaDto { Id = l.ProfissionalId, Nome = l.ProfissionalNome },
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
            CriadoEm = DataBrasil.Utc(l.CriadoEm),
            CriadoPor = l.CriadoPorId.HasValue
                ? new UsuarioReferenciaDto { Id = l.CriadoPorId.Value, NomeCompleto = l.CriadoPorNome ?? string.Empty }
                : null,
            AtualizadoEm = DataBrasil.Utc(l.AtualizadoEm),
            AtualizadoPor = l.AtualizadoPorId.HasValue
                ? new UsuarioReferenciaDto { Id = l.AtualizadoPorId.Value, NomeCompleto = l.AtualizadoPorNome ?? string.Empty }
                : null
        };

        if (l.ConvenioId.HasValue && l.DataValidadeCarteirinha.HasValue)
        {
            var validade = DateOnly.FromDateTime(l.DataValidadeCarteirinha.Value);
            dto.Convenio = new ConvenioPacienteDto
            {
                Convenio = new ConvenioReferenciaDto { Id = l.ConvenioId.Value, NomeFantasia = l.ConvenioNome ?? string.Empty },
                NumeroCarteirinha = l.NumeroCarteirinha ?? string.Empty,
                DataValidadeCarteirinha = validade,
                CarteirinhaVencida = validade < hoje
            };
        }

        if (responsavel is not null)
        {
            dto.ResponsavelLegal = new ResponsavelLegalDto
            {
                Id = responsavel.Id,
                Nome = responsavel.Nome,
                Cpf = responsavel.Cpf,
                Rg = responsavel.Rg,
                DataNascimento = DateOnly.FromDateTime(responsavel.DataNascimento),
                Telefone = responsavel.Telefone,
                Email = responsavel.Email,
                Escolaridade = responsavel.Escolaridade,
                Endereco = responsavel.Endereco,
                Numero = responsavel.Numero,
                Complemento = responsavel.Complemento,
                Cidade = responsavel.Cidade,
                Uf = responsavel.Uf,
                QuantidadePacientes = responsavel.QuantidadePacientes
            };
        }

        return dto;
    }

    private static PacienteResumoDto ParaResumo(ResumoLinha l, DateOnly hoje)
    {
        var nascimento = DateOnly.FromDateTime(l.DataNascimento);
        return new PacienteResumoDto
        {
            Id = l.Id,
            Ativo = l.Ativo,
            Nome = l.Nome,
            CpfMascarado = string.IsNullOrEmpty(l.Cpf) ? null : CpfUtil.Mascarar(l.Cpf),
            DataNascimento = nascimento,
            MenorDeIdade = DataBrasil.Idade(nascimento, hoje) < 18,
            Telefone = l.Telefone,
            Convenio = l.ConvenioId.HasValue
                ? new ConvenioReferenciaDto { Id = l.ConvenioId.Value, NomeFantasia = l.ConvenioNome ?? string.Empty }
                : null,
            ProfissionalResponsavel = new ProfissionalReferenciaDto { Id = l.ProfissionalId, Nome = l.ProfissionalNome }
        };
    }

    private static List<string> CamposAlterados(PacienteLinha a, PacienteBaseEntrada d, string? cpf)
    {
        var lista = new List<string>();
        void Dif<T>(string campo, T antes, T depois)
        {
            if (!EqualityComparer<T>.Default.Equals(antes, depois)) lista.Add(campo);
        }

        var estrangeiro = d.Estrangeiro ?? false;
        Dif("nome", a.Nome, d.Nome!.Trim());
        Dif("estrangeiro", a.Estrangeiro, estrangeiro);
        Dif("cpf", a.Cpf, cpf);
        Dif("tipoDocumentoEstrangeiro", a.TipoDocumentoEstrangeiro, estrangeiro ? Limpo(d.TipoDocumentoEstrangeiro) : null);
        Dif("numeroDocumentoEstrangeiro", a.NumeroDocumentoEstrangeiro, estrangeiro ? Limpo(d.NumeroDocumentoEstrangeiro) : null);
        Dif("rg", a.Rg, Limpo(d.Rg));
        Dif("dataNascimento", DateOnly.FromDateTime(a.DataNascimento), d.DataNascimento!.Value);
        Dif("telefone", a.Telefone, Limpo(d.Telefone));
        Dif("email", a.Email, Limpo(d.Email));
        Dif("endereco", a.Endereco, d.Endereco!.Trim());
        Dif("numero", a.Numero, d.Numero!.Trim().ToUpperInvariant());
        Dif("complemento", a.Complemento, Limpo(d.Complemento));
        Dif("cidade", a.Cidade, d.Cidade!.Trim());
        Dif("uf", a.Uf, d.Uf);
        Dif("escolaridade", a.Escolaridade, Limpo(d.Escolaridade));
        Dif("convenio.convenioId", a.ConvenioId, d.Convenio?.ConvenioId);
        Dif("convenio.numeroCarteirinha", a.NumeroCarteirinha, d.Convenio?.NumeroCarteirinha?.Trim());
        Dif("convenio.dataValidadeCarteirinha",
            a.DataValidadeCarteirinha.HasValue ? DateOnly.FromDateTime(a.DataValidadeCarteirinha.Value) : (DateOnly?)null,
            d.Convenio?.DataValidadeCarteirinha);
        Dif("responsavelLegalId", a.ResponsavelId, d.ResponsavelLegalId);
        return lista;
    }
}
