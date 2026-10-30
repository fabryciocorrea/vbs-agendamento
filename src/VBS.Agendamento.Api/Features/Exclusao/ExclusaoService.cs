using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Exclusao;

/// <summary>
/// Exclusão definitiva de cadastros, só para o administrador. Só apaga o que não tem histórico: se houver consulta, prontuário,
/// lista de espera ou outro vínculo, responde 409 REGISTRO_COM_VINCULOS e o caminho é Inativar. Vínculos de cadastro (por exemplo,
/// as especialidades de um profissional) são removidos junto. Cada exclusão fica no log de auditoria (ação EXCLUIR).
/// </summary>
public sealed class ExclusaoService
{
    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public ExclusaoService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    /// <summary>Algo que impede a exclusão: descrição e consulta que devolve a quantidade (usa @Id).</summary>
    private sealed record Impedimento(string Descricao, string SqlContagem);

    private static void ExigirAdministrador(UsuarioAtual usuario)
    {
        if (!usuario.Administrador)
            throw Erro.SemPermissao("Somente o administrador pode excluir cadastros.", "EXCLUSAO_SOMENTE_ADMINISTRADOR");
    }

    private static ApiException ComVinculos(string este, string? detalhe = null) =>
        Erro.Conflito(
            detalhe is null
                ? $"Não é possível excluir {este}: ele tem vínculos ou histórico. Use Inativar para tirá-lo de uso."
                : $"Não é possível excluir {este}: há {detalhe}. Use Inativar para tirá-lo de uso.",
            "REGISTRO_COM_VINCULOS");

    // ------------------------------------------------------------- motor comum
    private async Task ExcluirAsync(
        UsuarioAtual usuario, string entidade, string este, string naoEncontrado, int id, object parametros,
        string sqlExiste, IReadOnlyList<Impedimento> impedimentos, IReadOnlyList<string> filhos, string sqlExcluir,
        object? detalhes, CancellationToken ct)
    {
        ExigirAdministrador(usuario);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(sqlExiste, parametros, tx, ct));
        if (existe == 0) throw Erro.NaoEncontrado(naoEncontrado);

        var bloqueios = new List<string>();
        foreach (var imp in impedimentos)
        {
            var qtd = await con.ExecuteScalarAsync<int>(Sql.Cmd(imp.SqlContagem, parametros, tx, ct));
            if (qtd > 0) bloqueios.Add($"{qtd} {imp.Descricao}");
        }
        if (bloqueios.Count > 0) throw ComVinculos(este, string.Join(", ", bloqueios));

        try
        {
            foreach (var sql in filhos) await con.ExecuteAsync(Sql.Cmd(sql, parametros, tx, ct));
            await con.ExecuteAsync(Sql.Cmd(sqlExcluir, parametros, tx, ct));
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            throw ComVinculos(este);
        }

