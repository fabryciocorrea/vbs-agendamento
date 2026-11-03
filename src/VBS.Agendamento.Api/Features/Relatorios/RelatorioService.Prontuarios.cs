using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Consultas;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Relatorios;

/// <summary>
/// Relatório de prontuários (RF25): registros por paciente e profissional, SEM o texto clínico (L7). Só o administrador pede o texto.
/// Nunca para o médico (RN20). Unidade, convênio e tipo valem para os registros ligados a uma consulta.
/// </summary>
public sealed partial class RelatorioService
{
    private sealed class LinhaProntuario
    {
        public Guid RegistroId { get; set; }
        public Guid PacienteId { get; set; }
        public string PacienteNome { get; set; } = string.Empty;
        public int ProfissionalId { get; set; }
        public string ProfissionalNome { get; set; } = string.Empty;
        public string Conselho { get; set; } = string.Empty;
        public string NumeroConselho { get; set; } = string.Empty;
        public string Uf { get; set; } = string.Empty;
        public DateTime RegistradoEm { get; set; }
        public bool Corrigido { get; set; }
        public bool OrigemAdministrativa { get; set; }
        public string? IncluidoPorNome { get; set; }
        public string? Observacao { get; set; }
    }

    private sealed class ResumoProntuario
    {
        public int Total { get; set; }
        public int Pacientes { get; set; }
    }

    private const string DeProntuario = @"
FROM DBO.REGISTRO_PRONTUARIO R
JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = R.CODIGO_PACIENTE
LEFT JOIN DBO.CONSULTA C ON C.CODIGO_CONSULTA = R.CODIGO_CONSULTA
LEFT JOIN DBO.USUARIO UI ON UI.CODIGO_USUARIO = R.CODIGO_USUARIO_INCLUSAO
WHERE R.DATA_HORA_REGS >= @De AND R.DATA_HORA_REGS < @Ate
  AND (@Prof IS NULL OR R.CODIGO_PROFISSIONAL = @Prof)
  AND (@Unidade IS NULL OR C.CODIGO_UNIDADE = @Unidade)
  AND (@Convenio IS NULL OR C.CODIGO_CONVENIO = @Convenio)
  AND (@Tipo IS NULL OR C.TIPO_ATENDIMENTO = @Tipo)";

