using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Pacientes;

/// <summary>
/// Parte 2 do bloco de pacientes: troca do profissional responsável com histórico (RN16, RN18, E6)
/// e consentimentos depois do cadastro (E3).
/// </summary>
public sealed partial class PacienteService
{
    private sealed class HistoricoLinha
    {
        public int ProfissionalId { get; set; }
        public string ProfissionalNome { get; set; } = string.Empty;
        public DateTime DataInicio { get; set; }
        public DateTime? DataFim { get; set; }
        public int? AlteradoPorId { get; set; }
        public string? AlteradoPorNome { get; set; }
    }

    // ------------------------------------------------- troca do profissional responsável
    public async Task<PacienteDto> TrocarProfissionalAsync(Guid id, TrocaProfissionalEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d?.ProfissionalId is null or <= 0) throw Erro.Validacao("profissionalId", "Campo Não Preenchido");

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarLinhaAsync(con, tx, id, usuario, ct)
            ?? throw Erro.NaoEncontrado("Paciente não encontrado.");

        if (atual.ProfissionalId == d.ProfissionalId.Value)
            throw Erro.Conflito("Este profissional já é o responsável atual do paciente.", "PROFISSIONAL_JA_RESPONSAVEL");

        var ativo = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id AND ATIVO = 1;",
            new { Id = d.ProfissionalId.Value }, tx, ct));
        if (ativo == 0) throw Erro.Validacao("profissionalId", "Profissional não encontrado ou inativo");

        // RN18: fecha a linha aberta do histórico e abre a do novo profissional, na mesma transação.
        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.PACIENTE_PROFISSIONAL_HISTORICO SET DATA_FIM = SYSUTCDATETIME()
WHERE CODIGO_PACIENTE = @Paciente AND DATA_FIM IS NULL;
INSERT INTO DBO.PACIENTE_PROFISSIONAL_HISTORICO (CODIGO_PACIENTE, CODIGO_PROFISSIONAL, DATA_INICIO, ALTERACAO_USUARIO)
VALUES (@Paciente, @Profissional, SYSUTCDATETIME(), @Usuario);
UPDATE DBO.PACIENTE SET CODIGO_PROFISSIONAL_RESPONSAVEL = @Profissional,
       DATA_ATUALIZACAO = SYSUTCDATETIME(), ATUALIZACAO_USUARIO = @Usuario
