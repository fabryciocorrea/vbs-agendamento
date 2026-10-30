using System.Globalization;
using System.Text;
using Dapper;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Faturamento;

/// <summary>
/// Informações financeiras do faturamento. Só entram as consultas CONCLUIDAS no período (data da consulta, horário de Brasília).
/// Particular: recebido = pagamento confirmado pela recepção. Convênio: autorizado/pendente/negado conforme a guia.
/// </summary>
public sealed class FinanceiroService
{
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public FinanceiroService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private sealed class Totais
    {
        public int PartQtd { get; set; }
        public decimal PartTotal { get; set; }
        public int PartPagoQtd { get; set; }
        public decimal PartPagoValor { get; set; }
        public int ConvQtd { get; set; }
        public decimal ConvTotal { get; set; }
        public int ConvAutQtd { get; set; }
        public decimal ConvAutValor { get; set; }
        public int ConvNegQtd { get; set; }
        public decimal ConvNegValor { get; set; }
    }

    private static void Validar(FiltroFinanceiro f)
    {
        var e = new List<ErroCampo>();
        if (f.DataInicial is null) e.Add(new ErroCampo("dataInicial", "Campo Não Preenchido"));
        if (f.DataFinal is null) e.Add(new ErroCampo("dataFinal", "Campo Não Preenchido"));
        if (f.DataInicial.HasValue && f.DataFinal.HasValue)
        {
            if (f.DataFinal < f.DataInicial) e.Add(new ErroCampo("dataFinal", "A data final não pode ser anterior à inicial"));
            else if (f.DataFinal.Value.DayNumber - f.DataInicial.Value.DayNumber > 366) e.Add(new ErroCampo("dataFinal", "O período deve ter no máximo 1 ano"));
        }
        if (e.Count > 0) throw Erro.Validacao(e);
    }

    private static DynamicParameters Parametros(FiltroFinanceiro f)
    {
        var p = new DynamicParameters();
        p.Add("De", DataBrasil.ParaUtc(f.DataInicial!.Value.ToDateTime(TimeOnly.MinValue)));
        p.Add("Ate", DataBrasil.ParaUtc(f.DataFinal!.Value.AddDays(1).ToDateTime(TimeOnly.MinValue)));
        p.Add("Unidade", f.UnidadeId);
        return p;
    }

    private const string Onde = @"
WHERE C.STATUS = 'CONCLUIDA' AND C.DATA_INICIO >= @De AND C.DATA_INICIO < @Ate
  AND (@Unidade IS NULL OR C.CODIGO_UNIDADE = @Unidade)";

