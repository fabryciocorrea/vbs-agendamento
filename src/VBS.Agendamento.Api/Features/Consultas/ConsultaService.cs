using System.Data;
using System.Globalization;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Credenciamentos;
using VBS.Agendamento.Api.Features.Agenda;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Features.Pacotes;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Consultas;

/// <summary>
/// Consultas (1ª fatia): agendar, listar e consultar. A reserva da agenda e a conferência de sobreposição (RN09),
/// do limite de encaixes e do credenciamento (RN35) ficam na procedure USP_CONSULTA_CRIAR; aqui entram as regras de
/// cadastro (RN10, RN11, RN12) e o mapeamento dos erros do banco para os códigos do contrato.
/// </summary>
public sealed class ConsultaService
{
    private const string NaoPreenchido = "Campo Não Preenchido";

    private static readonly string[] StatusValidos =
        { "AGENDADA", "CONFIRMADA", "CHECK_IN", "EM_ATENDIMENTO", "CONCLUIDA", "CANCELADA", "FALTA", "REMARCADA" };

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;
    private readonly IPermissaoService _permissoes;

    public ConsultaService(IConexaoFactory db, IAuditoria auditoria, IPermissaoService permissoes)
    {
        _db = db;
        _auditoria = auditoria;
        _permissoes = permissoes;
    }

    // ------------------------------------------------------------- consultas
    private const string SelectConsulta = @"
SELECT C.CODIGO_CONSULTA AS Codigo, C.CODIGO_PUBLIC AS Id, C.VERSAO AS Versao, C.STATUS AS Status,
       P.CODIGO_PUBLIC AS PacienteId, P.NOME AS PacienteNome, P.CODIGO_PROFISSIONAL_RESPONSAVEL AS PacienteResponsavelId, P.DATA_VALIDADE_CARTEIRINHA AS PacienteValidadeCarteirinha,
       C.CODIGO_PROFISSIONAL AS ProfissionalId, PR.NOME AS ProfissionalNome,
       C.CODIGO_ESPECIALIDADE AS EspecialidadeId, E.NOME AS EspecialidadeNome,
       C.CODIGO_UNIDADE AS UnidadeId, UN.NOME_FANTASIA AS UnidadeNome,
       C.CODIGO_SALA AS SalaId, S.NUMERO AS SalaNumero, S.ANDAR AS SalaAndar,
       C.DATA_INICIO AS Inicio, C.DATA_FIM AS Fim, C.MODALIDADE AS Modalidade, C.LINK_ONLINE AS LinkOnline,
       C.TIPO_ATENDIMENTO AS TipoAtendimento, C.CODIGO_CONVENIO AS ConvenioId, COALESCE(CV.NOME_FANTASIA, CV.RAZAO_SOCIAL) AS ConvenioNome,
       C.NUMERO_CARTEIRINHA AS NumeroCarteirinha, C.VALOR AS Valor,
       C.CODIGO_PAGAMENTO AS FormaPagamentoId, FP.DESCRICAO AS FormaPagamentoNome,
       C.OBSERVACAO_CHECKIN AS Observacao, C.JUSTIFICATIVA_PROFISSIONAL AS JustificativaProfissional,
       C.CODIGO_SERIE AS SerieId,
       CASE WHEN C.CODIGO_SERIE IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM DBO.CONSULTA F WHERE F.CODIGO_SERIE = C.CODIGO_SERIE
            AND F.DATA_INICIO > C.DATA_INICIO AND F.STATUS IN ('AGENDADA', 'CONFIRMADA')) END AS SerieFuturas,
       PPK.CODIGO_PUBLIC AS PacotePacienteId, PPK.NOME AS PacoteNome, PPK.PAGAMENTO_CONFIRMADO AS PacotePagamentoConfirmado, PPK.VALOR AS PacoteValor, FPK.DESCRICAO AS PacoteFormaPagamentoNome,
       C.PAGAMENTO_CONFIRMADO AS PagamentoConfirmado, C.DATA_PAGAMENTO AS PagamentoConfirmadoEm,
       C.PAGAMENTO_CONFIRMADO_USUARIO AS PagamentoConfirmadoPorId, UP.NOME_COMPLETO AS PagamentoConfirmadoPorNome,
       ORIG.CODIGO_PUBLIC AS ConsultaOrigemId, C.ENCAIXE AS Encaixe,
       C.CREDENCIAMENTO_LIBERADO_POR AS LiberadoPorId, UL.NOME_COMPLETO AS LiberadoPorNome, C.CREDENCIAMENTO_JUSTIFICATIVA AS LiberacaoJustificativa,
       C.GUIA_STATUS AS GuiaStatus, C.GUIA_NUMERO AS GuiaNumero, C.GUIA_OBSERVACAO AS GuiaObservacao, C.GUIA_DATA AS GuiaEm,
       C.GUIA_USUARIO AS GuiaPorId, UG.NOME_COMPLETO AS GuiaPorNome,
       C.LIBERACAO_ADMIN_USUARIO AS LibAdminId, UA.NOME_COMPLETO AS LibAdminNome, C.LIBERACAO_ADMIN_JUSTIFICATIVA AS LibAdminJustificativa, C.LIBERACAO_ADMIN_DATA AS LibAdminEm,
       C.DATA_CRIACAO AS CriadoEm, C.CRIACAO_USUARIO AS CriadoPorId, UC.NOME_COMPLETO AS CriadoPorNome