WHERE CODIGO_PACIENTE = @Paciente;",
            new { Paciente = atual.Codigo, Profissional = d.ProfissionalId.Value, Usuario = usuario.Id }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "PACIENTE", atual.Codigo, atual.Codigo, usuario.Id,
            new { campos = new[] { "profissionalResponsavelId" } }, con, tx, ct);

        var nova = await CarregarLinhaPorCodigoAsync(con, tx, atual.Codigo, ct)
            ?? throw new InvalidOperationException("Paciente não encontrado após a troca.");
        var resultado = await MontarAsync(con, tx, nova, ct);

        await tx.CommitAsync(ct);
        return resultado;
    }

    // ---------------------------------------------------- histórico de profissionais
    public async Task<List<HistoricoProfissionalDto>> ListarHistoricoAsync(Guid id, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var paciente = await CarregarLinhaAsync(con, null, id, usuario, ct)
            ?? throw Erro.NaoEncontrado("Paciente não encontrado.");

        var linhas = await con.QueryAsync<HistoricoLinha>(Sql.Cmd(@"
SELECT H.CODIGO_PROFISSIONAL AS ProfissionalId, PR.NOME AS ProfissionalNome, H.DATA_INICIO AS DataInicio, H.DATA_FIM AS DataFim,
       H.ALTERACAO_USUARIO AS AlteradoPorId, U.NOME_COMPLETO AS AlteradoPorNome
FROM DBO.PACIENTE_PROFISSIONAL_HISTORICO H
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = H.CODIGO_PROFISSIONAL
LEFT JOIN DBO.USUARIO U ON U.CODIGO_USUARIO = H.ALTERACAO_USUARIO
WHERE H.CODIGO_PACIENTE = @Codigo
ORDER BY H.DATA_INICIO DESC, H.CODIGO_HISTORICO DESC;", new { paciente.Codigo }, ct: ct));

        await _auditoria.RegistrarAsync("VISUALIZAR", "PACIENTE", paciente.Codigo, paciente.Codigo, usuario.Id,
            new { origem = "profissionais-historico" }, con, ct: ct);

        return linhas.Select(l => new HistoricoProfissionalDto
        {
            Profissional = new ProfissionalReferenciaDto { Id = l.ProfissionalId, Nome = l.ProfissionalNome },
            DataInicio = DataBrasil.Utc(l.DataInicio),
            DataFim = DataBrasil.Utc(l.DataFim),
            AlteradoPor = l.AlteradoPorId.HasValue
                ? new UsuarioReferenciaDto { Id = l.AlteradoPorId.Value, NomeCompleto = l.AlteradoPorNome ?? string.Empty }
                : null
        }).ToList();
    }

    // ------------------------------------------------------------- consentimentos
    public async Task<List<ConsentimentoDto>> ListarConsentimentosAsync(Guid id, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var paciente = await CarregarLinhaAsync(con, null, id, usuario, ct)
            ?? throw Erro.NaoEncontrado("Paciente não encontrado.");

        var linhas = await con.QueryAsync<ConsentimentoLinha>(Sql.Cmd(@"
SELECT C.FINALIDADE AS Finalidade, C.CONCEDIDO AS Concedido, C.ORIGEM AS Origem, C.DATA_REGISTRO AS DataRegistro,
       C.REGISTRO_USUARIO AS RegistradoPorId, U.NOME_COMPLETO AS RegistradoPorNome
FROM DBO.CONSENTIMENTO_LGPD C
LEFT JOIN DBO.USUARIO U ON U.CODIGO_USUARIO = C.REGISTRO_USUARIO
WHERE C.CODIGO_PACIENTE = @Codigo
ORDER BY C.DATA_REGISTRO DESC, C.CODIGO_CONSENTIMENTO DESC;", new { paciente.Codigo }, ct: ct));

        await _auditoria.RegistrarAsync("VISUALIZAR", "PACIENTE", paciente.Codigo, paciente.Codigo, usuario.Id,
            new { origem = "consentimentos" }, con, ct: ct);

        return linhas.Select(ParaConsentimento).ToList();
    }

    public async Task<ConsentimentoDto> RegistrarConsentimentoAsync(Guid id, ConsentimentoEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");

        var e = new List<ErroCampo>();
        if (d.Finalidade is not ("TRATAMENTO_DADOS" or "CONTATO_SMS_WHATSAPP"))
            e.Add(new ErroCampo("finalidade", string.IsNullOrWhiteSpace(d.Finalidade) ? "Campo Não Preenchido" : "Finalidade inválida"));
        if (d.Concedido is null) e.Add(new ErroCampo("concedido", "Campo Não Preenchido"));
        // A origem SISTEMA é reservada ao servidor.
        if (d.Origem is not ("PRESENCIAL" or "TELEFONE"))
            e.Add(new ErroCampo("origem", string.IsNullOrWhiteSpace(d.Origem) ? "Campo Não Preenchido" : "Informe PRESENCIAL ou TELEFONE"));
        if (e.Count > 0) throw Erro.Validacao(e);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var paciente = await CarregarLinhaAsync(con, tx, id, usuario, ct)
            ?? throw Erro.NaoEncontrado("Paciente não encontrado.");

        // Para encerrar o tratamento de dados, o cadastro é inativado (RN14); não se revoga com o paciente ativo.
        if (d.Finalidade == "TRATAMENTO_DADOS" && d.Concedido == false && paciente.Ativo)
            throw Erro.Conflito("Não é possível revogar o tratamento de dados com o paciente ativo. Para encerrar o tratamento, inative o cadastro.",
                "TRATAMENTO_DADOS_PACIENTE_ATIVO");

        await con.ExecuteAsync(Sql.Cmd(@"
INSERT INTO DBO.CONSENTIMENTO_LGPD (CODIGO_PACIENTE, FINALIDADE, CONCEDIDO, ORIGEM, REGISTRO_USUARIO)
VALUES (@Paciente, @Finalidade, @Concedido, @Origem, @Usuario);",
            new { Paciente = paciente.Codigo, d.Finalidade, Concedido = d.Concedido!.Value, d.Origem, Usuario = usuario.Id }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "PACIENTE", paciente.Codigo, paciente.Codigo, usuario.Id,
            new { campos = new[] { "consentimentos" }, finalidade = d.Finalidade, concedido = d.Concedido.Value }, con, tx, ct);

        var gravado = await con.QueryFirstAsync<ConsentimentoLinha>(Sql.Cmd(@"
SELECT TOP 1 C.FINALIDADE AS Finalidade, C.CONCEDIDO AS Concedido, C.ORIGEM AS Origem, C.DATA_REGISTRO AS DataRegistro,
       C.REGISTRO_USUARIO AS RegistradoPorId, U.NOME_COMPLETO AS RegistradoPorNome
FROM DBO.CONSENTIMENTO_LGPD C
LEFT JOIN DBO.USUARIO U ON U.CODIGO_USUARIO = C.REGISTRO_USUARIO
WHERE C.CODIGO_PACIENTE = @Codigo AND C.FINALIDADE = @Finalidade
ORDER BY C.DATA_REGISTRO DESC, C.CODIGO_CONSENTIMENTO DESC;", new { paciente.Codigo, d.Finalidade }, tx, ct));

        await tx.CommitAsync(ct);
        return ParaConsentimento(gravado);
    }

    private static ConsentimentoDto ParaConsentimento(ConsentimentoLinha c) => new()
    {
        Finalidade = c.Finalidade,
        Concedido = c.Concedido,
        Origem = c.Origem,
        DataRegistro = DataBrasil.Utc(c.DataRegistro),
        RegistradoPor = c.RegistradoPorId.HasValue
            ? new UsuarioReferenciaDto { Id = c.RegistradoPorId.Value, NomeCompleto = c.RegistradoPorNome ?? string.Empty }
            : null
    };
}
