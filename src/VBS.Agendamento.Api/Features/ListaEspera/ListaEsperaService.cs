using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Consultas;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.ListaEspera;

/// <summary>
/// Lista de espera (RN08): fila por profissional ou especialidade. Quando uma consulta é cancelada, a procedure de status avisa a
/// recepção e marca como NOTIFICADO os itens que combinam. Aqui a recepção inclui, encerra (ATENDIDO ou CANCELADO) e devolve à fila.
/// </summary>
public sealed class ListaEsperaService
{
    private const string NaoPreenchido = "Campo Não Preenchido";
    private static readonly string[] Situacoes = { "AGUARDANDO", "NOTIFICADO", "ATENDIDO", "CANCELADO" };
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public ListaEsperaService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private sealed class Linha
    {
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid PacienteId { get; set; }
        public string PacienteNome { get; set; } = string.Empty;
        public int? ProfissionalId { get; set; }
        public string? ProfissionalNome { get; set; }
        public int? EspecialidadeId { get; set; }
        public string? EspecialidadeNome { get; set; }
        public int? UnidadeId { get; set; }
        public string? UnidadeNome { get; set; }
        public string? Observacao { get; set; }
        public DateTime? NotificadoEm { get; set; }
        public DateTime CriadoEm { get; set; }
        public int CriadoPorId { get; set; }
        public string? CriadoPorNome { get; set; }
        public int PacienteCodigo { get; set; }
    }

    private sealed class PacienteBusca
    {
        public int Codigo { get; set; }
        public bool Ativo { get; set; }
    }

    private const string Select = @"
SELECT L.CODIGO_LISTA AS Id, L.STATUS AS Status, P.CODIGO_PUBLIC AS PacienteId, P.NOME AS PacienteNome, P.CODIGO_PACIENTE AS PacienteCodigo,
       L.CODIGO_PROFISSIONAL AS ProfissionalId, PR.NOME AS ProfissionalNome,
       L.CODIGO_ESPECIALIDADE AS EspecialidadeId, E.NOME AS EspecialidadeNome,
       L.CODIGO_UNIDADE AS UnidadeId, U.NOME_FANTASIA AS UnidadeNome,
       L.OBSERVACAO AS Observacao, L.DATA_NOTIFICACAO AS NotificadoEm, L.DATA_CRIACAO AS CriadoEm,
       L.USUARIO_CRIACAO AS CriadoPorId, UC.NOME_COMPLETO AS CriadoPorNome
FROM DBO.LISTA_ESPERA L
JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = L.CODIGO_PACIENTE
LEFT JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = L.CODIGO_PROFISSIONAL
LEFT JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = L.CODIGO_ESPECIALIDADE
LEFT JOIN DBO.UNIDADE U ON U.CODIGO_UNIDADE = L.CODIGO_UNIDADE
LEFT JOIN DBO.USUARIO UC ON UC.CODIGO_USUARIO = L.USUARIO_CRIACAO";

    private static ListaEsperaDto ParaDto(Linha l) => new()
    {
        Id = l.Id,
        Status = l.Status,
        Paciente = new PacienteReferenciaDto { Id = l.PacienteId, Nome = l.PacienteNome },
        Profissional = l.ProfissionalId.HasValue ? new ReferenciaDto { Id = l.ProfissionalId.Value, Nome = l.ProfissionalNome ?? string.Empty } : null,
        Especialidade = l.EspecialidadeId.HasValue ? new ReferenciaDto { Id = l.EspecialidadeId.Value, Nome = l.EspecialidadeNome ?? string.Empty } : null,
        Unidade = l.UnidadeId.HasValue ? new ReferenciaDto { Id = l.UnidadeId.Value, Nome = l.UnidadeNome ?? string.Empty } : null,
        Observacao = l.Observacao,
        NotificadoEm = l.NotificadoEm.HasValue ? DataBrasil.Utc(l.NotificadoEm.Value) : null,
        CriadoEm = DataBrasil.Utc(l.CriadoEm),
        CriadoPor = new UsuarioReferenciaDto { Id = l.CriadoPorId, NomeCompleto = l.CriadoPorNome ?? string.Empty }
    };