    public async Task<ResumoFinanceiroDto> ResumoAsync(FiltroFinanceiro f, UsuarioAtual usuario, CancellationToken ct)
    {
        Validar(f);
        await using var con = await _db.AbrirAsync(ct);
        var t = await con.QueryFirstAsync<Totais>(Sql.Cmd(@"
SELECT
  CAST(ISNULL(SUM(CASE WHEN C.TIPO_ATENDIMENTO = 'PARTICULAR' THEN 1 ELSE 0 END), 0) AS INT) AS PartQtd,
  CAST(ISNULL(SUM(CASE WHEN C.TIPO_ATENDIMENTO = 'PARTICULAR' THEN C.VALOR ELSE 0 END), 0) AS DECIMAL(14,2)) AS PartTotal,
  CAST(ISNULL(SUM(CASE WHEN C.TIPO_ATENDIMENTO = 'PARTICULAR' AND C.PAGAMENTO_CONFIRMADO = 1 THEN 1 ELSE 0 END), 0) AS INT) AS PartPagoQtd,
  CAST(ISNULL(SUM(CASE WHEN C.TIPO_ATENDIMENTO = 'PARTICULAR' AND C.PAGAMENTO_CONFIRMADO = 1 THEN C.VALOR ELSE 0 END), 0) AS DECIMAL(14,2)) AS PartPagoValor,
  CAST(ISNULL(SUM(CASE WHEN C.TIPO_ATENDIMENTO = 'CONVENIO' THEN 1 ELSE 0 END), 0) AS INT) AS ConvQtd,
  CAST(ISNULL(SUM(CASE WHEN C.TIPO_ATENDIMENTO = 'CONVENIO' THEN C.VALOR ELSE 0 END), 0) AS DECIMAL(14,2)) AS ConvTotal,
  CAST(ISNULL(SUM(CASE WHEN C.TIPO_ATENDIMENTO = 'CONVENIO' AND C.GUIA_STATUS = 'AUTORIZADA' THEN 1 ELSE 0 END), 0) AS INT) AS ConvAutQtd,
  CAST(ISNULL(SUM(CASE WHEN C.TIPO_ATENDIMENTO = 'CONVENIO' AND C.GUIA_STATUS = 'AUTORIZADA' THEN C.VALOR ELSE 0 END), 0) AS DECIMAL(14,2)) AS ConvAutValor,
  CAST(ISNULL(SUM(CASE WHEN C.TIPO_ATENDIMENTO = 'CONVENIO' AND C.GUIA_STATUS = 'NEGADA' THEN 1 ELSE 0 END), 0) AS INT) AS ConvNegQtd,
  CAST(ISNULL(SUM(CASE WHEN C.TIPO_ATENDIMENTO = 'CONVENIO' AND C.GUIA_STATUS = 'NEGADA' THEN C.VALOR ELSE 0 END), 0) AS DECIMAL(14,2)) AS ConvNegValor
FROM DBO.CONSULTA C" + Onde + ";", Parametros(f), ct: ct));

        await _auditoria.RegistrarAsync("VISUALIZAR", "FATURAMENTO_FINANCEIRO", null, null, usuario.Id,
            new { de = f.DataInicial, ate = f.DataFinal, unidade = f.UnidadeId }, con, ct: ct);

        return new ResumoFinanceiroDto
        {
            DataInicial = f.DataInicial!.Value,
            DataFinal = f.DataFinal!.Value,
            UnidadeId = f.UnidadeId,
            Concluidas = t.PartQtd + t.ConvQtd,
            ValorTotal = t.PartTotal + t.ConvTotal,
            Particular = new ResumoParticularDto
            {
                Quantidade = t.PartQtd,
                Total = t.PartTotal,
                Recebido = new QuantidadeValorDto { Quantidade = t.PartPagoQtd, Valor = t.PartPagoValor },
                NaoConfirmado = new QuantidadeValorDto { Quantidade = t.PartQtd - t.PartPagoQtd, Valor = t.PartTotal - t.PartPagoValor }
            },
            Convenio = new ResumoConvenioDto
            {
                Quantidade = t.ConvQtd,
                Total = t.ConvTotal,
                Autorizado = new QuantidadeValorDto { Quantidade = t.ConvAutQtd, Valor = t.ConvAutValor },
                Negado = new QuantidadeValorDto { Quantidade = t.ConvNegQtd, Valor = t.ConvNegValor },
                Pendente = new QuantidadeValorDto { Quantidade = t.ConvQtd - t.ConvAutQtd - t.ConvNegQtd, Valor = t.ConvTotal - t.ConvAutValor - t.ConvNegValor }
            }
        };
    }

    private sealed class LinhaParticular
    {
        public Guid ConsultaId { get; set; }
        public DateTime Inicio { get; set; }
        public string StatusConsulta { get; set; } = string.Empty;
        public Guid PacienteId { get; set; }
        public string Paciente { get; set; } = string.Empty;
        public string Profissional { get; set; } = string.Empty;
        public string Especialidade { get; set; } = string.Empty;
        public string Unidade { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public string FormaPagamento { get; set; } = string.Empty;
        public bool PagamentoConfirmado { get; set; }
        public DateTime? PagamentoConfirmadoEm { get; set; }
        public string? PagamentoConfirmadoPor { get; set; }
    }

    /// <summary>Consultas particulares (sem canceladas, remarcadas e faltas) com a situação do pagamento, para o faturamento acompanhar.</summary>
    public async Task<ParticularPaginaDto> ListarParticularesAsync(FiltroParticulares f, CancellationToken ct)
    {
        var e = new List<ErroCampo>();
        if (f.Pagina < 1) e.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) e.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (!string.IsNullOrWhiteSpace(f.Pagamento) && f.Pagamento is not ("PENDENTE" or "CONFIRMADO")) e.Add(new ErroCampo("pagamento", "Informe PENDENTE ou CONFIRMADO"));
        if (f.DataInicial.HasValue && f.DataFinal.HasValue && f.DataFinal < f.DataInicial) e.Add(new ErroCampo("dataFinal", "A data final não pode ser anterior à inicial"));
        if (e.Count > 0) throw Erro.Validacao(e);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE C.TIPO_ATENDIMENTO = 'PARTICULAR' AND C.STATUS NOT IN ('CANCELADA', 'REMARCADA', 'FALTA')");
        if (f.Pagamento == "PENDENTE") onde.Append(" AND C.PAGAMENTO_CONFIRMADO = 0");
        else if (f.Pagamento == "CONFIRMADO") onde.Append(" AND C.PAGAMENTO_CONFIRMADO = 1");
        if (f.UnidadeId.HasValue) { onde.Append(" AND C.CODIGO_UNIDADE = @Unidade"); p.Add("Unidade", f.UnidadeId.Value); }
        if (f.DataInicial.HasValue) { onde.Append(" AND C.DATA_INICIO >= @De"); p.Add("De", DataBrasil.ParaUtc(f.DataInicial.Value.ToDateTime(TimeOnly.MinValue))); }
        if (f.DataFinal.HasValue) { onde.Append(" AND C.DATA_INICIO < @Ate"); p.Add("Ate", DataBrasil.ParaUtc(f.DataFinal.Value.AddDays(1).ToDateTime(TimeOnly.MinValue))); }
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            onde.Append(" AND P.NOME LIKE @Busca");
            p.Add("Busca", "%" + Formatos.EscaparLike(f.Busca.Trim()) + "%");
        }

        const string de = @"
FROM DBO.CONSULTA C
JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = C.CODIGO_PACIENTE
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = C.CODIGO_PROFISSIONAL
JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = C.CODIGO_ESPECIALIDADE
JOIN DBO.UNIDADE UN ON UN.CODIGO_UNIDADE = C.CODIGO_UNIDADE
JOIN DBO.FORMA_PAGAMENTO FP ON FP.CODIGO_PAGAMENTO = C.CODIGO_PAGAMENTO
LEFT JOIN DBO.USUARIO UP ON UP.CODIGO_USUARIO = C.PAGAMENTO_CONFIRMADO_USUARIO";

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1)" + de + onde, p, ct: ct));
        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<LinhaParticular>(Sql.Cmd(@"
SELECT C.CODIGO_PUBLIC AS ConsultaId, C.DATA_INICIO AS Inicio, C.STATUS AS StatusConsulta, P.CODIGO_PUBLIC AS PacienteId, P.NOME AS Paciente, PR.NOME AS Profissional,
       E.NOME AS Especialidade, UN.NOME_FANTASIA AS Unidade, C.VALOR AS Valor, FP.DESCRICAO AS FormaPagamento,
       C.PAGAMENTO_CONFIRMADO AS PagamentoConfirmado, C.DATA_PAGAMENTO AS PagamentoConfirmadoEm, UP.NOME_COMPLETO AS PagamentoConfirmadoPor"
            + de + onde + " ORDER BY C.DATA_INICIO DESC, C.CODIGO_CONSULTA DESC OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new ParticularPaginaDto
        {
            Pagina = f.Pagina,
            Tamanho = f.Tamanho,
            Total = total,
            Itens = linhas.Select(l => new ParticularItemDto
            {
                ConsultaId = l.ConsultaId,
                Inicio = DataBrasil.Utc(l.Inicio),
                StatusConsulta = l.StatusConsulta,
                PacienteId = l.PacienteId,
                Paciente = l.Paciente,
                Profissional = l.Profissional,
                Especialidade = l.Especialidade,
                Unidade = l.Unidade,
                Valor = l.Valor,
                FormaPagamento = l.FormaPagamento,
                PagamentoConfirmado = l.PagamentoConfirmado,
                PagamentoConfirmadoEm = DataBrasil.Utc(l.PagamentoConfirmadoEm),
                PagamentoConfirmadoPor = l.PagamentoConfirmadoPor
            }).ToList()
        };
    }

    private sealed class LinhaCsv
    {
        public DateTime Inicio { get; set; }
        public string Paciente { get; set; } = string.Empty;
        public string Profissional { get; set; } = string.Empty;
        public string Especialidade { get; set; } = string.Empty;
        public string Unidade { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public string? Convenio { get; set; }
        public decimal Valor { get; set; }
        public string? FormaPagamento { get; set; }
        public bool PagamentoConfirmado { get; set; }
        public string? GuiaStatus { get; set; }
        public string? GuiaNumero { get; set; }
    }

    private static string Campo(string? v)
    {
        var t = v ?? string.Empty;
        // Evita que o Excel trate o texto como fórmula.
        if (t.Length > 0 && (t[0] is '=' or '+' or '-' or '@')) t = "'" + t;
        return "\"" + t.Replace("\"", "\"\"") + "\"";
    }

    private static string Dinheiro(decimal v) => v.ToString("F2", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>CSV (separador ponto e vírgula, UTF-8 com BOM) para abrir no Excel.</summary>
    public async Task<byte[]> ExportarAsync(FiltroFinanceiro f, UsuarioAtual usuario, CancellationToken ct)
    {
        Validar(f);
        await using var con = await _db.AbrirAsync(ct);
        var linhas = (await con.QueryAsync<LinhaCsv>(Sql.Cmd(@"
SELECT C.DATA_INICIO AS Inicio, P.NOME AS Paciente, PR.NOME AS Profissional, E.NOME AS Especialidade, UN.NOME_FANTASIA AS Unidade,
       C.TIPO_ATENDIMENTO AS Tipo, COALESCE(CV.NOME_FANTASIA, CV.RAZAO_SOCIAL) AS Convenio, C.VALOR AS Valor, FP.DESCRICAO AS FormaPagamento,
       C.PAGAMENTO_CONFIRMADO AS PagamentoConfirmado, C.GUIA_STATUS AS GuiaStatus, C.GUIA_NUMERO AS GuiaNumero
FROM DBO.CONSULTA C
JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = C.CODIGO_PACIENTE
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = C.CODIGO_PROFISSIONAL
JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = C.CODIGO_ESPECIALIDADE
JOIN DBO.UNIDADE UN ON UN.CODIGO_UNIDADE = C.CODIGO_UNIDADE
JOIN DBO.FORMA_PAGAMENTO FP ON FP.CODIGO_PAGAMENTO = C.CODIGO_PAGAMENTO
LEFT JOIN DBO.CONVENIO CV ON CV.CODIGO_CONVENIO = C.CODIGO_CONVENIO" + Onde + " ORDER BY C.DATA_INICIO, C.CODIGO_CONSULTA;", Parametros(f), ct: ct))).ToList();

        var sb = new StringBuilder();
        sb.Append("Data;Hora;Paciente;Profissional;Especialidade;Unidade;Tipo;Convênio;Valor;Forma de pagamento;Pagamento confirmado;Guia;Número da guia\r\n");
        foreach (var l in linhas)
        {
            var local = l.Inicio.AddHours(-3); // Brasília = UTC-3 (sem horário de verão)
            var particular = l.Tipo == "PARTICULAR";
            sb.Append(local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)).Append(';')
              .Append(local.ToString("HH:mm", CultureInfo.InvariantCulture)).Append(';')
              .Append(Campo(l.Paciente)).Append(';').Append(Campo(l.Profissional)).Append(';').Append(Campo(l.Especialidade)).Append(';')
              .Append(Campo(l.Unidade)).Append(';').Append(particular ? "Particular" : "Convênio").Append(';').Append(Campo(l.Convenio)).Append(';')
              .Append(Dinheiro(l.Valor)).Append(';').Append(Campo(particular ? l.FormaPagamento : null)).Append(';')
              .Append(particular ? (l.PagamentoConfirmado ? "Sim" : "Não") : "").Append(';')
              .Append(particular ? "" : l.GuiaStatus switch { "AUTORIZADA" => "Autorizada", "NEGADA" => "Negada", "CANCELADA" => "Cancelada", _ => "Pendente" }).Append(';')
              .Append(Campo(l.GuiaNumero)).Append("\r\n");
        }

        await _auditoria.RegistrarAsync("EXPORTAR", "FATURAMENTO_FINANCEIRO", null, null, usuario.Id,
            new { de = f.DataInicial, ate = f.DataFinal, unidade = f.UnidadeId, linhas = linhas.Count }, con, ct: ct);

        var corpo = Encoding.UTF8.GetBytes(sb.ToString());
        return Encoding.UTF8.GetPreamble().Concat(corpo).ToArray();
    }
}