        await _auditoria.RegistrarAsync("EXCLUIR", entidade, id, null, usuario.Id, detalhes, con, tx, ct);
        await tx.CommitAsync(ct);
    }

    // ---------------------------------------------------------------- paciente
    public async Task ExcluirPacienteAsync(Guid publicId, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirAdministrador(usuario);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var p = await con.QueryFirstOrDefaultAsync<PacienteLinha>(Sql.Cmd(
            "SELECT CODIGO_PACIENTE AS Id, CODIGO_RESPONSAVEL_LEGAL AS ResponsavelId FROM DBO.PACIENTE WHERE CODIGO_PUBLIC = @Publico;",
            new { Publico = publicId }, tx, ct)) ?? throw Erro.NaoEncontrado("Paciente não encontrado.");

        var bloqueios = new List<string>();
        var consultas = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.CONSULTA WHERE CODIGO_PACIENTE = @Id;", new { p.Id }, tx, ct));
        if (consultas > 0) bloqueios.Add($"{consultas} consulta(s)");
        var prontuario = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.REGISTRO_PRONTUARIO WHERE CODIGO_PACIENTE = @Id;", new { p.Id }, tx, ct));
        if (prontuario > 0) bloqueios.Add($"{prontuario} registro(s) de prontuário");
        var espera = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.LISTA_ESPERA WHERE CODIGO_PACIENTE = @Id;", new { p.Id }, tx, ct));
        if (espera > 0) bloqueios.Add($"{espera} item(ns) na lista de espera");
        if (bloqueios.Count > 0) throw ComVinculos("este paciente", string.Join(", ", bloqueios));

        try
        {
            await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.PACIENTE_PROFISSIONAL_HISTORICO WHERE CODIGO_PACIENTE = @Id;", new { p.Id }, tx, ct));
            await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.PACIENTE WHERE CODIGO_PACIENTE = @Id;", new { p.Id }, tx, ct));

            // O responsável legal só sai se nenhum outro paciente o usa.
            if (p.ResponsavelId.HasValue)
                await con.ExecuteAsync(Sql.Cmd(@"
DELETE FROM DBO.RESPONSAVEL_LEGAL
WHERE CODIGO_RESPONSAVEL = @R AND NOT EXISTS (SELECT 1 FROM DBO.PACIENTE WHERE CODIGO_RESPONSAVEL_LEGAL = @R);",
                    new { R = p.ResponsavelId.Value }, tx, ct));
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            throw ComVinculos("este paciente");
        }

        await _auditoria.RegistrarAsync("EXCLUIR", "PACIENTE", p.Id, null, usuario.Id, new { publicId }, con, tx, ct);
        await tx.CommitAsync(ct);
    }

    private sealed class PacienteLinha
    {
        public int Id { get; set; }
        public int? ResponsavelId { get; set; }
    }

    // ----------------------------------------------------------------- convênio
    public Task ExcluirConvenioAsync(int id, UsuarioAtual usuario, CancellationToken ct) =>
        ExcluirAsync(usuario, "CONVENIO", "este convênio", "Convênio não encontrado.", id, new { Id = id },
            "SELECT COUNT(1) FROM DBO.CONVENIO WHERE CODIGO_CONVENIO = @Id;",
            new[]
            {
                new Impedimento("paciente(s)", "SELECT COUNT(1) FROM DBO.PACIENTE WHERE CODIGO_CONVENIO = @Id;"),
                new Impedimento("consulta(s)", "SELECT COUNT(1) FROM DBO.CONSULTA WHERE CODIGO_CONVENIO = @Id;")
            },
            new[]
            {
                "DELETE FROM DBO.CREDENCIAMENTO_CONVENIO WHERE CODIGO_CONVENIO = @Id;",
                "DELETE FROM DBO.CONVENIO_ESPECIALIDADE WHERE CODIGO_CONVENIO = @Id;",
                "DELETE FROM DBO.FORMA_PAGAMENTO WHERE CODIGO_CONVENIO = @Id;"
            },
            "DELETE FROM DBO.CONVENIO WHERE CODIGO_CONVENIO = @Id;", null, ct);

    // ------------------------------------------------------------ credenciamento
    public async Task ExcluirCredenciamentoAsync(int convenioId, int id, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirAdministrador(usuario);

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        var existe = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.CREDENCIAMENTO_CONVENIO WHERE CODIGO_CREDENCIAMENTO = @Id AND CODIGO_CONVENIO = @Convenio;",
            new { Id = id, Convenio = convenioId }, tx, ct));
        if (existe == 0) throw Erro.NaoEncontrado("Credenciamento não encontrado.");

        // Nenhuma consulta guarda o código do credenciamento; consultas futuras na combinação passam a aparecer como sem credenciamento.
        await con.ExecuteAsync(Sql.Cmd("DELETE FROM DBO.CREDENCIAMENTO_CONVENIO WHERE CODIGO_CREDENCIAMENTO = @Id;", new { Id = id }, tx, ct));
        await _auditoria.RegistrarAsync("EXCLUIR", "CREDENCIAMENTO_CONVENIO", id, null, usuario.Id, new { convenioId }, con, tx, ct);
        await tx.CommitAsync(ct);
    }

    // -------------------------------------------------------------- profissional
    public Task ExcluirProfissionalAsync(int id, UsuarioAtual usuario, CancellationToken ct) =>
        ExcluirAsync(usuario, "PROFISSIONAL", "este profissional", "Profissional não encontrado.", id, new { Id = id },
            "SELECT COUNT(1) FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id;",
            new[]
            {
                new Impedimento("consulta(s)", "SELECT COUNT(1) FROM DBO.CONSULTA WHERE CODIGO_PROFISSIONAL = @Id;"),
                new Impedimento("registro(s) de prontuário", "SELECT COUNT(1) FROM DBO.REGISTRO_PRONTUARIO WHERE CODIGO_PROFISSIONAL = @Id;"),
                new Impedimento("paciente(s) como responsável", "SELECT COUNT(1) FROM DBO.PACIENTE WHERE CODIGO_PROFISSIONAL_RESPONSAVEL = @Id;"),
                new Impedimento("histórico de responsável por paciente", "SELECT COUNT(1) FROM DBO.PACIENTE_PROFISSIONAL_HISTORICO WHERE CODIGO_PROFISSIONAL = @Id;"),
                new Impedimento("item(ns) na lista de espera", "SELECT COUNT(1) FROM DBO.LISTA_ESPERA WHERE CODIGO_PROFISSIONAL = @Id;"),
                new Impedimento("usuário(s) vinculado(s)", "SELECT COUNT(1) FROM DBO.USUARIO WHERE CODIGO_PROFISSIONAL = @Id;")
            },
            new[]
            {
                "DELETE FROM DBO.PROFISSIONAL_HORARIO WHERE CODIGO_PROFISSIONAL = @Id;",
                "DELETE FROM DBO.PROFISSIONAL_ESPECIALIDADE WHERE CODIGO_PROFISSIONAL = @Id;",
                "DELETE FROM DBO.PROFISSIONAL_UNIDADE WHERE CODIGO_PROFISSIONAL = @Id;"
            },
            "DELETE FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id;", null, ct);

    // --------------------------------------------------------------- colaborador
    public Task ExcluirColaboradorAsync(int id, UsuarioAtual usuario, CancellationToken ct) =>
        ExcluirAsync(usuario, "COLABORADOR_PRESTADOR", "este colaborador", "Colaborador não encontrado.", id, new { Id = id },
            "SELECT COUNT(1) FROM DBO.COLABORADOR_PRESTADOR WHERE CODIGO_CLB_PRT = @Id;",
            Array.Empty<Impedimento>(), Array.Empty<string>(),
            "DELETE FROM DBO.COLABORADOR_PRESTADOR WHERE CODIGO_CLB_PRT = @Id;", null, ct);

    // -------------------------------------------------------------- especialidade
    public Task ExcluirEspecialidadeAsync(int id, UsuarioAtual usuario, CancellationToken ct) =>
        ExcluirAsync(usuario, "ESPECIALIDADE", "esta especialidade", "Especialidade não encontrada.", id, new { Id = id },
            "SELECT COUNT(1) FROM DBO.ESPECIALIDADE WHERE CODIGO_ESPECIALIDADE = @Id;",
            new[]
            {
                new Impedimento("consulta(s)", "SELECT COUNT(1) FROM DBO.CONSULTA WHERE CODIGO_ESPECIALIDADE = @Id;"),
                new Impedimento("item(ns) na lista de espera", "SELECT COUNT(1) FROM DBO.LISTA_ESPERA WHERE CODIGO_ESPECIALIDADE = @Id;")
            },
            new[]
            {
                "DELETE FROM DBO.CREDENCIAMENTO_CONVENIO WHERE CODIGO_ESPECIALIDADE = @Id;",
                "DELETE FROM DBO.CONVENIO_ESPECIALIDADE WHERE CODIGO_ESPECIALIDADE = @Id;",
                "DELETE FROM DBO.PROFISSIONAL_ESPECIALIDADE WHERE CODIGO_ESPECIALIDADE = @Id;"
            },
            "DELETE FROM DBO.ESPECIALIDADE WHERE CODIGO_ESPECIALIDADE = @Id;", null, ct);

    // ------------------------------------------------------- forma de pagamento
    public Task ExcluirFormaPagamentoAsync(int id, UsuarioAtual usuario, CancellationToken ct) =>
        ExcluirAsync(usuario, "FORMA_PAGAMENTO", "esta forma de pagamento", "Forma de pagamento não encontrada.", id, new { Id = id },
            "SELECT COUNT(1) FROM DBO.FORMA_PAGAMENTO WHERE CODIGO_PAGAMENTO = @Id;",
            new[]
            {
                new Impedimento("consulta(s)", "SELECT COUNT(1) FROM DBO.CONSULTA WHERE CODIGO_PAGAMENTO = @Id;"),
                new Impedimento("convênio(s) ligado(s) a ela (exclua o convênio, ou use Inativar)", "SELECT COUNT(1) FROM DBO.FORMA_PAGAMENTO WHERE CODIGO_PAGAMENTO = @Id AND CODIGO_CONVENIO IS NOT NULL;")
            },
            Array.Empty<string>(),
            "DELETE FROM DBO.FORMA_PAGAMENTO WHERE CODIGO_PAGAMENTO = @Id;", null, ct);

    // ------------------------------------------------------------------- unidade
    public Task ExcluirUnidadeAsync(int id, UsuarioAtual usuario, CancellationToken ct) =>
        ExcluirAsync(usuario, "UNIDADE", "esta unidade", "Unidade não encontrada.", id, new { Id = id },
            "SELECT COUNT(1) FROM DBO.UNIDADE WHERE CODIGO_UNIDADE = @Id;",
            new[]
            {
                new Impedimento("consulta(s)", "SELECT COUNT(1) FROM DBO.CONSULTA WHERE CODIGO_UNIDADE = @Id;"),
                new Impedimento("item(ns) na lista de espera", "SELECT COUNT(1) FROM DBO.LISTA_ESPERA WHERE CODIGO_UNIDADE = @Id;")
            },
            new[]
            {
                "DELETE FROM DBO.CREDENCIAMENTO_CONVENIO WHERE CODIGO_UNIDADE = @Id;",
                "DELETE FROM DBO.PROFISSIONAL_HORARIO WHERE CODIGO_UNIDADE = @Id;",
                "DELETE FROM DBO.PROFISSIONAL_UNIDADE WHERE CODIGO_UNIDADE = @Id;",
                "DELETE FROM DBO.SALA WHERE CODIGO_UNIDADE = @Id;"
            },
            "DELETE FROM DBO.UNIDADE WHERE CODIGO_UNIDADE = @Id;", null, ct);

    // ---------------------------------------------------------------------- sala
    public Task ExcluirSalaAsync(int id, UsuarioAtual usuario, CancellationToken ct) =>
        ExcluirAsync(usuario, "SALA", "esta sala", "Sala não encontrada.", id, new { Id = id },
            "SELECT COUNT(1) FROM DBO.SALA WHERE CODIGO_SALA = @Id;",
            new[] { new Impedimento("consulta(s)", "SELECT COUNT(1) FROM DBO.CONSULTA WHERE CODIGO_SALA = @Id;") },
            Array.Empty<string>(),
            "DELETE FROM DBO.SALA WHERE CODIGO_SALA = @Id;", null, ct);

    // ------------------------------------------------------------------- usuário
    public async Task ExcluirUsuarioAsync(int id, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirAdministrador(usuario);
        if (usuario.Id == id)
            throw Erro.Regra("NAO_EXCLUIR_PROPRIO_USUARIO", "Você não pode excluir o seu próprio usuário.");

        await using (var con = await _db.AbrirAsync(ct))
        {
            var protegido = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
SELECT COUNT(1) FROM DBO.USUARIO U JOIN DBO.PERFIL_ACESSO P ON P.CODIGO_PERFIL = U.CODIGO_PERFIL
WHERE U.CODIGO_USUARIO = @Id AND P.PROTEGIDO = 1;", new { Id = id }, ct: ct));
            if (protegido > 0)
                throw Erro.Regra("USUARIO_ADMINISTRADOR_PROTEGIDO", "Usuário do perfil Administrador não pode ser excluído. Use Inativar.");
        }

        // Quem já registrou algo no sistema (consultas, pacientes, log) fica com vínculos e deve ser inativado.
        await ExcluirAsync(usuario, "USUARIO", "este usuário", "Usuário não encontrado.", id, new { Id = id },
            "SELECT COUNT(1) FROM DBO.USUARIO WHERE CODIGO_USUARIO = @Id;",
            new[] { new Impedimento("registro(s) no log de acessos", "SELECT COUNT(1) FROM DBO.LOG_AUDITORIA WHERE CODIGO_USUARIO = @Id;") },
            new[]
            {
                "DELETE FROM DBO.TOKEN_RECUPERACAO_SENHA WHERE CODIGO_USUARIO = @Id;",
                "DELETE FROM DBO.SESSAO_USUARIO WHERE CODIGO_USUARIO = @Id;"
            },
            "DELETE FROM DBO.USUARIO WHERE CODIGO_USUARIO = @Id;", null, ct);
    }
}