    private async Task<(List<LinhaProntuario> Linhas, int Total, int Pacientes)> ConsultarProntuariosAsync(
        SqlConnection con, Janela j, FiltroRelatorio f, bool texto, int deslocamento, int tamanho, CancellationToken ct)
    {
        var p = new DynamicParameters(Parametros(j, f));
        var resumo = await con.QuerySingleAsync<ResumoProntuario>(Sql.Cmd(
            "SELECT CAST(COUNT(1) AS INT) AS Total, CAST(COUNT(DISTINCT R.CODIGO_PACIENTE) AS INT) AS Pacientes" + DeProntuario + ";", p, ct: ct));

        p.Add("Deslocamento", deslocamento);
        p.Add("Tamanho", tamanho);
        var linhas = (await con.QueryAsync<LinhaProntuario>(Sql.Cmd(@"
SELECT R.CODIGO_PUBLIC AS RegistroId, P.CODIGO_PUBLIC AS PacienteId, P.NOME AS PacienteNome,
       R.CODIGO_PROFISSIONAL AS ProfissionalId, R.NOME_PROFISSIONAL AS ProfissionalNome,
       R.REGISTRO_PROFISSIONAL AS Conselho, R.NUMERO_CONSELHO AS NumeroConselho, R.UF_CONSELHO AS Uf,
       R.DATA_HORA_REGS AS RegistradoEm,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM DBO.PRONTUARIO_CORRECAO K WHERE K.CODIGO_REGS_PRNT = R.CODIGO_REGS_PRNT) THEN 1 ELSE 0 END AS BIT) AS Corrigido,
       CAST(CASE WHEN R.CODIGO_USUARIO_INCLUSAO IS NULL THEN 0 ELSE 1 END AS BIT) AS OrigemAdministrativa,
       UI.NOME_COMPLETO AS IncluidoPorNome,
       " + (texto ? "R.OBSERVACAO" : "CAST(NULL AS NVARCHAR(MAX))") + @" AS Observacao" + DeProntuario + @"
ORDER BY R.DATA_HORA_REGS DESC, R.CODIGO_REGS_PRNT DESC
OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct))).ToList();
        return (linhas, resumo.Total, resumo.Pacientes);
    }

    public async Task<RelatorioProntuariosDto> ProntuariosAsync(FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        if (f.Pagina < 1) throw Erro.Validacao("pagina", "Deve ser 1 ou mais");
        if (f.Tamanho is < 1 or > 100) throw Erro.Validacao("tamanho", "Deve estar entre 1 e 100");
        if (f.IncluirTexto && !usuario.Administrador)
            throw Erro.SemPermissao("Só o administrador pode incluir o texto dos registros no relatório.", "TEXTO_SOMENTE_ADMINISTRADOR");

        await using var con = await _db.AbrirAsync(ct);
        var j = await PrepararAsync(con, f, ct);
        var (linhas, total, pacientes) = await ConsultarProntuariosAsync(con, j, f, f.IncluirTexto, (f.Pagina - 1) * f.Tamanho, f.Tamanho, ct);

        await _auditoria.RegistrarAsync("VISUALIZAR", "RELATORIO_PRONTUARIOS", null, null, usuario.Id,
            new { de = j.Inicio, ate = j.Fim, profissional = f.ProfissionalId, unidade = f.UnidadeId, convenio = f.ConvenioId, tipo = j.Tipo, registros = total, textoIncluido = f.IncluirTexto },
            con, ct: ct);

        return new RelatorioProntuariosDto
        {
            Filtros = j.Aplicados,
            GeradoEm = DateTime.UtcNow,
            TotalPacientes = pacientes,
            Resultado = new ProntuarioRelatorioPaginaDto
            {
                Itens = linhas.Select(l => new ProntuarioRelatorioItemDto
                {
                    RegistroId = l.RegistroId,
                    Paciente = new PacienteReferenciaDto { Id = l.PacienteId, Nome = l.PacienteNome },
                    Profissional = new ProfissionalProntuarioDto
                    {
                        Id = l.ProfissionalId, Nome = l.ProfissionalNome, RegistroProfissional = l.Conselho,
                        NumeroConselho = l.NumeroConselho, UfConselho = l.Uf
                    },
                    RegistradoEm = DataBrasil.Utc(l.RegistradoEm),
                    Corrigido = l.Corrigido,
                    // Só o administrador vê que o registro foi incluído por administrador e por quem.
                    OrigemAdministrativa = usuario.Administrador && l.OrigemAdministrativa,
                    IncluidoPorNome = usuario.Administrador ? l.IncluidoPorNome : null,
                    Observacao = f.IncluirTexto ? l.Observacao : null
                }).ToList(),
                Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total
            }
        };
    }

    /// <summary>Exportação: nunca leva o texto clínico (L7). Registro, data, paciente, profissional e as marcas de correção e inclusão administrativa.</summary>
    private async Task<TabelaExportacao> TabelaProntuariosAsync(FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var j = await PrepararAsync(con, f, ct);
        var (linhas, total, pacientes) = await ConsultarProntuariosAsync(con, j, f, false, 0, LimiteExportacao + 1, ct);

        var filtros = TextoDosFiltros(j.Aplicados);
        filtros.Add("Texto clínico: não incluído");
        var tabela = new TabelaExportacao
        {
            Titulo = "Relatório de prontuários",
            Filtros = filtros,
            Colunas =
            {
                ColunaTabela.Texto("Data"), ColunaTabela.Texto("Hora"), ColunaTabela.Texto("Paciente"), ColunaTabela.Texto("Profissional"),
                ColunaTabela.Texto("Conselho"), ColunaTabela.Texto("Corrigido"), ColunaTabela.Texto("Incluído por (administrador)")
            },
            Totais = new List<string> { "Total", $"{total.ToString(Pt)} registro(s)", $"{pacientes.ToString(Pt)} paciente(s)", "", "", "", "" }
        };
        foreach (var l in linhas)
        {
            var local = DataBrasil.ParaBrasilia(l.RegistradoEm);
            tabela.Linhas.Add(new List<string>
            {
                local.ToString("dd/MM/yyyy", Pt), local.ToString("HH:mm", Pt), l.PacienteNome, l.ProfissionalNome,
                $"{l.Conselho} {l.NumeroConselho}/{l.Uf}", l.Corrigido ? "Sim" : "Não", usuario.Administrador && l.OrigemAdministrativa ? (l.IncluidoPorNome ?? "Administrador") : "—"
            });
        }
        await _auditoria.RegistrarAsync("VISUALIZAR", "RELATORIO_PRONTUARIOS", null, null, usuario.Id,
            new { de = j.Inicio, ate = j.Fim, profissional = f.ProfissionalId, unidade = f.UnidadeId, convenio = f.ConvenioId, tipo = j.Tipo, registros = total, textoIncluido = false, origem = "exportacao" },
            con, ct: ct);
        return tabela;
    }
}
