using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Consultas;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Prontuarios;

/// <summary>
/// Prontuário (RF12, RN16, RN19, H1 a H6). Cada atendimento gera um registro novo; nada é excluído.
/// O profissional responsável inclui direto; só o administrador inclui em nome de um profissional ou edita, e sempre com
/// solicitação do profissional e OK do responsável legal, pelas procedures do banco (que gravam correção e log).
/// </summary>
public sealed class ProntuarioService
{
    public const int TamanhoMaximoDocumento = 5 * 1024 * 1024;
    private const string NaoPreenchido = "Campo Não Preenchido";
    private const string MensagemAcessoNegado = "Você não é o profissional responsável por este paciente. Solicite acesso a um usuário com essa permissão ou ao administrador.";
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;
    private readonly IHttpContextAccessor _http;

    public ProntuarioService(IConexaoFactory db, IAuditoria auditoria, IHttpContextAccessor http)
    {
        _db = db;
        _auditoria = auditoria;
        _http = http;
    }

    private sealed class PacienteAcesso
    {
        public int Codigo { get; set; }
        public Guid Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public bool Ativo { get; set; }
        public int ProfissionalResponsavel { get; set; }
    }

    private sealed class Linha
    {
        public long Codigo { get; set; }
        public Guid Id { get; set; }
        public Guid PacienteId { get; set; }
        public string PacienteNome { get; set; } = string.Empty;
        public Guid? ConsultaId { get; set; }
        public int ProfissionalId { get; set; }
        public string ProfissionalNome { get; set; } = string.Empty;
        public string Conselho { get; set; } = string.Empty;
        public string NumeroConselho { get; set; } = string.Empty;
        public string Uf { get; set; } = string.Empty;
        public DateTime RegistradoEm { get; set; }
        public string Observacao { get; set; } = string.Empty;
        public int? IncluidoPorId { get; set; }
        public string? IncluidoPorNome { get; set; }
        public string? SolicitadoPor { get; set; }
        public string? AutorizadoPor { get; set; }
        public string? ObsAutorizacao { get; set; }
        public DateTime? CorrigidoEm { get; set; }
        public Guid? DocId { get; set; }
        public string? DocNome { get; set; }
        public int? DocTamanho { get; set; }
    }

    private sealed class UltimoRegistro
    {
        public string Observacao { get; set; } = string.Empty;
        public DateTime Quando { get; set; }
    }

    private sealed class ProfissionalDados
    {
        public string Nome { get; set; } = string.Empty;
        public string? Conselho { get; set; }
        public string? Numero { get; set; }
        public string? Uf { get; set; }
        public bool Ativo { get; set; }
    }

    private const string Select = @"
SELECT R.CODIGO_REGS_PRNT AS Codigo, R.CODIGO_PUBLIC AS Id, P.CODIGO_PUBLIC AS PacienteId, P.NOME AS PacienteNome,
       C.CODIGO_PUBLIC AS ConsultaId, R.CODIGO_PROFISSIONAL AS ProfissionalId, R.NOME_PROFISSIONAL AS ProfissionalNome,
       R.REGISTRO_PROFISSIONAL AS Conselho, R.NUMERO_CONSELHO AS NumeroConselho, R.UF_CONSELHO AS Uf,
       R.DATA_HORA_REGS AS RegistradoEm, R.OBSERVACAO AS Observacao,
       R.CODIGO_USUARIO_INCLUSAO AS IncluidoPorId, UI.NOME_COMPLETO AS IncluidoPorNome,
       R.SOLICITADO_POR AS SolicitadoPor, R.AUTORIZADO_POR AS AutorizadoPor, R.OBS_AUTORIZACAO AS ObsAutorizacao,
       (SELECT MAX(K.DATA_HORA_CORRECAO) FROM DBO.PRONTUARIO_CORRECAO K WHERE K.CODIGO_REGS_PRNT = R.CODIGO_REGS_PRNT) AS CorrigidoEm,
       DA.CODIGO_PUBLIC AS DocId, DA.NOME_ARQUIVO AS DocNome, DA.TAMANHO_BYTES AS DocTamanho
FROM DBO.REGISTRO_PRONTUARIO R
JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = R.CODIGO_PACIENTE
LEFT JOIN DBO.CONSULTA C ON C.CODIGO_CONSULTA = R.CODIGO_CONSULTA
LEFT JOIN DBO.USUARIO UI ON UI.CODIGO_USUARIO = R.CODIGO_USUARIO_INCLUSAO
LEFT JOIN DBO.PRONTUARIO_AUTORIZACAO_DOC DA ON DA.CODIGO_AUTORIZACAO_DOC = R.CODIGO_AUTORIZACAO_DOC";

