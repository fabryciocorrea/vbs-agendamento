using System.Text;
using Dapper;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Notificacoes;

/// <summary>
/// Avisos internos da recepção (RN07, RN08): o sino da barra do sistema. Quem tem a liberação vê os avisos dirigidos a todos e os
/// dirigidos a ele; o usuário vinculado a profissional só vê os dirigidos a ele. Lido por um, lido por todos. Nada é excluído (RNF05).
/// </summary>
public sealed class NotificacaoService
{
    private static readonly string[] Tipos =
        { "SEM_RESPOSTA_48H", "SEM_RESPOSTA_24H", "HORARIO_DISPONIVEL", "CREDENCIAMENTO", "CONTATO_MANUAL", "RESPOSTA_PACIENTE", "FALHA_ENVIO" };

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public NotificacaoService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private sealed class Linha
    {
        public int Id { get; set; }
        public string Tipo { get; set; } = string.Empty;
        public string Mensagem { get; set; } = string.Empty;
        public DateTime CriadaEm { get; set; }
        public DateTime? LidaEm { get; set; }
        public int? LidaPorId { get; set; }
        public string? LidaPorNome { get; set; }
        public Guid? ConsultaId { get; set; }
        public DateTime? ConsultaInicio { get; set; }
        public Guid? PacienteId { get; set; }
        public string? PacienteNome { get; set; }
    }

    private const string Select = @"
SELECT N.CODIGO_NOTF_INTERNA AS Id, N.TIPO AS Tipo, N.MENSAGEM AS Mensagem, N.DATA_CRIACAO AS CriadaEm, N.DATA_LEITURA AS LidaEm,
       N.USUARIO_LEITURA AS LidaPorId, UL.NOME_COMPLETO AS LidaPorNome,
       C.CODIGO_PUBLIC AS ConsultaId, C.DATA_INICIO AS ConsultaInicio, P.CODIGO_PUBLIC AS PacienteId, P.NOME AS PacienteNome
FROM DBO.NOTIFICACAO_INTERNA N
LEFT JOIN DBO.CONSULTA C ON C.CODIGO_CONSULTA = N.CODIGO_CONSULTA
LEFT JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = C.CODIGO_PACIENTE
LEFT JOIN DBO.USUARIO UL ON UL.CODIGO_USUARIO = N.USUARIO_LEITURA";

    private static NotificacaoInternaDto ParaDto(Linha l) => new()
    {
        Id = l.Id,
        Tipo = l.Tipo,
        Mensagem = l.Mensagem,
        CriadaEm = DataBrasil.Utc(l.CriadaEm),
        Lida = l.LidaEm.HasValue,
        LidaEm = l.LidaEm.HasValue ? DataBrasil.Utc(l.LidaEm.Value) : null,
        LidaPor = l.LidaPorId.HasValue ? new UsuarioReferenciaDto { Id = l.LidaPorId.Value, NomeCompleto = l.LidaPorNome ?? string.Empty } : null,
        Consulta = l.ConsultaId.HasValue && l.ConsultaInicio.HasValue && l.PacienteId.HasValue
            ? new ConsultaNotificacaoDto
            {
                Id = l.ConsultaId.Value,
                Inicio = DataBrasil.Utc(l.ConsultaInicio.Value),
                Paciente = new PacienteNotificacaoDto { Id = l.PacienteId.Value, Nome = l.PacienteNome ?? string.Empty }
            }
            : null
    };

    /// <summary>Quem enxerga o quê: avisos de todos e os dele; o médico só os dele.</summary>
    private static string Visibilidade(UsuarioAtual usuario) =>
        usuario.EhMedico ? "N.CODIGO_USUARIO_DESTINO = @Usuario" : "(N.CODIGO_USUARIO_DESTINO IS NULL OR N.CODIGO_USUARIO_DESTINO = @Usuario)";

    public async Task<NotificacaoInternaPaginaDto> ListarAsync(FiltroNotificacao f, UsuarioAtual usuario, CancellationToken ct)
    {
        var e = new List<ErroCampo>();
        if (f.Pagina < 1) e.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) e.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (!string.IsNullOrWhiteSpace(f.Tipo) && !Tipos.Contains(f.Tipo)) e.Add(new ErroCampo("tipo", "Tipo inválido"));
        if (e.Count > 0) throw Erro.Validacao(e);

        var p = new DynamicParameters();
        p.Add("Usuario", usuario.Id);
        var onde = new StringBuilder(" WHERE " + Visibilidade(usuario));
        if (!string.IsNullOrWhiteSpace(f.Tipo)) { onde.Append(" AND N.TIPO = @Tipo"); p.Add("Tipo", f.Tipo); }
        if (f.Lida == true) onde.Append(" AND N.DATA_LEITURA IS NOT NULL");
        if (f.Lida == false) onde.Append(" AND N.DATA_LEITURA IS NULL");
        if (f.ConsultaId.HasValue) { onde.Append(" AND C.CODIGO_PUBLIC = @ConsultaId"); p.Add("ConsultaId", f.ConsultaId.Value); }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.NOTIFICACAO_INTERNA N LEFT JOIN DBO.CONSULTA C ON C.CODIGO_CONSULTA = N.CODIGO_CONSULTA" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(
            Select + onde + @"
ORDER BY N.DATA_CRIACAO DESC, N.CODIGO_NOTF_INTERNA DESC
OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new NotificacaoInternaPaginaDto { Itens = linhas.Select(ParaDto).ToList(), Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total };
    }

    public async Task<ContagemNaoLidasDto> ContarNaoLidasAsync(UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var porTipo = (await con.QueryAsync<ContagemTipoDto>(Sql.Cmd(
            "SELECT N.TIPO AS Tipo, CAST(COUNT(1) AS INT) AS Total FROM DBO.NOTIFICACAO_INTERNA N WHERE N.DATA_LEITURA IS NULL AND " + Visibilidade(usuario) + " GROUP BY N.TIPO ORDER BY N.TIPO;",
            new { Usuario = usuario.Id }, ct: ct))).ToList();
        return new ContagemNaoLidasDto { Total = porTipo.Sum(x => x.Total), PorTipo = porTipo };
    }

    public async Task<NotificacaoInternaDto> MarcarLidaAsync(int id, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.NOTIFICACAO_INTERNA N WHERE N.CODIGO_NOTF_INTERNA = @Id AND " + Visibilidade(usuario) + ";",
            new { Id = id, Usuario = usuario.Id }, ct: ct));
        if (existe == 0) throw Erro.NaoEncontrado("Notificação não encontrada.");

        // Só a primeira leitura vale: quem já leu e a hora não mudam se outro marcar de novo.
        var alteradas = await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.NOTIFICACAO_INTERNA SET DATA_LEITURA = SYSUTCDATETIME(), USUARIO_LEITURA = @Usuario
WHERE CODIGO_NOTF_INTERNA = @Id AND DATA_LEITURA IS NULL;", new { Id = id, Usuario = usuario.Id }, ct: ct));
        if (alteradas > 0)
            await _auditoria.RegistrarAsync("EDITAR", "NOTIFICACAO_INTERNA", id, null, usuario.Id, new { acao = "marcar-lida" }, con, null, ct);

        var linha = await con.QuerySingleAsync<Linha>(Sql.Cmd(Select + " WHERE N.CODIGO_NOTF_INTERNA = @Id;", new { Id = id }, ct: ct));
        return ParaDto(linha);
    }
}