    // ---------------------------------------------------------------- lista
    public async Task<ListaEsperaPaginaDto> ListarAsync(FiltroListaEspera f, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (!string.IsNullOrWhiteSpace(f.Status) && !Situacoes.Contains(f.Status)) erros.Add(new ErroCampo("status", "Status inválido"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");
        // RN16: o médico só vê pacientes dele.
        if (usuario.EhMedico) { onde.Append(" AND P.CODIGO_PROFISSIONAL_RESPONSAVEL = @Medico"); p.Add("Medico", usuario.ProfissionalId); }
        if (!string.IsNullOrWhiteSpace(f.Status)) { onde.Append(" AND L.STATUS = @Status"); p.Add("Status", f.Status); }
        if (f.ProfissionalId.HasValue) { onde.Append(" AND L.CODIGO_PROFISSIONAL = @ProfissionalId"); p.Add("ProfissionalId", f.ProfissionalId.Value); }
        if (f.EspecialidadeId.HasValue) { onde.Append(" AND L.CODIGO_ESPECIALIDADE = @EspecialidadeId"); p.Add("EspecialidadeId", f.EspecialidadeId.Value); }
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            onde.Append(" AND P.NOME LIKE @Busca");
            p.Add("Busca", "%" + Formatos.EscaparLike(f.Busca.Trim()) + "%");
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.LISTA_ESPERA L JOIN DBO.PACIENTE P ON P.CODIGO_PACIENTE = L.CODIGO_PACIENTE" + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        // Em aberto primeiro (avisados antes dos que aguardam); dentro de cada grupo, quem entrou antes vem antes (fila).
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(
            Select + onde + @"
ORDER BY CASE L.STATUS WHEN 'NOTIFICADO' THEN 0 WHEN 'AGUARDANDO' THEN 1 ELSE 2 END, L.DATA_CRIACAO, L.CODIGO_LISTA
OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new ListaEsperaPaginaDto { Itens = linhas.Select(ParaDto).ToList(), Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total };
    }

    // ------------------------------------------------------------- inclusão
    public async Task<ListaEsperaDto> IncluirAsync(ListaEsperaEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var e = new List<ErroCampo>();
        if (d.PacienteId is null) e.Add(new ErroCampo("pacienteId", NaoPreenchido));
        if (d.ProfissionalId is null && d.EspecialidadeId is null)
        {
            e.Add(new ErroCampo("profissionalId", "Informe o profissional ou a especialidade"));
            e.Add(new ErroCampo("especialidadeId", "Informe o profissional ou a especialidade"));
        }
        var observacao = string.IsNullOrWhiteSpace(d.Observacao) ? null : d.Observacao.Trim();
        if (observacao is { Length: > 250 }) e.Add(new ErroCampo("observacao", "Máximo de 250 caracteres"));
        if (e.Count > 0) throw Erro.Validacao(e);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        // RN16: para o médico, paciente de outro profissional "não existe".
        var paciente = await con.QueryFirstOrDefaultAsync<PacienteBusca>(Sql.Cmd(
            "SELECT CODIGO_PACIENTE AS Codigo, ATIVO AS Ativo FROM DBO.PACIENTE WHERE CODIGO_PUBLIC = @Id" +
            (usuario.EhMedico ? " AND CODIGO_PROFISSIONAL_RESPONSAVEL = @Medico" : "") + ";",
            new { Id = d.PacienteId, Medico = usuario.ProfissionalId }, tx, ct))
            ?? throw Erro.NaoEncontrado("Paciente não encontrado.");
        if (!paciente.Ativo) throw Erro.Validacao("pacienteId", "O paciente está inativo");

        if (d.ProfissionalId.HasValue)
        {
            var ativo = await con.ExecuteScalarAsync<bool?>(Sql.Cmd(
                "SELECT ATIVO FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id;", new { Id = d.ProfissionalId }, tx, ct));
            if (ativo is null) throw Erro.Validacao("profissionalId", "Profissional não encontrado");
            if (ativo == false) throw Erro.Validacao("profissionalId", "O profissional está inativo");
        }
        if (d.EspecialidadeId.HasValue)
        {
            var ativo = await con.ExecuteScalarAsync<bool?>(Sql.Cmd(
                "SELECT ATIVO FROM DBO.ESPECIALIDADE WHERE CODIGO_ESPECIALIDADE = @Id;", new { Id = d.EspecialidadeId }, tx, ct));
            if (ativo is null) throw Erro.Validacao("especialidadeId", "Especialidade não encontrada");
            if (ativo == false) throw Erro.Validacao("especialidadeId", "A especialidade está inativa");
        }
        if (d.UnidadeId.HasValue)
        {
            var ativo = await con.ExecuteScalarAsync<bool?>(Sql.Cmd(
                "SELECT ATIVO FROM DBO.UNIDADE WHERE CODIGO_UNIDADE = @Id;", new { Id = d.UnidadeId }, tx, ct));
            if (ativo is null) throw Erro.Validacao("unidadeId", "Unidade não encontrada");
            if (ativo == false) throw Erro.Validacao("unidadeId", "A unidade está inativa");
        }
        if (d.ProfissionalId.HasValue && d.EspecialidadeId.HasValue)
        {
            var atende = await con.ExecuteScalarAsync<int>(Sql.Cmd(
                "SELECT COUNT(1) FROM DBO.PROFISSIONAL_ESPECIALIDADE WHERE CODIGO_PROFISSIONAL = @P AND CODIGO_ESPECIALIDADE = @E;",
                new { P = d.ProfissionalId, E = d.EspecialidadeId }, tx, ct));
            if (atende == 0) throw Erro.Regra("PROFISSIONAL_NAO_ATENDE", "O profissional não atende a especialidade escolhida.");
        }

        // O mesmo paciente não entra duas vezes na mesma fila em aberto.
        var duplicado = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.LISTA_ESPERA
WHERE CODIGO_PACIENTE = @Pac AND STATUS IN ('AGUARDANDO', 'NOTIFICADO')
  AND ISNULL(CODIGO_PROFISSIONAL, 0) = ISNULL(@Prof, 0) AND ISNULL(CODIGO_ESPECIALIDADE, 0) = ISNULL(@Esp, 0) AND ISNULL(CODIGO_UNIDADE, 0) = ISNULL(@Uni, 0);",
            new { Pac = paciente.Codigo, Prof = d.ProfissionalId, Esp = d.EspecialidadeId, Uni = d.UnidadeId }, tx, ct));
        if (duplicado > 0)
            throw Erro.Conflito("O paciente já está na lista de espera para este profissional, especialidade e unidade.", "LISTA_ESPERA_DUPLICADA");

        var id = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.LISTA_ESPERA (CODIGO_PACIENTE, CODIGO_PROFISSIONAL, CODIGO_ESPECIALIDADE, CODIGO_UNIDADE, STATUS, OBSERVACAO, USUARIO_CRIACAO)
VALUES (@Pac, @Prof, @Esp, @Uni, 'AGUARDANDO', @Obs, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new { Pac = paciente.Codigo, Prof = d.ProfissionalId, Esp = d.EspecialidadeId, Uni = d.UnidadeId, Obs = observacao, Usuario = usuario.Id }, tx, ct));

        await _auditoria.RegistrarAsync("INCLUIR", "LISTA_ESPERA", id, paciente.Codigo, usuario.Id, null, con, tx, ct);

        var linha = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Item recém-criado não encontrado.");
        await tx.CommitAsync(ct);
        return ParaDto(linha);
    }