    private static RegistroProntuarioDto ParaDto(Linha l) => new()
    {
        Id = l.Id,
        Paciente = new PacienteReferenciaDto { Id = l.PacienteId, Nome = l.PacienteNome },
        ConsultaId = l.ConsultaId,
        Profissional = new ProfissionalRegistroDto
        {
            Id = l.ProfissionalId, Nome = l.ProfissionalNome, RegistroProfissional = l.Conselho,
            NumeroConselho = l.NumeroConselho, UfConselho = l.Uf
        },
        RegistradoEm = DataBrasil.Utc(l.RegistradoEm),
        Observacao = l.Observacao,
        InclusaoAdministrativa = l.IncluidoPorId.HasValue
            ? new InclusaoAdministrativaDto
            {
                IncluidoPor = new UsuarioReferenciaDto { Id = l.IncluidoPorId.Value, NomeCompleto = l.IncluidoPorNome ?? string.Empty },
                SolicitadoPor = l.SolicitadoPor ?? string.Empty,
                AutorizadoPor = l.AutorizadoPor ?? string.Empty,
                ObservacaoAutorizacao = l.ObsAutorizacao ?? string.Empty,
                Documento = l.DocId.HasValue ? new DocumentoAutorizacaoDto { Id = l.DocId.Value, NomeArquivo = l.DocNome ?? string.Empty, TamanhoBytes = l.DocTamanho ?? 0 } : null
            }
            : null,
        Corrigido = l.CorrigidoEm.HasValue,
        CorrigidoEm = DataBrasil.Utc(l.CorrigidoEm)
    };

