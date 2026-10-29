using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Faturamento;

/// <summary>
/// Guias do convênio. No convênio o pagamento só vale quando o plano aprova a guia, e quem confirma é o faturamento.
/// Guia AUTORIZADA confirma o pagamento da consulta; PENDENTE ou NEGADA desfaz essa confirmação (correção do faturamento).
/// </summary>
public sealed class GuiaService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private static readonly string[] StatusGuia = { "PENDENTE", "AUTORIZADA", "NEGADA" };
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public GuiaService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private sealed class Linha
    {
        public int Codigo { get; set; }
        public Guid ConsultaId { get; set; }
        public DateTime Inicio { get; set; }
        public string StatusConsulta { get; set; } = string.Empty;
        public string Paciente { get; set; } = string.Empty;
        public int ConvenioId { get; set; }
        public string ConvenioNome { get; set; } = string.Empty;
        public string? NumeroCarteirinha { get; set; }
        public int EspecialidadeId { get; set; }
        public string EspecialidadeNome { get; set; } = string.Empty;
        public int ProfissionalId { get; set; }
        public string ProfissionalNome { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public string? GuiaStatus { get; set; }
        public string? GuiaNumero { get; set; }
        public string? GuiaObservacao { get; set; }
        public DateTime? GuiaEm { get; set; }
        public int? GuiaPorId { get; set; }
        public string? GuiaPorNome { get; set; }
        public string? LiberacaoAdmin { get; set; }
        public string TipoAtendimento { get; set; } = string.Empty;
    }

    private const string Select = @"
SELECT C.CODIGO_CONSULTA AS Codigo, C.CODIGO_PUBLIC AS ConsultaId, C.DATA_INICIO AS Inicio, C.STATUS AS StatusConsulta, P.NOME AS Paciente,
       C.CODIGO_CONVENIO AS ConvenioId, COALESCE(CV.NOME_FANTASIA, CV.RAZAO_SOCIAL) AS ConvenioNome, C.NUMERO_CARTEIRINHA AS NumeroCarteirinha,
       C.CODIGO_ESPECIALIDADE AS EspecialidadeId, E.NOME AS EspecialidadeNome, C.CODIGO_PROFISSIONAL AS ProfissionalId, PR.NOME AS ProfissionalNome,
       C.VALOR AS Valor, C.GUIA_STATUS AS GuiaStatus, C.GUIA_NUMERO AS GuiaNumero, C.GUIA_OBSERVACAO AS GuiaObservacao, C.GUIA_DATA AS GuiaEm,
       C.GUIA_USUARIO AS GuiaPorId, UG.NOME_COMPLETO AS GuiaPorNome, C.TIPO_ATENDIMENTO AS TipoAtendimento, C.LIBERACAO_ADMIN_JUSTIFICATIVA AS LiberacaoAdmin
FROM DBO.CONSULTA C
JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = C.CODIGO_PACIENTE
JOIN DBO.CONVENIO CV ON CV.CODIGO_CONVENIO = C.CODIGO_CONVENIO
JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = C.CODIGO_ESPECIALIDADE
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = C.CODIGO_PROFISSIONAL
LEFT JOIN DBO.USUARIO UG ON UG.CODIGO_USUARIO = C.GUIA_USUARIO";

    private static GuiaItemDto ParaDto(Linha l) => new()
    {
        ConsultaId = l.ConsultaId,
        Inicio = DataBrasil.Utc(l.Inicio),
        StatusConsulta = l.StatusConsulta,
        Paciente = l.Paciente,
        Convenio = new ReferenciaDto { Id = l.ConvenioId, Nome = l.ConvenioNome },
        NumeroCarteirinha = l.NumeroCarteirinha,
        Especialidade = new ReferenciaDto { Id = l.EspecialidadeId, Nome = l.EspecialidadeNome },
        Profissional = new ReferenciaDto { Id = l.ProfissionalId, Nome = l.ProfissionalNome },
        Valor = l.Valor,
        GuiaStatus = l.GuiaStatus ?? "PENDENTE",
        GuiaNumero = l.GuiaNumero,
        GuiaObservacao = l.GuiaObservacao,
        GuiaEm = l.GuiaEm.HasValue ? DataBrasil.Utc(l.GuiaEm.Value) : null,
        LiberacaoAdministrador = l.LiberacaoAdmin,
        GuiaPor = l.GuiaPorId.HasValue ? new UsuarioReferenciaDto { Id = l.GuiaPorId.Value, NomeCompleto = l.GuiaPorNome ?? string.Empty } : null
    };

    // ---------------------------------------------------------------- lista
    public async Task<GuiaPaginaDto> ListarAsync(FiltroGuias f, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (!string.IsNullOrWhiteSpace(f.Status) && !StatusGuia.Contains(f.Status)) erros.Add(new ErroCampo("status", "Status inválido"));
        if (f.DataInicial.HasValue && f.DataFinal.HasValue && f.DataFinal < f.DataInicial) erros.Add(new ErroCampo("dataFinal", "A data final não pode ser anterior à inicial"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        // Só consultas por convênio que ainda contam (canceladas e remarcadas saem; a nova consulta é que vale).
        var onde = new StringBuilder(" WHERE C.TIPO_ATENDIMENTO = 'CONVENIO' AND C.STATUS NOT IN ('CANCELADA', 'REMARCADA')");
        if (!string.IsNullOrWhiteSpace(f.Status))
        {
            onde.Append(f.Status == "PENDENTE" ? " AND ISNULL(C.GUIA_STATUS, 'PENDENTE') = 'PENDENTE'" : " AND C.GUIA_STATUS = @Status");
            if (f.Status != "PENDENTE") p.Add("Status", f.Status);
        }
        if (f.ConvenioId.HasValue) { onde.Append(" AND C.CODIGO_CONVENIO = @ConvenioId"); p.Add("ConvenioId", f.ConvenioId.Value); }
        if (f.DataInicial.HasValue)
        {
            onde.Append(" AND C.DATA_INICIO >= @De");
            p.Add("De", DataBrasil.ParaUtc(f.DataInicial.Value.ToDateTime(TimeOnly.MinValue)));
        }
        if (f.DataFinal.HasValue)
        {
            onde.Append(" AND C.DATA_INICIO < @Ate");
            p.Add("Ate", DataBrasil.ParaUtc(f.DataFinal.Value.AddDays(1).ToDateTime(TimeOnly.MinValue)));
        }
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            onde.Append(" AND (P.NOME LIKE @Busca OR C.GUIA_NUMERO LIKE @Busca OR C.NUMERO_CARTEIRINHA LIKE @Busca)");
            p.Add("Busca", "%" + Formatos.EscaparLike(f.Busca.Trim()) + "%");
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.CONSULTA C JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = C.CODIGO_PACIENTE" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(
            Select + onde + " ORDER BY C.DATA_INICIO DESC, C.CODIGO_CONSULTA DESC OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new GuiaPaginaDto { Itens = linhas.Select(ParaDto).ToList(), Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total };
    }

    // ------------------------------------------------------------ atualização
    public async Task<GuiaItemDto> AtualizarAsync(Guid consultaId, GuiaEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var e = new List<ErroCampo>();
        var numero = d.Numero?.Trim();
        var obs = d.Observacao?.Trim();
        if (string.IsNullOrWhiteSpace(d.Status)) e.Add(new ErroCampo("status", NaoPreenchido));
        else if (!StatusGuia.Contains(d.Status)) e.Add(new ErroCampo("status", "Informe PENDENTE, AUTORIZADA ou NEGADA"));
        if (numero is { Length: > 30 }) e.Add(new ErroCampo("numero", "Máximo de 30 caracteres"));
        if (obs is { Length: > 500 }) e.Add(new ErroCampo("observacao", "Máximo de 500 caracteres"));
        if (d.Status == "AUTORIZADA" && string.IsNullOrEmpty(numero)) e.Add(new ErroCampo("numero", NaoPreenchido));
        if (d.Status == "NEGADA" && string.IsNullOrEmpty(obs)) e.Add(new ErroCampo("observacao", "Informe o motivo da negativa"));
        if (e.Count > 0) throw Erro.Validacao(e);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(Select.Replace("JOIN DBO.CONVENIO CV", "LEFT JOIN DBO.CONVENIO CV")
            + " WHERE C.CODIGO_PUBLIC = @Id;", new { Id = consultaId }, tx, ct))
            ?? throw Erro.NaoEncontrado("Consulta não encontrada.");
        if (atual.TipoAtendimento != "CONVENIO")
            throw Erro.Conflito("A guia só existe em consulta por convênio.", "CONSULTA_NAO_CONVENIO");
        if (atual.StatusConsulta is "CANCELADA" or "REMARCADA")
            throw Erro.Conflito("Consulta cancelada ou remarcada não tem guia para confirmar.", "STATUS_NAO_PERMITE");

        var autorizada = d.Status == "AUTORIZADA";
        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.CONSULTA
SET GUIA_STATUS = @Status, GUIA_NUMERO = @Numero, GUIA_OBSERVACAO = @Obs, GUIA_DATA = SYSUTCDATETIME(), GUIA_USUARIO = @Usuario,
    PAGAMENTO_CONFIRMADO = @Pago,
    DATA_PAGAMENTO = CASE WHEN @Pago = 1 THEN SYSUTCDATETIME() ELSE NULL END,
    PAGAMENTO_CONFIRMADO_USUARIO = CASE WHEN @Pago = 1 THEN @Usuario ELSE NULL END,
    DATA_ATUALIZACAO = SYSUTCDATETIME()
WHERE CODIGO_CONSULTA = @Codigo;",
            new { Status = d.Status, Numero = string.IsNullOrEmpty(numero) ? null : numero, Obs = string.IsNullOrEmpty(obs) ? null : obs, Usuario = usuario.Id, Pago = autorizada, Codigo = atual.Codigo }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "CONSULTA", atual.Codigo, null, usuario.Id,
            new { guia = d.Status, de = atual.GuiaStatus ?? "PENDENTE" }, con, tx, ct);

        var nova = await con.QueryFirstAsync<Linha>(Sql.Cmd(Select + " WHERE C.CODIGO_CONSULTA = @Codigo;", new { Codigo = atual.Codigo }, tx, ct));
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }
}