FROM DBO.CONSULTA C
JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = C.CODIGO_PACIENTE
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = C.CODIGO_PROFISSIONAL
JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = C.CODIGO_ESPECIALIDADE
JOIN DBO.UNIDADE UN ON UN.CODIGO_UNIDADE = C.CODIGO_UNIDADE
JOIN DBO.FORMA_PAGAMENTO FP ON FP.CODIGO_PAGAMENTO = C.CODIGO_PAGAMENTO
LEFT JOIN DBO.SALA S ON S.CODIGO_SALA = C.CODIGO_SALA
LEFT JOIN DBO.CONVENIO CV ON CV.CODIGO_CONVENIO = C.CODIGO_CONVENIO
LEFT JOIN DBO.CONSULTA ORIG ON ORIG.CODIGO_CONSULTA = C.CODIGO_CONSULTA_ORIGEM
LEFT JOIN DBO.PACOTE_PACIENTE PPK ON PPK.CODIGO_PACOTE_PACIENTE = C.CODIGO_PACOTE_PACIENTE
LEFT JOIN DBO.FORMA_PAGAMENTO FPK ON FPK.CODIGO_PAGAMENTO = PPK.CODIGO_PAGAMENTO
LEFT JOIN DBO.USUARIO UP ON UP.CODIGO_USUARIO = C.PAGAMENTO_CONFIRMADO_USUARIO
LEFT JOIN DBO.USUARIO UL ON UL.CODIGO_USUARIO = C.CREDENCIAMENTO_LIBERADO_POR
LEFT JOIN DBO.USUARIO UC ON UC.CODIGO_USUARIO = C.CRIACAO_USUARIO
LEFT JOIN DBO.USUARIO UG ON UG.CODIGO_USUARIO = C.GUIA_USUARIO
LEFT JOIN DBO.USUARIO UA ON UA.CODIGO_USUARIO = C.LIBERACAO_ADMIN_USUARIO";

    private sealed class ConsultaLinha
    {
        public int Codigo { get; set; }
        public Guid Id { get; set; }
        public byte[] Versao { get; set; } = Array.Empty<byte>();
        public string Status { get; set; } = string.Empty;
        public Guid PacienteId { get; set; }
        public string PacienteNome { get; set; } = string.Empty;
        public int PacienteResponsavelId { get; set; }
        public DateTime? PacienteValidadeCarteirinha { get; set; }
        public int ProfissionalId { get; set; }
        public string ProfissionalNome { get; set; } = string.Empty;
        public int EspecialidadeId { get; set; }
        public string EspecialidadeNome { get; set; } = string.Empty;
        public int UnidadeId { get; set; }
        public string UnidadeNome { get; set; } = string.Empty;
        public int? SalaId { get; set; }
        public string? SalaNumero { get; set; }
        public string? SalaAndar { get; set; }
        public DateTime Inicio { get; set; }
        public DateTime Fim { get; set; }
        public string Modalidade { get; set; } = string.Empty;
        public string? LinkOnline { get; set; }
        public string TipoAtendimento { get; set; } = string.Empty;
        public int? ConvenioId { get; set; }
        public string? ConvenioNome { get; set; }
        public string? NumeroCarteirinha { get; set; }
        public decimal Valor { get; set; }
        public int FormaPagamentoId { get; set; }
        public string FormaPagamentoNome { get; set; } = string.Empty;
        public string? Observacao { get; set; }
        public string? JustificativaProfissional { get; set; }
        public Guid? SerieId { get; set; }
        public int SerieFuturas { get; set; }
        public Guid? PacotePacienteId { get; set; }
        public string? PacoteNome { get; set; }
        public bool? PacotePagamentoConfirmado { get; set; }
        public decimal? PacoteValor { get; set; }
        public string? PacoteFormaPagamentoNome { get; set; }
        public bool PagamentoConfirmado { get; set; }
        public DateTime? PagamentoConfirmadoEm { get; set; }
        public int? PagamentoConfirmadoPorId { get; set; }
        public string? PagamentoConfirmadoPorNome { get; set; }
        public Guid? ConsultaOrigemId { get; set; }
        public bool Encaixe { get; set; }
        public int? LiberadoPorId { get; set; }
        public string? LiberadoPorNome { get; set; }
        public string? LiberacaoJustificativa { get; set; }
        public int? LibAdminId { get; set; }
        public string? LibAdminNome { get; set; }
        public string? LibAdminJustificativa { get; set; }
        public DateTime? LibAdminEm { get; set; }
        public string? GuiaStatus { get; set; }
        public string? GuiaNumero { get; set; }
        public string? GuiaObservacao { get; set; }
        public DateTime? GuiaEm { get; set; }
        public int? GuiaPorId { get; set; }
        public string? GuiaPorNome { get; set; }
        public DateTime CriadoEm { get; set; }
        public int? CriadoPorId { get; set; }
        public string? CriadoPorNome { get; set; }
    }

    private static void Preencher(ConsultaResumoDto d, ConsultaLinha l, bool ocultarValor)
    {
        d.Id = l.Id;
        d.Versao = Convert.ToBase64String(l.Versao);
        d.Status = l.Status;
        d.Paciente = new PacienteReferenciaDto { Id = l.PacienteId, Nome = l.PacienteNome };
        d.PacienteResponsavelId = l.PacienteResponsavelId;
        d.Profissional = new ReferenciaDto { Id = l.ProfissionalId, Nome = l.ProfissionalNome };
        d.Especialidade = new ReferenciaDto { Id = l.EspecialidadeId, Nome = l.EspecialidadeNome };
        d.Unidade = new ReferenciaDto { Id = l.UnidadeId, Nome = l.UnidadeNome };
        d.Sala = l.SalaId.HasValue ? new SalaReferenciaDto { Id = l.SalaId.Value, Numero = l.SalaNumero ?? string.Empty, Andar = l.SalaAndar ?? string.Empty } : null;
        d.Inicio = DataBrasil.Utc(l.Inicio);
        d.Fim = DataBrasil.Utc(l.Fim);
        d.Modalidade = l.Modalidade;
        d.TipoAtendimento = l.TipoAtendimento;
        d.Convenio = l.ConvenioId.HasValue ? new ReferenciaDto { Id = l.ConvenioId.Value, Nome = l.ConvenioNome ?? string.Empty } : null;
        d.Valor = ocultarValor ? null : l.Valor;
        d.Encaixe = l.Encaixe;
        d.Remarcada = l.ConsultaOrigemId.HasValue;
        d.PendenteFaturamento = l.Status == "CONCLUIDA" && l.TipoAtendimento == "CONVENIO" && (l.GuiaStatus ?? "PENDENTE") == "PENDENTE";
    }

    private static ConsultaResumoDto ParaResumo(ConsultaLinha l, bool ocultarValor)
    {
        var d = new ConsultaResumoDto();
        Preencher(d, l, ocultarValor);
        return d;
    }

    private static ConsultaDto ParaConsulta(ConsultaLinha l, bool ocultarValor)
    {
        var d = new ConsultaDto();
        Preencher(d, l, ocultarValor);
        d.LinkOnline = l.LinkOnline;
        d.NumeroCarteirinha = l.NumeroCarteirinha;
        d.CarteirinhaVencida = l.TipoAtendimento == "CONVENIO" && l.PacienteValidadeCarteirinha.HasValue &&
                               DateOnly.FromDateTime(l.PacienteValidadeCarteirinha.Value) < DataBrasil.Hoje();
        d.FormaPagamento = new ReferenciaDto { Id = l.FormaPagamentoId, Nome = l.FormaPagamentoNome };
        d.Observacao = l.Observacao;
        d.JustificativaProfissional = l.JustificativaProfissional;
        d.SerieId = l.SerieId;
        d.SerieFuturas = l.SerieFuturas;
        d.PacotePacienteId = l.PacotePacienteId;
        d.PacoteNome = l.PacoteNome;
        d.PacotePagamentoConfirmado = l.PacotePagamentoConfirmado;
        d.PacoteValor = l.PacoteValor;
        d.PacoteFormaPagamentoNome = l.PacoteFormaPagamentoNome;
        d.PagamentoConfirmado = l.PagamentoConfirmado;
        d.PagamentoConfirmadoEm = DataBrasil.Utc(l.PagamentoConfirmadoEm);
        d.PagamentoConfirmadoPor = l.PagamentoConfirmadoPorId.HasValue
            ? new UsuarioReferenciaDto { Id = l.PagamentoConfirmadoPorId.Value, NomeCompleto = l.PagamentoConfirmadoPorNome ?? string.Empty } : null;
        d.ConsultaOrigemId = l.ConsultaOrigemId;
        d.CredenciamentoLiberado = l.LiberadoPorId.HasValue
            ? new CredenciamentoLiberadoDto
            {
                LiberadoPor = new UsuarioReferenciaDto { Id = l.LiberadoPorId.Value, NomeCompleto = l.LiberadoPorNome ?? string.Empty },
                Justificativa = l.LiberacaoJustificativa ?? string.Empty
            } : null;
        d.LiberacaoAdministrador = l.LibAdminId.HasValue
            ? new LiberacaoAdministradorDto
            {
                LiberadoPor = new UsuarioReferenciaDto { Id = l.LibAdminId.Value, NomeCompleto = l.LibAdminNome ?? string.Empty },
                Justificativa = l.LibAdminJustificativa ?? string.Empty,
                Em = DataBrasil.Utc(l.LibAdminEm ?? DateTime.UtcNow)
            }
            : null;
        d.Guia = l.TipoAtendimento == "CONVENIO"
            ? new GuiaConvenioDto
            {
                Status = l.GuiaStatus ?? "PENDENTE",
                Numero = l.GuiaNumero,
                Observacao = l.GuiaObservacao,
                AtualizadoEm = l.GuiaEm.HasValue ? DataBrasil.Utc(l.GuiaEm.Value) : null,
                AtualizadoPor = l.GuiaPorId.HasValue
                    ? new UsuarioReferenciaDto { Id = l.GuiaPorId.Value, NomeCompleto = l.GuiaPorNome ?? string.Empty } : null
            } : null;
        d.CriadoEm = DataBrasil.Utc(l.CriadoEm);
        d.CriadoPor = l.CriadoPorId.HasValue
            ? new UsuarioReferenciaDto { Id = l.CriadoPorId.Value, NomeCompleto = l.CriadoPorNome ?? string.Empty } : null;
        return d;
    }

    // ----------------------------------------- consultas usadas pela agenda
    internal readonly record struct Ocupado(DateTime Inicio, DateTime Fim);

    private sealed class OcupadoLinha
    {
        public DateTime Inicio { get; set; }
        public DateTime Fim { get; set; }
    }

    /// <summary>
    /// Condição do médico sobre o paciente da consulta (RN16): só aparecem as consultas de pacientes de que ele é o responsável atual.
    /// Paciente transferido para outro profissional, ou que nunca foi dele, fica fora da agenda, do início e das listas do médico.
    /// </summary>
    private static string FiltroPacientesTransferidos(string parametro) =>
        $" AND C.CODIGO_PACIENTE IN (SELECT CODIGO_PACIENTE FROM DBO.PACIENTE WHERE CODIGO_PROFISSIONAL_RESPONSAVEL = {parametro})";

    /// <summary>Intervalos já ocupados do profissional (canceladas e remarcadas não contam, RN09).</summary>
    internal async Task<List<Ocupado>> PeriodosOcupadosAsync(SqlConnection con, int profissionalId, DateTime deUtc, DateTime ateUtc, CancellationToken ct)
    {
        var linhas = await con.QueryAsync<OcupadoLinha>(Sql.Cmd(@"
SELECT DATA_INICIO AS Inicio, DATA_FIM AS Fim FROM DBO.CONSULTA
WHERE CODIGO_PROFISSIONAL = @Prof AND STATUS NOT IN ('CANCELADA', 'REMARCADA')
  AND DATA_INICIO < @Ate AND DATA_FIM > @De;", new { Prof = profissionalId, De = deUtc, Ate = ateUtc }, ct: ct));
        return linhas.Select(l => new Ocupado(DataBrasil.Utc(l.Inicio), DataBrasil.Utc(l.Fim))).ToList();
    }

    internal async Task<List<ConsultaResumoDto>> ListarPorPeriodoAsync(SqlConnection con, int? profissionalId, int? unidadeId,
        DateTime deUtc, DateTime ateUtc, bool incluirCanceladas, bool ocultarValor, int? responsavelId, CancellationToken ct)
    {
        var sql = SelectConsulta + @"
WHERE (@Prof IS NULL OR C.CODIGO_PROFISSIONAL = @Prof) AND C.DATA_INICIO < @Ate AND C.DATA_FIM > @De
  AND (@Unidade IS NULL OR C.CODIGO_UNIDADE = @Unidade)" +
            // Para o médico, só consultas de pacientes de que ele é o responsável atual.
            (responsavelId.HasValue ? FiltroPacientesTransferidos("@Responsavel") : "") +
            (incluirCanceladas ? "" : " AND C.STATUS NOT IN ('CANCELADA', 'REMARCADA')") + " ORDER BY C.DATA_INICIO, C.CODIGO_CONSULTA;";
        var linhas = await con.QueryAsync<ConsultaLinha>(Sql.Cmd(sql, new { Prof = profissionalId, De = deUtc, Ate = ateUtc, Unidade = unidadeId, Responsavel = responsavelId }, ct: ct));
        return linhas.Select(l => ParaResumo(l, ocultarValor)).ToList();
    }

    // ----------------------------------------------------------------- lista
    /// <summary>
    /// Consultas futuras (AGENDADA ou CONFIRMADA) por convênio cujo credenciamento deixou de valer na unidade e especialidade (J4).
    /// Usa a mesma regra do banco (FN_CREDENCIADO, com a data no horário de Brasília) e deixa de fora as liberadas pelo administrador.
    /// Nada é cancelado aqui: a recepção contata o paciente. O médico recebe só as próprias consultas.
    /// </summary>
    public async Task<ConsultaPaginaDto> ListarSemCredenciamentoAsync(FiltroSemCredenciamento f, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(@"
 WHERE C.TIPO_ATENDIMENTO = 'CONVENIO' AND C.STATUS IN ('AGENDADA', 'CONFIRMADA') AND C.DATA_INICIO >= SYSUTCDATETIME()
   AND C.CREDENCIAMENTO_LIBERADO_POR IS NULL
   AND DBO.FN_CREDENCIADO(C.CODIGO_CONVENIO, C.CODIGO_UNIDADE, C.CODIGO_ESPECIALIDADE, CONVERT(DATE, DATEADD(HOUR, -3, C.DATA_INICIO))) = 0");
        if (usuario.EhMedico)
        {
            onde.Append(" AND C.CODIGO_PROFISSIONAL = @Medico").Append(FiltroPacientesTransferidos("@Medico"));
            p.Add("Medico", usuario.ProfissionalId!.Value);
        }
        if (f.UnidadeId.HasValue) { onde.Append(" AND C.CODIGO_UNIDADE = @UnidadeId"); p.Add("UnidadeId", f.UnidadeId.Value); }
        if (f.ConvenioId.HasValue) { onde.Append(" AND C.CODIGO_CONVENIO = @ConvenioId"); p.Add("ConvenioId", f.ConvenioId.Value); }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.CONSULTA C" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<ConsultaLinha>(Sql.Cmd(
            SelectConsulta + onde + " ORDER BY C.DATA_INICIO, C.CODIGO_CONSULTA OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new ConsultaPaginaDto
        {
            Itens = linhas.Select(l => ParaResumo(l, usuario.EhMedico)).ToList(),
            Pagina = f.Pagina,
            Tamanho = f.Tamanho,
            Total = total
        };
    }

    public async Task<ConsultaPaginaDto> ListarAsync(FiltroConsultas f, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (f.DataInicial.HasValue && f.DataFinal.HasValue && f.DataFinal < f.DataInicial)
            erros.Add(new ErroCampo("dataFinal", "A data final não pode ser anterior à inicial"));
        if (!string.IsNullOrWhiteSpace(f.Status) && !StatusValidos.Contains(f.Status))
            erros.Add(new ErroCampo("status", "Status inválido"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");

        if (usuario.EhMedico)
        {
            // O médico vê só as consultas dos pacientes de que é o responsável atual.
            onde.Append(" AND C.CODIGO_PROFISSIONAL = @Medico").Append(FiltroPacientesTransferidos("@Medico"));
            p.Add("Medico", usuario.ProfissionalId!.Value);
        }
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
        if (f.PacienteId.HasValue) { onde.Append(" AND P.CODIGO_PUBLIC = @PacienteId"); p.Add("PacienteId", f.PacienteId.Value); }
        if (f.ProfissionalId.HasValue) { onde.Append(" AND C.CODIGO_PROFISSIONAL = @ProfissionalId"); p.Add("ProfissionalId", f.ProfissionalId.Value); }
        if (f.UnidadeId.HasValue) { onde.Append(" AND C.CODIGO_UNIDADE = @UnidadeId"); p.Add("UnidadeId", f.UnidadeId.Value); }
        if (!string.IsNullOrWhiteSpace(f.Status)) { onde.Append(" AND C.STATUS = @Status"); p.Add("Status", f.Status); }
        if (f.Encaixe.HasValue) { onde.Append(" AND C.ENCAIXE = @Encaixe"); p.Add("Encaixe", f.Encaixe.Value); }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.CONSULTA C JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = C.CODIGO_PACIENTE" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<ConsultaLinha>(Sql.Cmd(
            SelectConsulta + onde + " ORDER BY C.DATA_INICIO, C.CODIGO_CONSULTA OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new ConsultaPaginaDto
        {
            Itens = linhas.Select(l => ParaResumo(l, usuario.EhMedico)).ToList(),
            Pagina = f.Pagina,
            Tamanho = f.Tamanho,
            Total = total
        };
    }

    // ------------------------------------------------------------- consulta
    public async Task<ConsultaDto> ObterAsync(Guid id, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var sql = SelectConsulta + " WHERE C.CODIGO_PUBLIC = @Id" +
                  (usuario.EhMedico ? " AND C.CODIGO_PROFISSIONAL = @Medico" + FiltroPacientesTransferidos("@Medico") : "") + ";";
        var linha = await con.QueryFirstOrDefaultAsync<ConsultaLinha>(Sql.Cmd(sql, new { Id = id, Medico = usuario.ProfissionalId }, ct: ct))
            ?? throw Erro.NaoEncontrado("Consulta não encontrada.");

        await _auditoria.RegistrarAsync("VISUALIZAR", "CONSULTA", linha.Codigo, null, usuario.Id, null, con, ct: ct);
        return ParaConsulta(linha, usuario.EhMedico);
    }

    // ---------------------------------------------------------------- agendar
    private sealed class PacienteLinha
    {
        public int Codigo { get; set; }
        public bool Ativo { get; set; }
        public int? ConvenioId { get; set; }
        public string? NumeroCarteirinha { get; set; }
        public DateTime? ValidadeCarteirinha { get; set; }
        public int Responsavel { get; set; }
    }

    private sealed class SituacaoLinha
    {
        public bool Ativo { get; set; }
        public string Nome { get; set; } = string.Empty;
    }

    private sealed class FormaLinha
    {
        public bool Ativo { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public int? ConvenioId { get; set; }
    }

    private sealed class SalaLinha
    {
        public bool Ativo { get; set; }
        public int UnidadeId { get; set; }
    }

    private sealed record Dados(Guid PacienteId, int ProfissionalId, int EspecialidadeId, int UnidadeId, int? SalaId, string Modalidade,
        string Tipo, int? ConvenioId, decimal Valor, int? FormaId, DateTime Inicio, DateTime? Fim, bool Encaixe, bool PagamentoConfirmado,
        string? JustificativaLiberacao, Guid? PacotePacienteId, VendaPacoteEntrada? VendaPacote, string? JustificativaProfissional);

    private static Dados Validar(ConsultaEntrada? d)
    {
        if (d is null) throw Erro.Validacao("corpo", NaoPreenchido);
        var e = new List<ErroCampo>();

        if (d.PacienteId is null || d.PacienteId == Guid.Empty) e.Add(new ErroCampo("pacienteId", NaoPreenchido));
        if (d.ProfissionalId is null) e.Add(new ErroCampo("profissionalId", NaoPreenchido));
        if (d.EspecialidadeId is null) e.Add(new ErroCampo("especialidadeId", NaoPreenchido));
        if (d.UnidadeId is null) e.Add(new ErroCampo("unidadeId", NaoPreenchido));

        if (string.IsNullOrWhiteSpace(d.Modalidade)) e.Add(new ErroCampo("modalidade", NaoPreenchido));
        else if (d.Modalidade is not ("PRESENCIAL" or "ONLINE")) e.Add(new ErroCampo("modalidade", "Informe PRESENCIAL ou ONLINE"));

        if (string.IsNullOrWhiteSpace(d.TipoAtendimento)) e.Add(new ErroCampo("tipoAtendimento", NaoPreenchido));
        else if (d.TipoAtendimento is not ("PARTICULAR" or "CONVENIO")) e.Add(new ErroCampo("tipoAtendimento", "Informe PARTICULAR ou CONVENIO"));

        if (d.Modalidade == "PRESENCIAL" && d.SalaId is null) e.Add(new ErroCampo("salaId", NaoPreenchido));
        if (d.TipoAtendimento == "CONVENIO" && d.ConvenioId is null) e.Add(new ErroCampo("convenioId", NaoPreenchido));

        // Pacote (só particular): o valor da sessão e a forma de pagamento vêm do pacote, vendido antes ou na hora.
        var usaPacote = d.PacotePacienteId.HasValue || d.VendaPacote is not null;
        if (usaPacote && d.TipoAtendimento == "CONVENIO") e.Add(new ErroCampo("pacotePacienteId", "O pacote só vale em atendimento particular"));
        if (d.PacotePacienteId.HasValue && d.PacotePacienteId == Guid.Empty) e.Add(new ErroCampo("pacotePacienteId", "Pacote inválido"));

        // Em convênio o valor vem do cadastro do convênio (a tela não o envia); o valor informado só vale no atendimento particular.
        if (d.TipoAtendimento == "CONVENIO") { }
        else if (usaPacote) { }
        else if (d.Valor is null) e.Add(new ErroCampo("valor", NaoPreenchido));
        else if (d.Valor < 0) e.Add(new ErroCampo("valor", "Não pode ser negativo"));
        else if (d.Valor >= 100_000_000m) e.Add(new ErroCampo("valor", "Valor acima do permitido"));
        else if (decimal.Round(d.Valor.Value, 2) != d.Valor) e.Add(new ErroCampo("valor", "Use no máximo duas casas decimais"));

        // No convênio a forma é a do próprio convênio: se a tela não enviar, o servidor localiza (mais simples para a tela).
        if (d.FormaPagamentoId is null && d.TipoAtendimento != "CONVENIO" && !usaPacote) e.Add(new ErroCampo("formaPagamentoId", NaoPreenchido));

        DateTime? inicio = null, fim = null;
        if (d.Inicio is null) e.Add(new ErroCampo("inicio", NaoPreenchido));
        else inicio = Minuto(d.Inicio.Value);
        if (d.Fim is not null)
        {
            fim = Minuto(d.Fim.Value);
            if (inicio is not null && fim <= inicio) e.Add(new ErroCampo("fim", "O término deve ser depois do início"));
            else if (inicio is not null && (fim.Value - inicio.Value).TotalMinutes > 240) e.Add(new ErroCampo("fim", "A consulta pode ter no máximo 240 minutos"));
        }

        string? justificativa = null;
        if (d.LiberacaoCredenciamento is not null)
        {
            justificativa = d.LiberacaoCredenciamento.Justificativa?.Trim();
            if (string.IsNullOrEmpty(justificativa)) e.Add(new ErroCampo("liberacaoCredenciamento.justificativa", NaoPreenchido));
            else if (justificativa.Length < 10) e.Add(new ErroCampo("liberacaoCredenciamento.justificativa", "Informe ao menos 10 caracteres"));
            else if (justificativa.Length > 500) e.Add(new ErroCampo("liberacaoCredenciamento.justificativa", "No máximo 500 caracteres"));
        }

        if (e.Count > 0) throw Erro.Validacao(e);

        return new Dados(d.PacienteId!.Value, d.ProfissionalId!.Value, d.EspecialidadeId!.Value, d.UnidadeId!.Value,
            d.Modalidade == "PRESENCIAL" ? d.SalaId : null, d.Modalidade!, d.TipoAtendimento!,
            d.TipoAtendimento == "CONVENIO" ? d.ConvenioId : null, d.TipoAtendimento == "CONVENIO" ? 0m : d.Valor ?? 0m, d.FormaPagamentoId,
            inicio!.Value, fim, d.Encaixe, d.PagamentoConfirmado, justificativa, d.PacotePacienteId, d.VendaPacote, d.JustificativaProfissional);
    }

    /// <summary>UTC, sem segundos (as colunas são DATETIME2(0) e a regra trabalha em minutos).</summary>
    private static DateTime Minuto(DateTime valor)
    {
        var utc = valor.Kind == DateTimeKind.Local ? valor.ToUniversalTime() : DateTime.SpecifyKind(valor, DateTimeKind.Utc);
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
    }

    public async Task<ConsultaDto> AgendarAsync(ConsultaEntrada? entrada, UsuarioAtual usuario, CancellationToken ct)
    {
        var d = Validar(entrada);
        var liberacaoAdmin = LerLiberacaoAdmin(entrada?.LiberacaoAdministrador, usuario);

        // Encaixe (G2) e liberação de credenciamento (RN35) são ações separadas da inclusão comum.
        if (d.Encaixe && !await _permissoes.TemAsync(usuario.PerfilId, usuario.Administrador, "consulta.agendamento:editar", ct))
            throw Erro.SemPermissao("O seu perfil não tem liberação para incluir encaixe.", "ENCAIXE_SEM_PERMISSAO");
        if (d.JustificativaLiberacao is not null && !usuario.Administrador)
            throw Erro.SemPermissao("Somente o administrador libera consulta por convênio sem credenciamento.", "LIBERACAO_CREDENCIAMENTO_NEGADA");

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        // ---- paciente (RN16: para o médico, paciente de outro profissional "não existe")
        var paciente = await con.QueryFirstOrDefaultAsync<PacienteLinha>(Sql.Cmd(@"
SELECT CODIGO_PACIENTE AS Codigo, ATIVO AS Ativo, CODIGO_CONVENIO AS ConvenioId, NUMERO_CARTEIRINHA AS NumeroCarteirinha,
       DATA_VALIDADE_CARTEIRINHA AS ValidadeCarteirinha, CODIGO_PROFISSIONAL_RESPONSAVEL AS Responsavel
FROM DBO.PACIENTE WHERE CODIGO_PUBLIC = @Id" + (usuario.EhMedico ? " AND CODIGO_PROFISSIONAL_RESPONSAVEL = @Medico" : "") + ";",
            new { Id = d.PacienteId, Medico = usuario.ProfissionalId }, tx, ct))
            ?? throw Erro.NaoEncontrado("Paciente não encontrado.");
        if (!paciente.Ativo) throw Erro.Validacao("pacienteId", "O paciente está inativo");

        // ---- profissional, especialidade, unidade (RN11)
        var prof = await con.QueryFirstOrDefaultAsync<SituacaoLinha>(Sql.Cmd(
            "SELECT ATIVO AS Ativo, NOME AS Nome FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id;", new { Id = d.ProfissionalId }, tx, ct))
            ?? throw Erro.Validacao("profissionalId", "Profissional não encontrado");
        if (!prof.Ativo) throw Erro.Validacao("profissionalId", "O profissional está inativo");
        // Profissional que não é o responsável do paciente: agenda só com justificativa (10 a 500 caracteres).
        string? justificativaProfissional = null;
        if (paciente.Responsavel != d.ProfissionalId)
        {
            justificativaProfissional = d.JustificativaProfissional?.Trim();
            if (string.IsNullOrEmpty(justificativaProfissional))
                throw Erro.Regra("PROFISSIONAL_NAO_RESPONSAVEL",
                    $"O profissional {prof.Nome} não é o profissional responsável por este paciente. Informe uma justificativa para agendar mesmo assim.");
            if (justificativaProfissional.Length is < 10 or > 500)
                throw Erro.Validacao("justificativaProfissional", "Informe de 10 a 500 caracteres");
        }

        var esp = await con.QueryFirstOrDefaultAsync<SituacaoLinha>(Sql.Cmd(
            "SELECT ATIVO AS Ativo, NOME AS Nome FROM DBO.ESPECIALIDADE WHERE CODIGO_ESPECIALIDADE = @Id;", new { Id = d.EspecialidadeId }, tx, ct))
            ?? throw Erro.Validacao("especialidadeId", "Especialidade não encontrada");
        if (!esp.Ativo) throw Erro.Validacao("especialidadeId", "A especialidade está inativa");

        var uni = await con.QueryFirstOrDefaultAsync<SituacaoLinha>(Sql.Cmd(
            "SELECT ATIVO AS Ativo, NOME_FANTASIA AS Nome FROM DBO.UNIDADE WHERE CODIGO_UNIDADE = @Id;", new { Id = d.UnidadeId }, tx, ct))
            ?? throw Erro.Validacao("unidadeId", "Unidade não encontrada");
        if (!uni.Ativo) throw Erro.Validacao("unidadeId", "A unidade está inativa");

        var atende = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT CASE WHEN EXISTS (SELECT 1 FROM DBO.PROFISSIONAL_ESPECIALIDADE WHERE CODIGO_PROFISSIONAL = @P AND CODIGO_ESPECIALIDADE = @E)
             AND EXISTS (SELECT 1 FROM DBO.PROFISSIONAL_UNIDADE WHERE CODIGO_PROFISSIONAL = @P AND CODIGO_UNIDADE = @U) THEN 1 ELSE 0 END;",
            new { P = d.ProfissionalId, E = d.EspecialidadeId, U = d.UnidadeId }, tx, ct));
        if (atende == 0)
            throw Erro.Regra("PROFISSIONAL_NAO_ATENDE", $"{prof.Nome} não atende {esp.Nome} na unidade {uni.Nome}.");

        // ---- sala (presencial)
        if (d.SalaId.HasValue)
        {
            var sala = await con.QueryFirstOrDefaultAsync<SalaLinha>(Sql.Cmd(
                "SELECT ATIVO AS Ativo, CODIGO_UNIDADE AS UnidadeId FROM DBO.SALA WHERE CODIGO_SALA = @Id;", new { Id = d.SalaId }, tx, ct))
                ?? throw Erro.Validacao("salaId", "Sala não encontrada");
            if (sala.UnidadeId != d.UnidadeId) throw Erro.Validacao("salaId", "A sala não é da unidade escolhida");
            if (!sala.Ativo) throw Erro.Validacao("salaId", "A sala está inativa");
        }

        // ---- pacote (consulta particular que consome uma sessão de pacote já vendido, ou venda de um pacote novo na hora)
        UsoPacote? pacote = null;
        if (d.PacotePacienteId.HasValue || d.VendaPacote is not null)
        {
            if (d.Tipo != "PARTICULAR") throw Erro.Validacao("pacotePacienteId", "O pacote só vale em atendimento particular");
            var vende = d.VendaPacote is not null;
            if (!await _permissoes.TemAsync(usuario.PerfilId, usuario.Administrador, vende ? "pacote.venda:incluir" : "pacote.venda:visualizar", ct))
                throw Erro.SemPermissao(vende ? "O seu perfil não tem liberação para vender pacotes." : "O seu perfil não tem liberação para usar pacotes.", "PACOTE_SEM_PERMISSAO");
            pacote = await PacotePacienteService.PrepararUsoAsync(con, tx, _auditoria, paciente.Codigo, d.EspecialidadeId, d.Inicio,
                d.PacotePacienteId, d.VendaPacote, usuario, ct);
        }

        // ---- forma de pagamento
        var formaId = pacote?.FormaPagamentoId ?? d.FormaId;
        if (formaId is null)
        {
            formaId = await con.ExecuteScalarAsync<int?>(Sql.Cmd(
                "SELECT TOP 1 CODIGO_PAGAMENTO FROM DBO.FORMA_PAGAMENTO WHERE TIPO = 'CONVENIO' AND CODIGO_CONVENIO = @C AND ATIVO = 1;",
                new { C = d.ConvenioId }, tx, ct));
            if (formaId is null)
                throw Erro.Validacao("formaPagamentoId", "O convênio não tem forma de pagamento ativa. Confira o cadastro do convênio");
        }
        var forma = await con.QueryFirstOrDefaultAsync<FormaLinha>(Sql.Cmd(
            "SELECT ATIVO AS Ativo, TIPO AS Tipo, CODIGO_CONVENIO AS ConvenioId FROM DBO.FORMA_PAGAMENTO WHERE CODIGO_PAGAMENTO = @Id;",
            new { Id = formaId }, tx, ct))
            ?? throw Erro.Validacao("formaPagamentoId", "Forma de pagamento não encontrada");
        if (!forma.Ativo) throw Erro.Validacao("formaPagamentoId", "A forma de pagamento está inativa");

        // ---- tipo de atendimento e convênio (RN02, RN12, G3)
        string? numeroCarteirinha = null;
        string? convenioNome = null;
        var valorConsulta = pacote?.ValorSessao ?? d.Valor;
        if (d.Tipo == "CONVENIO")
        {
            // Qualquer perfil pode escolher outro convênio; o credenciamento ativo continua exigido (RN35).

            var conv = await con.QueryFirstOrDefaultAsync<SituacaoLinha>(Sql.Cmd(
                "SELECT ATIVO AS Ativo, COALESCE(NOME_FANTASIA, RAZAO_SOCIAL) AS Nome FROM DBO.CONVENIO WHERE CODIGO_CONVENIO = @Id;",
                new { Id = d.ConvenioId }, tx, ct))
                ?? throw Erro.Validacao("convenioId", "Convênio não encontrado");
            if (!conv.Ativo && liberacaoAdmin is null) throw Erro.Validacao("convenioId", "O convênio está inativo");
            convenioNome = conv.Nome;

            // O valor da consulta por convênio vem do credenciamento (convênio + unidade + especialidade), vigente na data da consulta.
            var achado = await CredenciamentoService.BuscarValorAsync(con, tx, d.ConvenioId!.Value, d.UnidadeId, d.EspecialidadeId,
                DateOnly.FromDateTime(DataBrasil.ParaBrasilia(d.Inicio)), ct);
            valorConsulta = achado?.Valor ?? 0m;

            if (liberacaoAdmin is null && paciente.ConvenioId == d.ConvenioId && paciente.ValidadeCarteirinha.HasValue && DateOnly.FromDateTime(paciente.ValidadeCarteirinha.Value) < DataBrasil.Hoje())
                throw Erro.Regra("CARTEIRINHA_VENCIDA",
                    $"Carteirinha vencida em {paciente.ValidadeCarteirinha.Value:dd/MM/yyyy}. Atualize o cadastro do paciente.");

            if (forma.Tipo != "CONVENIO" || forma.ConvenioId != d.ConvenioId)
                throw Erro.Validacao("formaPagamentoId", "No atendimento por convênio, use a forma de pagamento do convênio");
            // A carteirinha do cadastro só vale para o convênio do paciente.
            numeroCarteirinha = paciente.ConvenioId == d.ConvenioId ? paciente.NumeroCarteirinha : null;
        }
        else if (forma.Tipo == "CONVENIO")
        {
            throw Erro.Validacao("formaPagamentoId", "A forma de pagamento de convênio só vale para atendimento por convênio");
        }

        // ---- horário (RN10)
        var parametros = await AgendaService.LerParametrosAsync(con, tx, ct);
        var inicio = d.Inicio;
        var fim = d.Fim ?? inicio.AddMinutes(parametros.DuracaoPadraoConsultaMin ?? 45);

        if (liberacaoAdmin is null) await ValidarHorarioAsync(con, tx, d.ProfissionalId, d.UnidadeId, inicio, fim, d.Encaixe, ct);

        // ---- gravação pela procedure (reserva da agenda, RN09, limite de encaixes, RN35)
        var p = new DynamicParameters();
        p.Add("CODIGO_PACIENTE", paciente.Codigo, DbType.Int32);
        p.Add("CODIGO_PROFISSIONAL", d.ProfissionalId, DbType.Int32);
        p.Add("CODIGO_ESPECIALIDADE", d.EspecialidadeId, DbType.Int32);
        p.Add("CODIGO_UNIDADE", d.UnidadeId, DbType.Int32);
        p.Add("CODIGO_SALA", d.SalaId, DbType.Int32);
        p.Add("MODALIDADE", d.Modalidade, DbType.String, size: 10);
        p.Add("LINK_ONLINE", null, DbType.String, size: 500);
        p.Add("TIPO_ATENDIMENTO", d.Tipo, DbType.String, size: 10);
        p.Add("CODIGO_CONVENIO", d.ConvenioId, DbType.Int32);
        p.Add("NUMERO_CARTEIRINHA", numeroCarteirinha, DbType.String, size: 30);
        p.Add("DATA_INICIO", inicio, DbType.DateTime2);
        p.Add("DATA_FIM", fim, DbType.DateTime2);
        p.Add("VALOR", valorConsulta, DbType.Decimal, precision: 10, scale: 2);
        p.Add("CODIGO_PAGAMENTO", formaId, DbType.Int32);
        p.Add("PAGAMENTO_CONFIRMADO", pacote?.PagamentoConfirmado ?? d.PagamentoConfirmado, DbType.Boolean);
        p.Add("ENCAIXE", d.Encaixe, DbType.Boolean);
        p.Add("CODIGO_USUARIO", usuario.Id, DbType.Int32);
        p.Add("LIBERACAO_USUARIO", d.JustificativaLiberacao is null ? null : usuario.Id, DbType.Int32);
        p.Add("LIBERACAO_JUSTIFICATIVA", d.JustificativaLiberacao, DbType.String, size: 500);
        p.Add("CODIGO_CONSULTA", dbType: DbType.Int32, direction: ParameterDirection.Output);
        p.Add("CODIGO_PUBLIC", dbType: DbType.Guid, direction: ParameterDirection.Output);

        try
        {
            await con.ExecuteAsync(new CommandDefinition("DBO.USP_CONSULTA_CRIAR", p, tx,
                commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number is >= 50001 and <= 50072)
        {
            throw Traduzir(ex, convenioNome, uni.Nome);
        }

        var codigo = p.Get<int>("CODIGO_CONSULTA");
        if (liberacaoAdmin is not null) await GravarLiberacaoAdminAsync(con, tx, codigo, liberacaoAdmin, usuario.Id, ct);
        if (justificativaProfissional is not null)
            await con.ExecuteAsync(Sql.Cmd("UPDATE DBO.CONSULTA SET JUSTIFICATIVA_PROFISSIONAL = @J WHERE CODIGO_CONSULTA = @C;",
                new { J = justificativaProfissional, C = codigo }, tx, ct));
        if (pacote is not null) await PacotePacienteService.VincularAsync(con, tx, codigo, pacote.CodigoPacotePaciente, ct);

        await _auditoria.RegistrarAsync("INCLUIR", "CONSULTA", codigo, paciente.Codigo, usuario.Id,
            new { encaixe = d.Encaixe, tipo = d.Tipo, modalidade = d.Modalidade, liberacaoCredenciamento = d.JustificativaLiberacao is not null, liberacaoAdministrador = liberacaoAdmin, pacotePacienteId = pacote?.CodigoPacotePaciente, profissionalNaoResponsavel = justificativaProfissional is not null, justificativaProfissional },
            con, tx, ct);

        var linha = await con.QueryFirstAsync<ConsultaLinha>(Sql.Cmd(
            SelectConsulta + " WHERE C.CODIGO_CONSULTA = @Codigo;", new { Codigo = codigo }, tx, ct));
        await tx.CommitAsync(ct);

        var saida = ParaConsulta(linha, usuario.EhMedico);
        var avisos = new List<AvisoDto>();
        if (d.Modalidade == "ONLINE")
            avisos.Add(new AvisoDto
            {
                Codigo = "LINK_ONLINE_NAO_GERADO",
                Mensagem = "A consulta foi agendada, mas o link da consulta online não foi gerado. Informe o link ao paciente por outro meio."
            });
        saida.Avisos = avisos;
        return saida;
    }

    // ------------------------------------------------------ agendamento repetido
    /// <summary>Datas (UTC) da série. A hora de parede de Brasília se mantém; MENSAL usa o mesmo dia da semana e a mesma posição no mês (a 5ª vira a última).</summary>
    internal static List<DateTime> GerarDatasSerie(DateTime inicioUtc, string repeticao, int quantidade)
    {
        var local = DataBrasil.ParaBrasilia(inicioUtc);
        var datas = new List<DateTime>();
        var posicao = (local.Day - 1) / 7 + 1;
        for (var k = 0; k < quantidade; k++)
        {
            DateTime alvo;
            if (repeticao == "SEMANAL") alvo = local.AddDays(7 * k);
            else if (repeticao == "QUINZENAL") alvo = local.AddDays(14 * k);
            else
            {
                var mes = new DateTime(local.Year, local.Month, 1).AddMonths(k);
                var primeiro = new DateTime(mes.Year, mes.Month, 1);
                var deslocamento = ((int)local.DayOfWeek - (int)primeiro.DayOfWeek + 7) % 7;
                var dia = 1 + deslocamento + 7 * (posicao - 1);
                if (dia > DateTime.DaysInMonth(mes.Year, mes.Month)) dia -= 7;
                alvo = new DateTime(mes.Year, mes.Month, dia, local.Hour, local.Minute, 0);
            }
            datas.Add(DataBrasil.ParaUtc(alvo));
        }
        return datas;
    }

    /// <summary>
    /// Agenda a primeira consulta e as repetições, cada uma na sua transação. Se a primeira falhar, nada é criado e o erro é o da própria consulta.
    /// As demais que não puderem ser agendadas (horário ocupado, feriado, fora do expediente, pacote sem saldo...) são puladas e devolvidas com o motivo.
    /// </summary>
    public async Task<ConsultaSerieDto> AgendarSerieAsync(ConsultaSerieEntrada? entrada, UsuarioAtual usuario, CancellationToken ct)
    {
        if (entrada is null) throw Erro.Validacao("corpo", NaoPreenchido);
        var e = new List<ErroCampo>();
        if (entrada.Repeticao is not ("SEMANAL" or "QUINZENAL" or "MENSAL")) e.Add(new ErroCampo("repeticao", "Informe SEMANAL, QUINZENAL ou MENSAL"));
        if (entrada.Quantidade is null or < 2 or > 12) e.Add(new ErroCampo("quantidade", "Informe de 2 a 12 consultas"));
        if (entrada.Inicio is null) e.Add(new ErroCampo("inicio", NaoPreenchido));
        if (entrada.Encaixe) e.Add(new ErroCampo("encaixe", "O encaixe não pode ser repetido"));
        if (e.Count > 0) throw Erro.Validacao(e);

        var primeiraInicio = Minuto(entrada.Inicio!.Value);
        var duracao = entrada.Fim.HasValue ? Minuto(entrada.Fim.Value) - primeiraInicio : (TimeSpan?)null;
        var datas = GerarDatasSerie(primeiraInicio, entrada.Repeticao!, entrada.Quantidade!.Value);

        var saida = new ConsultaSerieDto();
        var primeira = await AgendarAsync(Copiar(entrada, datas[0], duracao, true, null), usuario, ct);
        saida.Criadas.Add(primeira);

        for (var i = 1; i < datas.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Pacote: as repetições usam o pacote da primeira (já existente ou vendido agora); cada uma consome uma sessão.
                var consulta = await AgendarAsync(Copiar(entrada, datas[i], duracao, false, primeira.PacotePacienteId), usuario, ct);
                saida.Criadas.Add(consulta);
            }
            catch (ApiException ex)
            {
                saida.Ignoradas.Add(new SerieIgnoradaDto { Inicio = datas[i], Codigo = ex.Codigo, Mensagem = ex.Detalhe ?? ex.Titulo });
            }
        }

        // Marca as consultas criadas como uma série (serve para cancelar esta e as próximas).
        var serie = Guid.NewGuid();
        await using (var con = await _db.AbrirAsync(ct))
            await con.ExecuteAsync(Sql.Cmd("UPDATE DBO.CONSULTA SET CODIGO_SERIE = @Serie WHERE CODIGO_PUBLIC IN @Ids;",
                new { Serie = serie, Ids = saida.Criadas.Select(c => c.Id).ToArray() }, ct: ct));
        foreach (var c in saida.Criadas) c.SerieId = serie;
        return saida;
    }

    /// <summary>Cancela a consulta atual e as próximas da mesma série (AGENDADA ou CONFIRMADA). Se a atual falhar, nada é cancelado; as próximas que falharem voltam em Ignoradas.</summary>
    public async Task<CancelamentoSerieDto> CancelarSerieAsync(Guid id, CancelamentoSerieEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var atual = await AlterarStatusAsync(id, new StatusEntrada
        {
            Status = "CANCELADA", Motivo = d.Motivo, CanceladoPor = d.CanceladoPor, Versao = d.Versao, LiberacaoAdministrador = d.LiberacaoAdministrador
        }, usuario, ct);

        var saida = new CancelamentoSerieDto { Canceladas = 1 };
        if (atual.SerieId is null) return saida;

        List<ProximaSerie> proximas;
        await using (var con = await _db.AbrirAsync(ct))
            proximas = (await con.QueryAsync<ProximaSerie>(Sql.Cmd(@"
SELECT CODIGO_PUBLIC AS Id, VERSAO AS Versao, DATA_INICIO AS Inicio FROM DBO.CONSULTA
WHERE CODIGO_SERIE = @Serie AND DATA_INICIO > @Inicio AND STATUS IN ('AGENDADA', 'CONFIRMADA') AND CODIGO_PUBLIC <> @Id
ORDER BY DATA_INICIO;", new { Serie = atual.SerieId, Inicio = DateTime.SpecifyKind(atual.Inicio, DateTimeKind.Utc), Id = id }, ct: ct))).ToList();

        foreach (var p in proximas)
        {
            try
            {
                await AlterarStatusAsync(p.Id, new StatusEntrada
                {
                    Status = "CANCELADA", Motivo = d.Motivo, CanceladoPor = d.CanceladoPor,
                    Versao = Convert.ToBase64String(p.Versao), LiberacaoAdministrador = d.LiberacaoAdministrador
                }, usuario, ct);
                saida.Canceladas++;
            }
            catch (ApiException ex)
            {
                saida.Ignoradas.Add(new SerieIgnoradaDto { Inicio = DataBrasil.Utc(p.Inicio), Codigo = ex.Codigo, Mensagem = ex.Detalhe ?? ex.Titulo });
            }
        }
        return saida;
    }

    private sealed class ProximaSerie
    {
        public Guid Id { get; set; }
        public byte[] Versao { get; set; } = Array.Empty<byte>();
        public DateTime Inicio { get; set; }
    }

    private static ConsultaEntrada Copiar(ConsultaSerieEntrada o, DateTime inicio, TimeSpan? duracao, bool primeira, Guid? pacotePacienteId) => new()
    {
        PacienteId = o.PacienteId, ProfissionalId = o.ProfissionalId, EspecialidadeId = o.EspecialidadeId, UnidadeId = o.UnidadeId,
        SalaId = o.SalaId, Modalidade = o.Modalidade, TipoAtendimento = o.TipoAtendimento, ConvenioId = o.ConvenioId,
        Valor = o.Valor, FormaPagamentoId = o.FormaPagamentoId,
        Inicio = inicio, Fim = duracao.HasValue ? inicio + duracao.Value : null,
        Encaixe = false,
        // O pagamento recebido vale só para a primeira consulta avulsa (no pacote, vale para o pacote, na venda).
        PagamentoConfirmado = primeira && o.PagamentoConfirmado,
        PacotePacienteId = primeira ? o.PacotePacienteId : pacotePacienteId,
        VendaPacote = primeira ? o.VendaPacote : null,
        LiberacaoCredenciamento = o.LiberacaoCredenciamento,
        LiberacaoAdministrador = o.LiberacaoAdministrador,
        JustificativaProfissional = o.JustificativaProfissional
    };

    // ------------------------------------------------------ status e remarcação
    private static readonly string[] StatusInformaveis = { "CONFIRMADA", "CHECK_IN", "EM_ATENDIMENTO", "CONCLUIDA", "CANCELADA", "FALTA" };

    /// <summary>Liberação do administrador: devolve a justificativa validada, ou null quando não foi pedida.</summary>
    private static string? LerLiberacaoAdmin(LiberacaoAdministradorEntrada? l, UsuarioAtual usuario)
    {
        if (l is null) return null;
        if (!usuario.Administrador)
            throw Erro.SemPermissao("Somente o administrador pode liberar esta etapa.", "LIBERACAO_ADMIN_NEGADA");
        var j = l.Justificativa?.Trim();
        if (string.IsNullOrEmpty(j)) throw Erro.Validacao("liberacaoAdministrador.justificativa", NaoPreenchido);
        if (j.Length is < 10 or > 500) throw Erro.Validacao("liberacaoAdministrador.justificativa", "Informe de 10 a 500 caracteres");
        return j;
    }

    private static async Task GravarLiberacaoAdminAsync(SqlConnection con, SqlTransaction? tx, int codigoConsulta, string justificativa, int usuarioId, CancellationToken ct) =>
        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.CONSULTA SET LIBERACAO_ADMIN_USUARIO = @U, LIBERACAO_ADMIN_JUSTIFICATIVA = @J, LIBERACAO_ADMIN_DATA = SYSUTCDATETIME()
WHERE CODIGO_CONSULTA = @C;", new { U = usuarioId, J = justificativa, C = codigoConsulta }, tx, ct));

    private static byte[] LerVersao(string? versao, List<ErroCampo> e)
    {
        if (string.IsNullOrWhiteSpace(versao)) { e.Add(new ErroCampo("versao", NaoPreenchido)); return Array.Empty<byte>(); }
        try
        {
            var bytes = Convert.FromBase64String(versao);
            if (bytes.Length == 8) return bytes;
        }
        catch (FormatException) { }
        e.Add(new ErroCampo("versao", "Versão inválida"));
        return Array.Empty<byte>();
    }

    private async Task<ConsultaLinha> CarregarParaAlterarAsync(SqlConnection con, SqlTransaction? tx, Guid id, UsuarioAtual usuario, CancellationToken ct)
    {
        if (usuario.EhMedico) throw Erro.SemPermissao("O perfil Médico não altera o status nem remarca consultas.");
        return await con.QueryFirstOrDefaultAsync<ConsultaLinha>(Sql.Cmd(SelectConsulta + " WHERE C.CODIGO_PUBLIC = @Id;", new { Id = id }, tx, ct))
            ?? throw Erro.NaoEncontrado("Consulta não encontrada.");
    }

    /// <summary>Concluir, registrar falta (não concluída), cancelar e demais mudanças de status (RN06, RN15, G6, RN17).</summary>
    public async Task<ConsultaDto> AlterarStatusAsync(Guid id, StatusEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var e = new List<ErroCampo>();
        if (string.IsNullOrWhiteSpace(d.Status)) e.Add(new ErroCampo("status", NaoPreenchido));
        else if (!StatusInformaveis.Contains(d.Status)) e.Add(new ErroCampo("status", "Status inválido"));
        if (d.CanceladoPor is not null and not ("PACIENTE" or "CLINICA")) e.Add(new ErroCampo("canceladoPor", "Informe PACIENTE ou CLINICA"));
        var motivo = d.Motivo?.Trim();
        if (motivo is { Length: > 500 }) e.Add(new ErroCampo("motivo", "Máximo de 500 caracteres"));
        if ((d.Status is "CANCELADA" or "FALTA") && string.IsNullOrEmpty(motivo)) e.Add(new ErroCampo("motivo", NaoPreenchido));
        if (d.Status == "CANCELADA" && d.CanceladoPor is null) e.Add(new ErroCampo("canceladoPor", NaoPreenchido));
        var versao = LerVersao(d.Versao, e);
        if (e.Count > 0) throw Erro.Validacao(e);
        var liberacao = LerLiberacaoAdmin(d.LiberacaoAdministrador, usuario);

        await using var con = await _db.AbrirAsync(ct);
        var atual = await CarregarParaAlterarAsync(con, null, id, usuario, ct);

        // Convênio com carteirinha vencida: iniciar e concluir o atendimento só com liberação (justificativa) do administrador.
        if (liberacao is null && atual.TipoAtendimento == "CONVENIO" && d.Status is ("EM_ATENDIMENTO" or "CONCLUIDA")
            && atual.PacienteValidadeCarteirinha.HasValue && DateOnly.FromDateTime(atual.PacienteValidadeCarteirinha.Value) < DataBrasil.Hoje())
        {
            throw Erro.Regra("CARTEIRINHA_VENCIDA_ATENDIMENTO",
                $"{(d.Status == "EM_ATENDIMENTO" ? "O atendimento não pode ser iniciado" : "A consulta não pode ser concluída")}, pois a validade da carteirinha do convênio do paciente está vencida ({atual.PacienteValidadeCarteirinha.Value:dd/MM/yyyy}). Solicite autorização do administrador ou da gerência para liberação.");
        }

        var p = new DynamicParameters();
        p.Add("LIBERADO", liberacao is not null, DbType.Boolean);
        p.Add("LIBERACAO_JUSTIFICATIVA", liberacao, DbType.String, size: 500);
        p.Add("CODIGO_CONSULTA", atual.Codigo, DbType.Int32);
        p.Add("STATUS_NOVO", d.Status, DbType.String, size: 20);
        p.Add("MOTIVO", string.IsNullOrEmpty(motivo) ? null : motivo, DbType.String, size: 500);
        p.Add("CANCELADO_POR", d.Status == "CANCELADA" ? d.CanceladoPor : null, DbType.String, size: 10);
        p.Add("VERSAO", versao, DbType.Binary, size: 8);
        p.Add("CODIGO_USUARIO", usuario.Id, DbType.Int32);

        try
        {
            await con.ExecuteAsync(new CommandDefinition("DBO.USP_CONSULTA_ALTERAR_STATUS", p, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number is >= 50001 and <= 50072)
        {
            throw Traduzir(ex, atual.ConvenioNome, atual.UnidadeNome);
        }

        await _auditoria.RegistrarAsync("EDITAR", "CONSULTA", atual.Codigo, null, usuario.Id, new { status = d.Status, de = atual.Status, liberacaoAdministrador = liberacao }, con, ct: ct);

        var linha = await con.QueryFirstAsync<ConsultaLinha>(Sql.Cmd(SelectConsulta + " WHERE C.CODIGO_CONSULTA = @Codigo;", new { Codigo = atual.Codigo }, ct: ct));
        return ParaConsulta(linha, usuario.EhMedico);
    }

    /// <summary>Confirma o pagamento do atendimento particular na recepção (G4, RN17). No convênio quem confirma é o faturamento, pela guia.</summary>
    public async Task<ConsultaDto> ConfirmarPagamentoAsync(Guid id, PagamentoConfirmacaoEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var e = new List<ErroCampo>();
        var versao = LerVersao(d.Versao, e);
        if (e.Count > 0) throw Erro.Validacao(e);
        var liberacaoAdmin = LerLiberacaoAdmin(d.LiberacaoAdministrador, usuario);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        var atual = await CarregarParaAlterarAsync(con, tx, id, usuario, ct);

        if (atual.TipoAtendimento == "CONVENIO")
            throw Erro.Conflito("No convênio o pagamento é confirmado pelo faturamento, na guia.", "PAGAMENTO_CONVENIO_VIA_GUIA");
        if (atual.Status != "CHECK_IN" && !(liberacaoAdmin is not null && (atual.Status is "AGENDADA" or "CONFIRMADA" or "EM_ATENDIMENTO")))
            throw Erro.Conflito("O pagamento é confirmado depois do check-in. Confirme o agendamento e faça o check-in primeiro.", "STATUS_NAO_PERMITE");
        if (atual.PagamentoConfirmado)
            throw Erro.Conflito("O pagamento já foi confirmado.", "PAGAMENTO_JA_CONFIRMADO");

        var formaId = atual.FormaPagamentoId;
        if (d.FormaPagamentoId.HasValue && d.FormaPagamentoId.Value != formaId)
        {
            var forma = await con.QueryFirstOrDefaultAsync<FormaBasica>(Sql.Cmd(
                "SELECT ATIVO AS Ativo, TIPO AS Tipo FROM DBO.FORMA_PAGAMENTO WHERE CODIGO_PAGAMENTO = @Id;", new { Id = d.FormaPagamentoId.Value }, tx, ct))
                ?? throw Erro.Validacao("formaPagamentoId", "Forma de pagamento não encontrada");
            if (!forma.Ativo) throw Erro.Validacao("formaPagamentoId", "A forma de pagamento está inativa");
            if (forma.Tipo == "CONVENIO") throw Erro.Validacao("formaPagamentoId", "A forma de pagamento de convênio só vale para atendimento por convênio");
            formaId = d.FormaPagamentoId.Value;
        }

        var linhas = await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.CONSULTA
SET PAGAMENTO_CONFIRMADO = 1, DATA_PAGAMENTO = SYSUTCDATETIME(), PAGAMENTO_CONFIRMADO_USUARIO = @Usuario,
    CODIGO_PAGAMENTO = @Forma, DATA_ATUALIZACAO = SYSUTCDATETIME()
WHERE CODIGO_CONSULTA = @Codigo AND VERSAO = @Versao;",
            new { Usuario = usuario.Id, Forma = formaId, Codigo = atual.Codigo, Versao = versao }, tx, ct));
        if (linhas == 0)
            throw Erro.Conflito("A consulta foi alterada por outra pessoa. Atualize a tela e tente novamente.", "CONSULTA_ALTERADA");

        await _auditoria.RegistrarAsync("EDITAR", "CONSULTA", atual.Codigo, null, usuario.Id,
            new { pagamentoConfirmado = true, formaPagamentoId = formaId, liberacaoAdministrador = liberacaoAdmin }, con, tx, ct);
        if (liberacaoAdmin is not null) await GravarLiberacaoAdminAsync(con, tx, atual.Codigo, liberacaoAdmin, usuario.Id, ct);

        var linha = await con.QueryFirstAsync<ConsultaLinha>(Sql.Cmd(SelectConsulta + " WHERE C.CODIGO_CONSULTA = @Codigo;", new { Codigo = atual.Codigo }, tx, ct));
        await tx.CommitAsync(ct);
        return ParaConsulta(linha, usuario.EhMedico);
    }

    private sealed class FormaBasica
    {
        public bool Ativo { get; set; }
        public string Tipo { get; set; } = string.Empty;
    }

    /// <summary>Grava a observação do check-in (só consulta por convênio, enquanto está em CHECK_IN). Vazio apaga a observação.</summary>
    public async Task<ConsultaDto> SalvarObservacaoAsync(Guid id, ObservacaoEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var e = new List<ErroCampo>();
        var versao = LerVersao(d.Versao, e);
        var texto = d.Observacao?.Trim();
        if (texto is { Length: > 500 }) e.Add(new ErroCampo("observacao", "Máximo de 500 caracteres"));
        if (e.Count > 0) throw Erro.Validacao(e);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        var atual = await CarregarParaAlterarAsync(con, tx, id, usuario, ct);

        if (atual.TipoAtendimento != "CONVENIO")
            throw Erro.Conflito("A observação é só para consulta por convênio.", "OBSERVACAO_SO_CONVENIO");
        if (atual.Status != "CHECK_IN")
            throw Erro.Conflito("A observação é anotada enquanto a consulta está em check-in.", "STATUS_NAO_PERMITE");

        var linhas = await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.CONSULTA SET OBSERVACAO_CHECKIN = @Texto, DATA_ATUALIZACAO = SYSUTCDATETIME()
WHERE CODIGO_CONSULTA = @Codigo AND VERSAO = @Versao;",
            new { Texto = string.IsNullOrEmpty(texto) ? null : texto, Codigo = atual.Codigo, Versao = versao }, tx, ct));
        if (linhas == 0)
            throw Erro.Conflito("A consulta foi alterada por outra pessoa. Atualize a tela e tente novamente.", "CONSULTA_ALTERADA");

        await _auditoria.RegistrarAsync("EDITAR", "CONSULTA", atual.Codigo, null, usuario.Id, new { observacaoCheckin = true }, con, tx, ct);

        var linha = await con.QueryFirstAsync<ConsultaLinha>(Sql.Cmd(SelectConsulta + " WHERE C.CODIGO_CONSULTA = @Codigo;", new { Codigo = atual.Codigo }, tx, ct));
        await tx.CommitAsync(ct);
        return ParaConsulta(linha, usuario.EhMedico);
    }

    /// <summary>Remarca (ou antecipa): cria uma nova consulta ligada à original, que passa a REMARCADA (RN06, G5).</summary>
    public async Task<ConsultaDto> RemarcarAsync(Guid id, RemarcacaoEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var e = new List<ErroCampo>();
        if (d.NovoInicio is null) e.Add(new ErroCampo("novoInicio", NaoPreenchido));
        var motivo = d.Motivo?.Trim();
        if (string.IsNullOrEmpty(motivo)) e.Add(new ErroCampo("motivo", NaoPreenchido));
        else if (motivo.Length > 500) e.Add(new ErroCampo("motivo", "Máximo de 500 caracteres"));
        var versao = LerVersao(d.Versao, e);
        var liberacaoAdmin = LerLiberacaoAdmin(d.LiberacaoAdministrador, usuario);
        string? justificativa = null;
        if (d.LiberacaoCredenciamento is not null)
        {
            justificativa = d.LiberacaoCredenciamento.Justificativa?.Trim();
            if (string.IsNullOrEmpty(justificativa)) e.Add(new ErroCampo("liberacaoCredenciamento.justificativa", NaoPreenchido));
            else if (justificativa.Length is < 10 or > 500) e.Add(new ErroCampo("liberacaoCredenciamento.justificativa", "Informe de 10 a 500 caracteres"));
        }
        DateTime? novoInicio = d.NovoInicio is null ? null : Minuto(d.NovoInicio.Value);
        DateTime? novoFim = d.NovoFim is null ? null : Minuto(d.NovoFim.Value);
        if (novoInicio is not null && novoFim is not null && novoFim <= novoInicio) e.Add(new ErroCampo("novoFim", "O término deve ser depois do início"));
        if (e.Count > 0) throw Erro.Validacao(e);

        if (d.Encaixe && !await _permissoes.TemAsync(usuario.PerfilId, usuario.Administrador, "consulta.agendamento:editar", ct))
            throw Erro.SemPermissao("O seu perfil não tem liberação para incluir encaixe.", "ENCAIXE_SEM_PERMISSAO");
        if (justificativa is not null && !usuario.Administrador)
            throw Erro.SemPermissao("Somente o administrador libera consulta por convênio sem credenciamento.", "LIBERACAO_CREDENCIAMENTO_NEGADA");

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var orig = await CarregarParaAlterarAsync(con, tx, id, usuario, ct);
        if (orig.Status is not ("AGENDADA" or "CONFIRMADA"))
            throw Erro.Conflito("Só é possível remarcar consulta agendada ou confirmada.", "STATUS_NAO_PERMITE");

        var profissionalId = d.ProfissionalId ?? orig.ProfissionalId;
        var unidadeId = d.UnidadeId ?? orig.UnidadeId;
        var parametros = await AgendaService.LerParametrosAsync(con, tx, ct);
        var inicio = novoInicio!.Value;
        var fim = novoFim ?? inicio.AddMinutes(parametros.DuracaoPadraoConsultaMin ?? 45);
        if (liberacaoAdmin is null) await ValidarHorarioAsync(con, tx, profissionalId, unidadeId, inicio, fim, d.Encaixe, ct);
        // Consulta de pacote: a nova data precisa estar dentro da validade do pacote.
        await PacotePacienteService.ValidarRemarcacaoAsync(con, tx, orig.Codigo, inicio, ct);

        var p = new DynamicParameters();
        p.Add("CODIGO_CONSULTA", orig.Codigo, DbType.Int32);
        p.Add("NOVO_INICIO", inicio, DbType.DateTime2);
        p.Add("NOVO_FIM", fim, DbType.DateTime2);
        p.Add("MOTIVO", motivo, DbType.String, size: 500);
        p.Add("CODIGO_PROFISSIONAL", d.ProfissionalId, DbType.Int32);
        p.Add("CODIGO_ESPECIALIDADE", d.EspecialidadeId, DbType.Int32);
        p.Add("CODIGO_UNIDADE", d.UnidadeId, DbType.Int32);
        p.Add("CODIGO_SALA", d.SalaId, DbType.Int32);
        p.Add("ENCAIXE", d.Encaixe, DbType.Boolean);
        p.Add("VERSAO", versao, DbType.Binary, size: 8);
        p.Add("CODIGO_USUARIO", usuario.Id, DbType.Int32);
        p.Add("LIBERACAO_USUARIO", justificativa is null ? null : usuario.Id, DbType.Int32);
        p.Add("LIBERACAO_JUSTIFICATIVA", justificativa, DbType.String, size: 500);
        p.Add("CODIGO_CONSULTA_NOVA", dbType: DbType.Int32, direction: ParameterDirection.Output);
        p.Add("CODIGO_PUBLIC", dbType: DbType.Guid, direction: ParameterDirection.Output);

        try
        {
            await con.ExecuteAsync(new CommandDefinition("DBO.USP_CONSULTA_REMARCAR", p, tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number is >= 50001 and <= 50072)
        {
            throw Traduzir(ex, orig.ConvenioNome, orig.UnidadeNome);
        }

        var codigoNova = p.Get<int>("CODIGO_CONSULTA_NOVA");

        // A guia do convênio e o pacote (se houver) acompanham o paciente para a nova consulta.
        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE N SET N.GUIA_STATUS = O.GUIA_STATUS, N.GUIA_NUMERO = O.GUIA_NUMERO, N.GUIA_OBSERVACAO = O.GUIA_OBSERVACAO,
             N.GUIA_DATA = O.GUIA_DATA, N.GUIA_USUARIO = O.GUIA_USUARIO, N.CODIGO_PACOTE_PACIENTE = O.CODIGO_PACOTE_PACIENTE
FROM DBO.CONSULTA N JOIN DBO.CONSULTA O ON O.CODIGO_CONSULTA = @Orig
WHERE N.CODIGO_CONSULTA = @Nova;", new { Orig = orig.Codigo, Nova = codigoNova }, tx, ct));

        if (liberacaoAdmin is not null) await GravarLiberacaoAdminAsync(con, tx, codigoNova, liberacaoAdmin, usuario.Id, ct);
        await _auditoria.RegistrarAsync("EDITAR", "CONSULTA", orig.Codigo, null, usuario.Id, new { remarcada = true, novaConsulta = codigoNova, liberacaoAdministrador = liberacaoAdmin }, con, tx, ct);
        await _auditoria.RegistrarAsync("INCLUIR", "CONSULTA", codigoNova, null, usuario.Id, new { remarcacao = true }, con, tx, ct);

        var linha = await con.QueryFirstAsync<ConsultaLinha>(Sql.Cmd(SelectConsulta + " WHERE C.CODIGO_CONSULTA = @Codigo;", new { Codigo = codigoNova }, tx, ct));
        await tx.CommitAsync(ct);
        var saida = ParaConsulta(linha, usuario.EhMedico);
        saida.Avisos = new List<AvisoDto>();
        return saida;
    }

    /// <summary>Regras de horário do agendamento e da remarcação (RN10): passado, feriado, recesso, expediente e intervalo.</summary>
    private static async Task ValidarHorarioAsync(SqlConnection con, SqlTransaction tx, int profissionalId, int unidadeId,
        DateTime inicio, DateTime fim, bool encaixe, CancellationToken ct)
    {
        if (inicio <= DateTime.UtcNow)
            throw Erro.Regra("HORARIO_NO_PASSADO", "O horário escolhido já passou.");

        var diaInicio = DateOnly.FromDateTime(DataBrasil.ParaBrasilia(inicio));
        var diaFim = DateOnly.FromDateTime(DataBrasil.ParaBrasilia(fim.AddSeconds(-1)));
        var cal = await AgendaService.MontarCalendarioAsync(con, profissionalId, unidadeId, diaInicio, diaFim, ct, tx);

        var fechado = cal.Bloqueios.FirstOrDefault(b => (b.Tipo == "FERIADO" || b.Tipo == "RECESSO") && b.Inicio < fim && b.Fim > inicio);
        if (fechado is not null)
            throw Erro.Regra("HORARIO_BLOQUEADO", fechado.Tipo == "FERIADO"
                ? $"Não é possível agendar para esta data, pois é feriado de {NomeDoFeriado(fechado.Descricao)}."
                : "Não é possível agendar para esta data, pois a clínica está em recesso de fim de ano.");

        if (!encaixe)
        {
            // O fim do expediente do dia é o horário do último paciente: ele pode terminar depois do expediente.
            var fimDoDia = AgendaService.FimDoExpedientePorDia(cal).GetValueOrDefault(diaInicio);
            if (!cal.Expediente.Any(x => x.P.Inicio <= inicio && (x.P.Fim >= fim || (x.P.Fim == fimDoDia && inicio <= x.P.Fim))))
                throw Erro.Regra("FORA_DO_EXPEDIENTE", "O horário está fora do expediente de atendimento do profissional nesta unidade.");
            if (cal.Intervalos.Any(x => x.P.Inicio < fim && x.P.Fim > inicio))
                throw Erro.Regra("HORARIO_BLOQUEADO", "O horário cai no intervalo do profissional.");
        }
    }

    /// <summary>"Carnaval (terça-feira)" vira "Carnaval".</summary>
    private static string NomeDoFeriado(string? nome)
    {
        var n = (nome ?? "feriado").Split(" (")[0].Trim();
        return n;
    }

    /// <summary>Mapeia os THROW das procedures (scripts 02 e 09) para os códigos do contrato.</summary>
    private static ApiException Traduzir(SqlException ex, string? convenioNome, string unidadeNome) => ex.Number switch
    {
        50001 => Erro.Conflito("A agenda do profissional está em uso por outra operação. Tente novamente em instantes.", "AGENDA_EM_USO"),
        50002 => Erro.Conflito("Horário indisponível: o profissional já tem consulta nesse intervalo.", "HORARIO_INDISPONIVEL"),
        50003 => Erro.Conflito("O limite de encaixes do dia para este profissional foi atingido.", "LIMITE_ENCAIXES_ATINGIDO"),
        50070 => Erro.Conflito(
            $"O convênio {convenioNome} não tem credenciamento aprovado para a unidade {unidadeNome} nesta especialidade. " +
            "Agende como particular ou escolha outra unidade.", "CONVENIO_NAO_CREDENCIADO"),
        50004 => Erro.Conflito("A consulta foi alterada por outro usuário. Recarregue e tente novamente.", "CONSULTA_ALTERADA"),
        50005 => Erro.NaoEncontrado("Consulta não encontrada."),
        50006 => Erro.Validacao("status", "Status inválido"),
        50007 => Erro.Conflito("Mudança de status não permitida pelo fluxo da consulta.", "TRANSICAO_INVALIDA"),
        50008 => Erro.Validacao("motivo", ex.Message),
        50009 => Erro.Conflito("Cancelamento pelo paciente fora do prazo mínimo de antecedência.", "CANCELAMENTO_FORA_DO_PRAZO"),
        50010 => Erro.Conflito("A falta só pode ser registrada depois do horário da consulta.", "FALTA_ANTES_DO_HORARIO"),
        50011 => Erro.Conflito("Confirme o pagamento antes de iniciar o atendimento.", "PAGAMENTO_PENDENTE"),
        50015 => Erro.Conflito("A guia do convênio precisa estar autorizada pelo faturamento para iniciar o atendimento e concluir a consulta.", "GUIA_NAO_AUTORIZADA"),
        50016 => Erro.Conflito("A guia do convênio já foi autorizada. O faturamento precisa cancelar a guia antes de cancelar a consulta.", "GUIA_AUTORIZADA_CANCELAR_ANTES"),
        50013 => Erro.Conflito("Só é possível remarcar consulta agendada ou confirmada.", "STATUS_NAO_PERMITE"),
        50014 => Erro.Conflito("A consulta só pode ser concluída diretamente depois do horário agendado.", "CONCLUSAO_ANTES_DO_HORARIO"),
        50071 => Erro.Validacao("liberacaoCredenciamento.justificativa", "Informe a justificativa da liberação"),
        50072 => Erro.SemPermissao("Somente o administrador libera consulta por convênio sem credenciamento.", "LIBERACAO_CREDENCIAMENTO_NEGADA"),
        _ => Erro.Conflito(ex.Message, "CONSULTA_NAO_GRAVADA")
    };
}