    // ------------------------------------------------------------- acesso (H1)
    /// <summary>Só o profissional responsável atual (RN16) ou o administrador acessam. A tentativa negada também vai para o log (H2).</summary>
    private async Task<PacienteAcesso> ExigirAcessoAsync(SqlConnection con, SqlTransaction? tx, Guid pacienteId, UsuarioAtual usuario, string operacao, CancellationToken ct)
    {
        var paciente = await con.QueryFirstOrDefaultAsync<PacienteAcesso>(Sql.Cmd(@"
SELECT CODIGO_PACIENTE AS Codigo, CODIGO_PUBLIC AS Id, NOME AS Nome, ATIVO AS Ativo, CODIGO_PROFISSIONAL_RESPONSAVEL AS ProfissionalResponsavel
FROM DBO.PACIENTE WHERE CODIGO_PUBLIC = @Id;", new { Id = pacienteId }, tx, ct))
            ?? throw Erro.NaoEncontrado("Paciente não encontrado.");

        var autorizado = usuario.Administrador || (usuario.ProfissionalId.HasValue && usuario.ProfissionalId.Value == paciente.ProfissionalResponsavel);
        if (!autorizado)
        {
            await _auditoria.RegistrarAsync("VISUALIZAR", "REGISTRO_PRONTUARIO", null, paciente.Codigo, usuario.Id,
                new { negado = true, operacao }, null, null, ct);
            throw Erro.SemPermissao(MensagemAcessoNegado, "ACESSO_PRONTUARIO_NEGADO");
        }
        return paciente;
    }

    // ------------------------------------------------------------------ lista
    public async Task<RegistroProntuarioPaginaDto> ListarAsync(Guid pacienteId, FiltroProntuario f, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (f.DataInicial.HasValue && f.DataFinal.HasValue && f.DataFinal < f.DataInicial) erros.Add(new ErroCampo("dataFinal", "Deve ser igual ou posterior à data inicial"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        await using var con = await _db.AbrirAsync(ct);
        var paciente = await ExigirAcessoAsync(con, null, pacienteId, usuario, "listar", ct);

        var p = new DynamicParameters();
        p.Add("Paciente", paciente.Codigo);
        var onde = new StringBuilder(" WHERE R.CODIGO_PACIENTE = @Paciente");
        if (f.DataInicial.HasValue) { onde.Append(" AND R.DATA_HORA_REGS >= @Ini"); p.Add("Ini", DataBrasil.ParaUtc(f.DataInicial.Value.ToDateTime(TimeOnly.MinValue))); }
        if (f.DataFinal.HasValue) { onde.Append(" AND R.DATA_HORA_REGS < @Fim"); p.Add("Fim", DataBrasil.ParaUtc(f.DataFinal.Value.AddDays(1).ToDateTime(TimeOnly.MinValue))); }
        if (f.ProfissionalId.HasValue) { onde.Append(" AND R.CODIGO_PROFISSIONAL = @Prof"); p.Add("Prof", f.ProfissionalId.Value); }
        if (f.ConsultaId.HasValue) { onde.Append(" AND C.CODIGO_PUBLIC = @Consulta"); p.Add("Consulta", f.ConsultaId.Value); }

        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.REGISTRO_PRONTUARIO R LEFT JOIN DBO.CONSULTA C ON C.CODIGO_CONSULTA = R.CODIGO_CONSULTA" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = (await con.QueryAsync<Linha>(Sql.Cmd(
            Select + onde + " ORDER BY R.DATA_HORA_REGS DESC, R.CODIGO_REGS_PRNT DESC OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct))).ToList();

        await _auditoria.RegistrarAsync("VISUALIZAR", "REGISTRO_PRONTUARIO", null, paciente.Codigo, usuario.Id,
            new { quantidade = linhas.Count, pagina = f.Pagina }, con, null, ct);

        return new RegistroProntuarioPaginaDto { Itens = linhas.Select(ParaDto).ToList(), Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total };
    }

    // ------------------------------------------------------------------ obter
    public async Task<RegistroProntuarioDto> ObterAsync(Guid pacienteId, Guid registroId, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var paciente = await ExigirAcessoAsync(con, null, pacienteId, usuario, "consultar", ct);
        var linha = await CarregarAsync(con, null, registroId, paciente.Codigo, ct) ?? throw Erro.NaoEncontrado("Registro não encontrado.");
        await _auditoria.RegistrarAsync("VISUALIZAR", "REGISTRO_PRONTUARIO", linha.Codigo, paciente.Codigo, usuario.Id, null, con, null, ct);
        return ParaDto(linha);
    }

    private static Task<Linha?> CarregarAsync(SqlConnection con, SqlTransaction? tx, Guid registroId, int pacienteCodigo, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(Select + " WHERE R.CODIGO_PUBLIC = @Id AND R.CODIGO_PACIENTE = @Paciente;",
            new { Id = registroId, Paciente = pacienteCodigo }, tx, ct));

    // --------------------------------------------------------------- inclusão
    public async Task<RegistroProntuarioDto> IncluirAsync(Guid pacienteId, RegistroProntuarioEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");

        var e = new List<ErroCampo>();
        var texto = d.Observacao?.Trim();
        if (string.IsNullOrEmpty(texto)) e.Add(new ErroCampo("observacao", NaoPreenchido));
        else if (texto.Length > 20000) e.Add(new ErroCampo("observacao", "Máximo de 20.000 caracteres"));

        var administrador = usuario.Administrador;
        if (administrador)
        {
            if (d.ProfissionalId is null) e.Add(new ErroCampo("profissionalId", "Informe o profissional que solicitou o registro"));
            ValidarAutorizacao(d.Autorizacao, e);
        }
        else
        {
            if (d.ProfissionalId.HasValue) e.Add(new ErroCampo("profissionalId", "Não deve ser informado: o registro fica em nome do profissional logado"));
            if (d.Autorizacao is not null) e.Add(new ErroCampo("autorizacao", "Não deve ser informada pelo profissional"));
        }
        if (e.Count > 0) throw Erro.Validacao(e);

        await using var con = await _db.AbrirAsync(ct);
        var paciente = await ExigirAcessoAsync(con, null, pacienteId, usuario, "incluir", ct);
        if (!paciente.Ativo) throw Erro.Conflito("O paciente está inativo e não recebe novos registros.", "PACIENTE_INATIVO");

        var profissionalId = administrador ? d.ProfissionalId!.Value : usuario.ProfissionalId!.Value;
        var prof = await con.QueryFirstOrDefaultAsync<ProfissionalDados>(Sql.Cmd(@"
SELECT NOME AS Nome, REGISTRO_PROFISSIONAL AS Conselho, NUMERO_CONSELHO AS Numero, UF_CONSELHO AS Uf, ATIVO AS Ativo
FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id;", new { Id = profissionalId }, ct: ct));
        if (prof is null) throw Erro.Validacao("profissionalId", "Profissional não encontrado");
        if (!prof.Ativo) throw Erro.Validacao("profissionalId", "O profissional está inativo");
        if (string.IsNullOrWhiteSpace(prof.Conselho) || string.IsNullOrWhiteSpace(prof.Numero) || string.IsNullOrWhiteSpace(prof.Uf))
            throw Erro.Validacao("profissionalId", "O profissional não tem CRM ou CRP cadastrado");

        int? consultaCodigo = null;
        if (d.ConsultaId.HasValue)
        {
            consultaCodigo = await con.ExecuteScalarAsync<int?>(Sql.Cmd(@"
SELECT CODIGO_CONSULTA FROM DBO.CONSULTA
WHERE CODIGO_PUBLIC = @Id AND CODIGO_PACIENTE = @Pac AND CODIGO_PROFISSIONAL = @Prof AND STATUS IN ('EM_ATENDIMENTO', 'CONCLUIDA');",
                new { Id = d.ConsultaId.Value, Pac = paciente.Codigo, Prof = profissionalId }, ct: ct));
            if (consultaCodigo is null)
                throw Erro.Regra("CONSULTA_INVALIDA", "A consulta deve ser deste paciente e do profissional do registro, em atendimento ou concluída.");
        }

        // H4: registro idêntico ao anterior, do mesmo profissional e paciente, em menos de 2 minutos. O texto é comparado aqui (campo criptografado).
        var ultimo = await con.QueryFirstOrDefaultAsync<UltimoRegistro>(Sql.Cmd(@"
SELECT TOP 1 OBSERVACAO AS Observacao, DATA_HORA_REGS AS Quando FROM DBO.REGISTRO_PRONTUARIO
WHERE CODIGO_PACIENTE = @Pac AND CODIGO_PROFISSIONAL = @Prof ORDER BY DATA_HORA_REGS DESC, CODIGO_REGS_PRNT DESC;",
            new { Pac = paciente.Codigo, Prof = profissionalId }, ct: ct));
        if (ultimo is not null && ultimo.Observacao.Trim() == texto && DateTime.UtcNow - DataBrasil.Utc(ultimo.Quando) < TimeSpan.FromMinutes(2))
            throw Erro.Conflito("Este registro é igual ao anterior, salvo há menos de 2 minutos.", "REGISTRO_DUPLICADO");

        long codigo;
        if (administrador)
        {
            var a = d.Autorizacao!;
            var (solicitante, docCodigo) = await ResolverAutorizacaoAsync(con, a, paciente.Codigo, usuario, ct);
            Guid publico;
            try
            {
                publico = await con.QuerySingleAsync<Guid>(new CommandDefinition("DBO.USP_PRONTUARIO_INCLUIR_ADMIN", new
                {
                    CODIGO_USUARIO = usuario.Id,
                    CODIGO_PUBLIC_PACIENTE = pacienteId,
                    CODIGO_PROFISSIONAL = profissionalId,
                    CODIGO_PUBLIC_CONSULTA = d.ConsultaId,
                    OBSERVACAO = texto,
                    SOLICITADO_POR = solicitante,
                    AUTORIZADO_POR = a.AutorizadoPor!.Trim(),
                    OBS_AUTORIZACAO = a.Observacao!.Trim(),
                    CODIGO_AUTORIZACAO_DOC = docCodigo,
                    IP = Ip()
                }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
            }
            catch (SqlException ex) when (ex.Number is >= 50060 and <= 50066)
            {
                throw TraduzirErroBanco(ex);
            }
            codigo = await con.ExecuteScalarAsync<long>(Sql.Cmd(
                "SELECT CODIGO_REGS_PRNT FROM DBO.REGISTRO_PRONTUARIO WHERE CODIGO_PUBLIC = @Id;", new { Id = publico }, ct: ct));
        }
        else
        {
            await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
            // O gatilho da tabela impede OUTPUT sem INTO: o código vem do SCOPE_IDENTITY.
            codigo = await con.ExecuteScalarAsync<long>(Sql.Cmd(@"
INSERT INTO DBO.REGISTRO_PRONTUARIO (CODIGO_PACIENTE, CODIGO_CONSULTA, CODIGO_PROFISSIONAL, NOME_PROFISSIONAL, REGISTRO_PROFISSIONAL, NUMERO_CONSELHO, UF_CONSELHO, OBSERVACAO)
VALUES (@Pac, @Consulta, @Prof, @Nome, @Conselho, @Numero, @Uf, @Texto);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
                new { Pac = paciente.Codigo, Consulta = consultaCodigo, Prof = profissionalId, prof.Nome, prof.Conselho, prof.Numero, prof.Uf, Texto = texto }, tx, ct));
            await _auditoria.RegistrarAsync("INCLUIR", "REGISTRO_PRONTUARIO", codigo, paciente.Codigo, usuario.Id,
                new { registros = 1, profissional = prof.Nome }, con, tx, ct);
            await tx.CommitAsync(ct);
        }

        var linha = await con.QueryFirstAsync<Linha>(Sql.Cmd(Select + " WHERE R.CODIGO_REGS_PRNT = @Codigo;", new { Codigo = codigo }, ct: ct));
        return ParaDto(linha);
    }

    // ----------------------------------------------------------------- edição
    public async Task<RegistroProntuarioDto> EditarAsync(Guid pacienteId, Guid registroId, RegistroProntuarioEdicao? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        if (!usuario.Administrador)
            throw Erro.SemPermissao("Só o administrador edita um registro já salvo, com a solicitação do profissional e o OK do responsável legal.", "ACESSO_PRONTUARIO_NEGADO");

        var e = new List<ErroCampo>();
        var texto = d.Observacao?.Trim();
        if (string.IsNullOrEmpty(texto)) e.Add(new ErroCampo("observacao", NaoPreenchido));
        else if (texto.Length > 20000) e.Add(new ErroCampo("observacao", "Máximo de 20.000 caracteres"));
        ValidarAutorizacao(d.Autorizacao, e);
        if (e.Count > 0) throw Erro.Validacao(e);

        await using var con = await _db.AbrirAsync(ct);
        var paciente = await ExigirAcessoAsync(con, null, pacienteId, usuario, "editar", ct);
        var atual = await CarregarAsync(con, null, registroId, paciente.Codigo, ct) ?? throw Erro.NaoEncontrado("Registro não encontrado.");
        if (atual.Observacao.Trim() == texto) throw Erro.Conflito("O texto é igual ao atual.", "SEM_ALTERACAO");

        var a = d.Autorizacao!;
        var (solicitante, docCodigo) = await ResolverAutorizacaoAsync(con, a, paciente.Codigo, usuario, ct);
        try
        {
            await con.ExecuteAsync(new CommandDefinition("DBO.USP_PRONTUARIO_EDITAR_ADMIN", new
            {
                CODIGO_USUARIO = usuario.Id,
                CODIGO_PUBLIC_PACIENTE = pacienteId,
                CODIGO_PUBLIC_REGISTRO = registroId,
                OBSERVACAO = texto,
                SOLICITADO_POR = solicitante,
                AUTORIZADO_POR = a.AutorizadoPor!.Trim(),
                OBS_AUTORIZACAO = a.Observacao!.Trim(),
                CODIGO_AUTORIZACAO_DOC = docCodigo,
                IP = Ip()
            }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number is >= 50060 and <= 50066)
        {
            throw TraduzirErroBanco(ex);
        }

        // O gatilho da tabela já gravou a correção e o evento no log.
        var nova = await CarregarAsync(con, null, registroId, paciente.Codigo, ct) ?? throw new InvalidOperationException("Registro não encontrado após a edição.");
        return ParaDto(nova);
    }

    // ------------------------------------------------------------- correções
    public async Task<List<CorrecaoProntuarioDto>> CorrecoesAsync(Guid pacienteId, Guid registroId, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var paciente = await ExigirAcessoAsync(con, null, pacienteId, usuario, "correcoes", ct);
        var codigo = await con.ExecuteScalarAsync<long?>(Sql.Cmd(
            "SELECT CODIGO_REGS_PRNT FROM DBO.REGISTRO_PRONTUARIO WHERE CODIGO_PUBLIC = @Id AND CODIGO_PACIENTE = @Pac;",
            new { Id = registroId, Pac = paciente.Codigo }, ct: ct)) ?? throw Erro.NaoEncontrado("Registro não encontrado.");

        var itens = (await con.QueryAsync<CorrecaoLinha>(Sql.Cmd(@"
SELECT K.ORIGEM AS Origem, K.OBSERVACAO_ANTERIOR AS ObservacaoAnterior, K.OBSERVACAO_NOVO AS ObservacaoNova,
       K.SOLICITADO_POR AS SolicitadoPor, K.AUTORIZADO_USUARIO AS AutorizadoPor, K.MOTIVO AS ObservacaoAutorizacao,
       CASE WHEN K.ORIGEM = 'SISTEMA' THEN COALESCE(U.NOME_COMPLETO, K.CORRECAO_USUARIO) ELSE K.CORRECAO_USUARIO END AS ExecutadoPor,
       K.DATA_HORA_CORRECAO AS CorrigidoEm,
       DA.CODIGO_PUBLIC AS DocId, DA.NOME_ARQUIVO AS DocNome, DA.TAMANHO_BYTES AS DocTamanho
FROM DBO.PRONTUARIO_CORRECAO K
LEFT JOIN DBO.USUARIO U ON U.CODIGO_USUARIO = K.CODIGO_USUARIO
LEFT JOIN DBO.PRONTUARIO_AUTORIZACAO_DOC DA ON DA.CODIGO_AUTORIZACAO_DOC = K.CODIGO_AUTORIZACAO_DOC
WHERE K.CODIGO_REGS_PRNT = @Codigo
ORDER BY K.DATA_HORA_CORRECAO DESC, K.CODIGO_PRNT_CORRECAO DESC;", new { Codigo = codigo }, ct: ct))).ToList();

        await _auditoria.RegistrarAsync("VISUALIZAR", "PRONTUARIO_CORRECAO", codigo, paciente.Codigo, usuario.Id,
            new { quantidade = itens.Count }, con, null, ct);

        return itens.Select(c => new CorrecaoProntuarioDto
        {
            Origem = c.Origem, ObservacaoAnterior = c.ObservacaoAnterior, ObservacaoNova = c.ObservacaoNova,
            SolicitadoPor = c.SolicitadoPor ?? string.Empty, AutorizadoPor = c.AutorizadoPor,
            ObservacaoAutorizacao = c.ObservacaoAutorizacao, ExecutadoPor = c.ExecutadoPor,
            CorrigidoEm = DataBrasil.Utc(c.CorrigidoEm),
            Documento = c.DocId.HasValue ? new DocumentoAutorizacaoDto { Id = c.DocId.Value, NomeArquivo = c.DocNome ?? string.Empty, TamanhoBytes = c.DocTamanho ?? 0 } : null
        }).ToList();
    }

    private sealed class CorrecaoLinha
    {
        public string Origem { get; set; } = string.Empty;
        public string ObservacaoAnterior { get; set; } = string.Empty;
        public string ObservacaoNova { get; set; } = string.Empty;
        public string? SolicitadoPor { get; set; }
        public string AutorizadoPor { get; set; } = string.Empty;
        public string ObservacaoAutorizacao { get; set; } = string.Empty;
        public string ExecutadoPor { get; set; } = string.Empty;
        public DateTime CorrigidoEm { get; set; }
        public Guid? DocId { get; set; }
        public string? DocNome { get; set; }
        public int? DocTamanho { get; set; }
    }

    // ------------------------------------------------------ transferência
    /// <summary>
    /// Transfere o prontuário: o profissional responsável do paciente passa a ser o novo (RN18). O anterior deixa de ver e de preencher (RN16);
    /// o novo vê todos os registros, inclusive os antigos, que seguem em nome de quem os escreveu (RN19). Fica no histórico com o motivo e no log.
    /// </summary>
    public async Task<TransferenciaProntuarioDto> TransferirAsync(Guid pacienteId, TransferenciaProntuarioEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var e = new List<ErroCampo>();
        if (d.ProfissionalId is null or <= 0) e.Add(new ErroCampo("profissionalId", NaoPreenchido));
        var motivo = d.Motivo?.Trim();
        if (string.IsNullOrEmpty(motivo)) e.Add(new ErroCampo("motivo", NaoPreenchido));
        else if (motivo.Length is < 10 or > 500) e.Add(new ErroCampo("motivo", "Entre 10 e 500 caracteres, sem texto clínico"));
        if (e.Count > 0) throw Erro.Validacao(e);

        await using var con = await _db.AbrirAsync(ct);
        // A transferência não lê texto clínico: quem tem a liberação (controller) pode fazer, sem ser o responsável atual (H1 vale para ler e escrever).
        var paciente = await con.QueryFirstOrDefaultAsync<PacienteAcesso>(Sql.Cmd(@"
SELECT CODIGO_PACIENTE AS Codigo, CODIGO_PUBLIC AS Id, NOME AS Nome, ATIVO AS Ativo, CODIGO_PROFISSIONAL_RESPONSAVEL AS ProfissionalResponsavel
FROM DBO.PACIENTE WHERE CODIGO_PUBLIC = @Id;", new { Id = pacienteId }, ct: ct))
            ?? throw Erro.NaoEncontrado("Paciente não encontrado.");
        if (paciente.ProfissionalResponsavel == d.ProfissionalId!.Value)
            throw Erro.Conflito("Este profissional já é o responsável atual do paciente.", "PROFISSIONAL_JA_RESPONSAVEL");

        var novoNome = await con.ExecuteScalarAsync<string?>(Sql.Cmd(
            "SELECT NOME FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id AND ATIVO = 1;", new { Id = d.ProfissionalId.Value }, ct: ct));
        if (string.IsNullOrWhiteSpace(novoNome)) throw Erro.Validacao("profissionalId", "Profissional não encontrado ou inativo");

        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.PACIENTE_PROFISSIONAL_HISTORICO SET DATA_FIM = SYSUTCDATETIME()
WHERE CODIGO_PACIENTE = @Paciente AND DATA_FIM IS NULL;
INSERT INTO DBO.PACIENTE_PROFISSIONAL_HISTORICO (CODIGO_PACIENTE, CODIGO_PROFISSIONAL, DATA_INICIO, ALTERACAO_USUARIO, MOTIVO)
VALUES (@Paciente, @Profissional, SYSUTCDATETIME(), @Usuario, @Motivo);
UPDATE DBO.PACIENTE SET CODIGO_PROFISSIONAL_RESPONSAVEL = @Profissional,
       DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_PACIENTE = @Paciente;",
            new { Paciente = paciente.Codigo, Profissional = d.ProfissionalId.Value, Usuario = usuario.Id, Motivo = motivo }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "PACIENTE", paciente.Codigo, paciente.Codigo, usuario.Id,
            new { origem = "transferencia-prontuario", de = paciente.ProfissionalResponsavel, para = d.ProfissionalId.Value, motivo }, con, tx, ct);
        var futuras = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.CONSULTA
WHERE CODIGO_PACIENTE = @Paciente AND CODIGO_PROFISSIONAL = @Anterior AND STATUS IN ('AGENDADA', 'CONFIRMADA') AND DATA_INICIO >= SYSUTCDATETIME();",
            new { Paciente = paciente.Codigo, Anterior = paciente.ProfissionalResponsavel }, tx, ct));
        await tx.CommitAsync(ct);

        return new TransferenciaProntuarioDto
        {
            ProfissionalResponsavel = new ReferenciaDto { Id = d.ProfissionalId.Value, Nome = novoNome.Trim() },
            TransferidoEm = DateTime.UtcNow,
            ConsultasFuturasComProfissionalAnterior = futuras
        };
    }

    // ------------------------------------------------- documento de autorização
    private sealed class DocLinha
    {
        public long Codigo { get; set; }
    }

    /// <summary>Resolve o profissional que solicitou (nome gravado) e confere o documento: do mesmo paciente, enviado por este usuário e ainda não usado.</summary>
    private static async Task<(string Solicitante, long DocCodigo)> ResolverAutorizacaoAsync(SqlConnection con, AutorizacaoProntuarioEntrada a, int pacienteCodigo, UsuarioAtual usuario, CancellationToken ct)
    {
        var nome = await con.ExecuteScalarAsync<string?>(Sql.Cmd(
            "SELECT NOME FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id AND ATIVO = 1;", new { Id = a.SolicitadoPorProfissionalId }, ct: ct));
        if (string.IsNullOrWhiteSpace(nome))
            throw Erro.Validacao("autorizacao.solicitadoPorProfissionalId", "Profissional não encontrado ou inativo");

        var doc = await con.QueryFirstOrDefaultAsync<DocLinha>(Sql.Cmd(@"
SELECT D.CODIGO_AUTORIZACAO_DOC AS Codigo FROM DBO.PRONTUARIO_AUTORIZACAO_DOC D
WHERE D.CODIGO_PUBLIC = @Id AND D.CODIGO_PACIENTE = @Pac AND D.CODIGO_USUARIO = @Usu
  AND NOT EXISTS (SELECT 1 FROM DBO.REGISTRO_PRONTUARIO R WHERE R.CODIGO_AUTORIZACAO_DOC = D.CODIGO_AUTORIZACAO_DOC)
  AND NOT EXISTS (SELECT 1 FROM DBO.PRONTUARIO_CORRECAO K WHERE K.CODIGO_AUTORIZACAO_DOC = D.CODIGO_AUTORIZACAO_DOC);",
            new { Id = a.DocumentoId, Pac = pacienteCodigo, Usu = usuario.Id }, ct: ct))
            ?? throw Erro.Validacao("autorizacao.documentoId", "Documento inválido ou já utilizado. Anexe o documento novamente");
        return (nome.Trim(), doc.Codigo);
    }

    /// <summary>Tipo real do arquivo pelos primeiros bytes (PDF, JPEG ou PNG); a extensão não vale.</summary>
    private static string? TipoDoArquivo(byte[] b)
    {
        if (b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D) return "application/pdf";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "image/png";
        return null;
    }

    public async Task<DocumentoAutorizacaoDto> EnviarDocumentoAsync(Guid pacienteId, string nomeOriginal, byte[] conteudo, UsuarioAtual usuario, CancellationToken ct)
    {
        if (!usuario.Administrador)
            throw Erro.SemPermissao("Só o administrador anexa documento de autorização.", "ACESSO_PRONTUARIO_NEGADO");
        if (conteudo.Length == 0) throw Erro.Validacao("arquivo", "Anexe o documento de autorização");
        if (conteudo.Length > TamanhoMaximoDocumento) throw Erro.Validacao("arquivo", "O documento deve ter no máximo 5 MB");
        var tipo = TipoDoArquivo(conteudo) ?? throw Erro.Validacao("arquivo", "Use um arquivo PDF, JPG ou PNG");

        var nome = Path.GetFileName((nomeOriginal ?? string.Empty).Replace('\\', '/')).Trim();
        if (nome.Length == 0) nome = "autorizacao";
        if (nome.Length > 255) nome = nome[^255..];
        var hash = Convert.ToHexString(SHA256.HashData(conteudo)).ToLowerInvariant();

        await using var con = await _db.AbrirAsync(ct);
        var paciente = await ExigirAcessoAsync(con, null, pacienteId, usuario, "anexar", ct);
        var publico = Guid.NewGuid();
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        // A tabela tem gatilho: o código vem do SCOPE_IDENTITY (sem OUTPUT).
        var codigo = await con.ExecuteScalarAsync<long>(Sql.Cmd(@"
INSERT INTO DBO.PRONTUARIO_AUTORIZACAO_DOC (CODIGO_PUBLIC, CODIGO_PACIENTE, NOME_ARQUIVO, TIPO_CONTEUDO, TAMANHO_BYTES, HASH_SHA256, CONTEUDO, CODIGO_USUARIO)
VALUES (@Publico, @Pac, @Nome, @Tipo, @Tamanho, @Hash, @Conteudo, @Usu);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);",
            new { Publico = publico, Pac = paciente.Codigo, Nome = nome, Tipo = tipo, Tamanho = conteudo.Length, Hash = hash, Conteudo = conteudo, Usu = usuario.Id }, tx, ct));
        await _auditoria.RegistrarAsync("INCLUIR", "PRONTUARIO_AUTORIZACAO_DOC", codigo, paciente.Codigo, usuario.Id,
            new { arquivo = nome, tamanho = conteudo.Length, sha256 = hash }, con, tx, ct);
        await tx.CommitAsync(ct);
        return new DocumentoAutorizacaoDto { Id = publico, NomeArquivo = nome, TamanhoBytes = conteudo.Length };
    }

    private sealed class DocConteudo
    {
        public long Codigo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public byte[] Conteudo { get; set; } = Array.Empty<byte>();
    }

    public async Task<(byte[] Conteudo, string Nome, string Tipo)> BaixarDocumentoAsync(Guid pacienteId, Guid documentoId, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var paciente = await ExigirAcessoAsync(con, null, pacienteId, usuario, "documento", ct);
        var d = await con.QueryFirstOrDefaultAsync<DocConteudo>(Sql.Cmd(@"
SELECT CODIGO_AUTORIZACAO_DOC AS Codigo, NOME_ARQUIVO AS Nome, TIPO_CONTEUDO AS Tipo, CONTEUDO AS Conteudo
FROM DBO.PRONTUARIO_AUTORIZACAO_DOC WHERE CODIGO_PUBLIC = @Id AND CODIGO_PACIENTE = @Pac;",
            new { Id = documentoId, Pac = paciente.Codigo }, ct: ct)) ?? throw Erro.NaoEncontrado("Documento não encontrado.");
        await _auditoria.RegistrarAsync("VISUALIZAR", "PRONTUARIO_AUTORIZACAO_DOC", d.Codigo, paciente.Codigo, usuario.Id, new { arquivo = d.Nome }, con, null, ct);
        return (d.Conteudo, d.Nome, d.Tipo);
    }

    // ---------------------------------------------------------------- apoio
    private string? Ip()
    {
        var ip = _http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        return ip is { Length: > 45 } ? ip[..45] : ip;
    }

    private static void ValidarAutorizacao(AutorizacaoProntuarioEntrada? a, List<ErroCampo> e)
    {
        if (a is null) { e.Add(new ErroCampo("autorizacao", "Informe quem solicitou, quem autorizou, a observação e o documento da autorização")); return; }
        var aut = a.AutorizadoPor?.Trim();
        var obs = a.Observacao?.Trim();
        if (a.SolicitadoPorProfissionalId is null) e.Add(new ErroCampo("autorizacao.solicitadoPorProfissionalId", NaoPreenchido));
        if (a.DocumentoId is null) e.Add(new ErroCampo("autorizacao.documentoId", "Anexe o documento de autorização"));
        if (string.IsNullOrEmpty(aut)) e.Add(new ErroCampo("autorizacao.autorizadoPor", NaoPreenchido));
        else if (aut.Length is < 3 or > 150) e.Add(new ErroCampo("autorizacao.autorizadoPor", "Entre 3 e 150 caracteres"));
        if (string.IsNullOrEmpty(obs)) e.Add(new ErroCampo("autorizacao.observacao", NaoPreenchido));
        else if (obs.Length is < 10 or > 1000) e.Add(new ErroCampo("autorizacao.observacao", "Entre 10 e 1.000 caracteres, sem texto clínico"));
    }

    private static ApiException TraduzirErroBanco(SqlException ex) => ex.Number switch
    {
        50060 => Erro.SemPermissao(ex.Message, "ACESSO_PRONTUARIO_NEGADO"),
        50061 => Erro.NaoEncontrado("Registro não encontrado."),
        50062 => Erro.Validacao("autorizacao", ex.Message),
        50066 => Erro.Validacao("autorizacao.documentoId", "Anexe o documento de autorização"),
        50063 => Erro.Conflito("O paciente está inativo e não recebe novos registros.", "PACIENTE_INATIVO"),
        50064 => Erro.Validacao("profissionalId", "Profissional inexistente ou inativo"),
        _ => Erro.Regra("CONSULTA_INVALIDA", "A consulta deve ser deste paciente e do profissional do registro.")
    };
}