    // ---------------------------------------------------------------- status
    public async Task<ListaEsperaDto> AlterarStatusAsync(int id, ListaEsperaStatusEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null || string.IsNullOrWhiteSpace(d.Status)) throw Erro.Validacao("status", NaoPreenchido);
        if (d.Status is not ("ATENDIDO" or "CANCELADO" or "AGUARDANDO"))
            throw Erro.Validacao("status", "Informe ATENDIDO, CANCELADO ou AGUARDANDO");

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var atual = await CarregarAsync(con, tx, id, ct);
        // RN16: para o médico, item de paciente de outro profissional "não existe".
        if (atual is null) throw Erro.NaoEncontrado("Item da lista de espera não encontrado.");
        if (usuario.EhMedico)
        {
            var dele = await con.ExecuteScalarAsync<int>(Sql.Cmd(
                "SELECT COUNT(1) FROM DBO.PACIENTE WHERE CODIGO_PACIENTE = @P AND CODIGO_PROFISSIONAL_RESPONSAVEL = @M;",
                new { P = atual.PacienteCodigo, M = usuario.ProfissionalId }, tx, ct));
            if (dele == 0) throw Erro.NaoEncontrado("Item da lista de espera não encontrado.");
        }

        if (atual.Status is "ATENDIDO" or "CANCELADO")
            throw Erro.Conflito("O item já foi encerrado.", "LISTA_ESPERA_ENCERRADA");
        if (d.Status == "AGUARDANDO" && atual.Status != "NOTIFICADO")
            throw Erro.Conflito("Só um item já avisado pode voltar para a fila.", "TRANSICAO_INVALIDA");
        if (d.Status == atual.Status) throw Erro.Conflito("O item já está nessa situação.", "TRANSICAO_INVALIDA");

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.LISTA_ESPERA
SET STATUS = @Status, DATA_NOTIFICACAO = CASE WHEN @Status = 'AGUARDANDO' THEN NULL ELSE DATA_NOTIFICACAO END
WHERE CODIGO_LISTA = @Id;", new { Id = id, d.Status }, tx, ct));
        await _auditoria.RegistrarAsync("EDITAR", "LISTA_ESPERA", id, atual.PacienteCodigo, usuario.Id, new { de = atual.Status, para = d.Status }, con, tx, ct);

        var nova = await CarregarAsync(con, tx, id, ct) ?? throw new InvalidOperationException("Item não encontrado após a alteração.");
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    private static Task<Linha?> CarregarAsync(SqlConnection con, SqlTransaction tx, int id, CancellationToken ct) =>
        con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(Select + " WHERE L.CODIGO_LISTA = @Id;", new { Id = id }, tx, ct));
}
