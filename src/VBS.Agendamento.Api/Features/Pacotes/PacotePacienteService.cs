using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Consultas;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Pacotes;

/// <summary>Condições de uma consulta que consome sessão de um pacote (valor da sessão, forma e situação do pagamento).</summary>
public sealed record UsoPacote(int CodigoPacotePaciente, decimal ValorSessao, int FormaPagamentoId, bool PagamentoConfirmado);

/// <summary>
/// Pacotes vendidos a pacientes (consultas particulares). Permissão: pacote.venda. As sessões usadas não são guardadas:
/// contam-se as consultas ligadas ao pacote que não estão canceladas nem remarcadas. Por isso, cancelar uma consulta devolve a
/// sessão sozinho, e a falta consome a sessão. O profissional vinculado só vê pacotes de pacientes de que é o responsável (RN16, RN49).
/// </summary>
public sealed class PacotePacienteService
{
    private const string NaoPreenchido = "Campo Não Preenchido";

    private readonly IConexaoFactory _db;
    private readonly IAuditoria _auditoria;

    public PacotePacienteService(IConexaoFactory db, IAuditoria auditoria)
    {
        _db = db;
        _auditoria = auditoria;
    }

    private const string Usadas =
        "(SELECT COUNT(1) FROM DBO.CONSULTA CU WHERE CU.CODIGO_PACOTE_PACIENTE = PP.CODIGO_PACOTE_PACIENTE AND CU.STATUS NOT IN ('CANCELADA','REMARCADA'))";

    private const string De = @"
FROM DBO.PACOTE_PACIENTE PP
JOIN DBO.PACIENTE PA ON PA.CODIGO_PACIENTE = PP.CODIGO_PACIENTE
JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = PP.CODIGO_ESPECIALIDADE
JOIN DBO.FORMA_PAGAMENTO FP ON FP.CODIGO_PAGAMENTO = PP.CODIGO_PAGAMENTO
LEFT JOIN DBO.USUARIO UV ON UV.CODIGO_USUARIO = PP.VENDA_USUARIO";

    private const string SelectPacote = @"
SELECT PP.CODIGO_PACOTE_PACIENTE AS Codigo, PP.CODIGO_PUBLIC AS Id, PP.NOME AS Nome,
       PA.CODIGO_PUBLIC AS PacienteId, PA.NOME AS PacienteNome, PA.CODIGO_PROFISSIONAL_RESPONSAVEL AS Responsavel,
       PP.CODIGO_ESPECIALIDADE AS EspecialidadeId, E.NOME AS EspecialidadeNome,
       PP.QUANTIDADE_SESSOES AS QuantidadeSessoes, " + Usadas + @" AS SessoesUsadas,
       PP.VALOR AS Valor, PP.VALOR_SESSAO AS ValorSessao, PP.DATA_INICIO AS DataInicio, PP.DATA_VALIDADE AS DataValidade,
       PP.CODIGO_PAGAMENTO AS FormaId, FP.DESCRICAO AS FormaNome,
       PP.PAGAMENTO_CONFIRMADO AS PagamentoConfirmado, PP.DATA_PAGAMENTO AS PagamentoConfirmadoEm,
       PP.STATUS AS Status, PP.DATA_VENDA AS VendidoEm, PP.VENDA_USUARIO AS VendidoPorId, UV.NOME_COMPLETO AS VendidoPorNome,
       PP.DATA_CANCELAMENTO AS CanceladoEm, PP.MOTIVO_CANCELAMENTO AS MotivoCancelamento" + De;

    private sealed class Linha
    {
        public int Codigo { get; set; }
        public Guid Id { get; set; }
        public string Nome { get; set; } = string.Empty;
        public Guid PacienteId { get; set; }
        public string PacienteNome { get; set; } = string.Empty;
        public int Responsavel { get; set; }
        public int EspecialidadeId { get; set; }
        public string EspecialidadeNome { get; set; } = string.Empty;
        public int QuantidadeSessoes { get; set; }
        public int SessoesUsadas { get; set; }
        public decimal Valor { get; set; }
        public decimal ValorSessao { get; set; }
        public DateTime DataInicio { get; set; }
        public DateTime DataValidade { get; set; }
        public int FormaId { get; set; }
        public string FormaNome { get; set; } = string.Empty;
        public bool PagamentoConfirmado { get; set; }
        public DateTime? PagamentoConfirmadoEm { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime VendidoEm { get; set; }
        public int? VendidoPorId { get; set; }
        public string? VendidoPorNome { get; set; }
        public DateTime? CanceladoEm { get; set; }
        public string? MotivoCancelamento { get; set; }
    }

    private sealed class PacienteBase
    {
        public int Codigo { get; set; }
        public int ProfissionalResponsavel { get; set; }
    }

    /// <summary>O paciente de outro profissional aparece como não encontrado para o usuário vinculado a um profissional.</summary>
    private static async Task<PacienteBase> ExigirPacienteAsync(SqlConnection con, SqlTransaction? tx, Guid pacienteId, UsuarioAtual usuario, CancellationToken ct)
    {
        var p = await con.QueryFirstOrDefaultAsync<PacienteBase>(Sql.Cmd(
            "SELECT CODIGO_PACIENTE AS Codigo, CODIGO_PROFISSIONAL_RESPONSAVEL AS ProfissionalResponsavel FROM DBO.PACIENTE WHERE CODIGO_PUBLIC = @Id;",
            new { Id = pacienteId }, tx, ct)) ?? throw Erro.NaoEncontrado("Paciente não encontrado.");
        if (!usuario.Administrador && usuario.EhMedico && usuario.ProfissionalId != p.ProfissionalResponsavel)
            throw Erro.NaoEncontrado("Paciente não encontrado.");
        return p;
    }

    private static DateTime HojeComoData() => DataBrasil.Hoje().ToDateTime(TimeOnly.MinValue);

    // ------------------------------------------------------------------- consultas à API
    public async Task<List<PacotePacienteDto>> ListarDoPacienteAsync(Guid pacienteId, bool somenteDisponiveis, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var paciente = await ExigirPacienteAsync(con, null, pacienteId, usuario, ct);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(
            SelectPacote + " WHERE PP.CODIGO_PACIENTE = @Pac ORDER BY PP.DATA_VENDA DESC, PP.CODIGO_PACOTE_PACIENTE DESC;", new { Pac = paciente.Codigo }, ct: ct));
        var lista = linhas.Select(l => ParaDto(l)).ToList();
        return somenteDisponiveis ? lista.Where(p => p.Situacao == "DISPONIVEL").ToList() : lista;
    }

    public async Task<PacotePacientePaginaDto> ListarAsync(FiltroPacotesVendidos f, UsuarioAtual usuario, CancellationToken ct)
    {
        var erros = new List<ErroCampo>();
        if (f.Pagina < 1) erros.Add(new ErroCampo("pagina", "Deve ser 1 ou mais"));
        if (f.Tamanho is < 1 or > 100) erros.Add(new ErroCampo("tamanho", "Deve estar entre 1 e 100"));
        if (!string.IsNullOrWhiteSpace(f.Situacao) && f.Situacao is not ("DISPONIVEL" or "ENCERRADO" or "CANCELADO"))
            erros.Add(new ErroCampo("situacao", "Informe DISPONIVEL, ENCERRADO ou CANCELADO"));
        if (erros.Count > 0) throw Erro.Validacao(erros);

        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");
        p.Add("Hoje", HojeComoData(), System.Data.DbType.Date);
        if (usuario.EhMedico && !usuario.Administrador)
        {
            onde.Append(" AND PA.CODIGO_PROFISSIONAL_RESPONSAVEL = @Medico");
            p.Add("Medico", usuario.ProfissionalId);
        }
        if (!string.IsNullOrWhiteSpace(f.Busca))
        {
            onde.Append(" AND (PA.NOME LIKE @Busca OR PP.NOME LIKE @Busca)");
            p.Add("Busca", "%" + Formatos.EscaparLike(f.Busca.Trim()) + "%");
        }
        if (f.PagamentoPendente == true) onde.Append(" AND PP.STATUS = 'ATIVO' AND PP.PAGAMENTO_CONFIRMADO = 0");
        switch (f.Situacao)
        {
            case "CANCELADO":
                onde.Append(" AND PP.STATUS = 'CANCELADO'");
                break;
            case "DISPONIVEL":
                onde.Append(" AND PP.STATUS = 'ATIVO' AND PP.DATA_VALIDADE >= @Hoje AND PP.QUANTIDADE_SESSOES > " + Usadas);
                break;
            case "ENCERRADO":
                onde.Append(" AND PP.STATUS = 'ATIVO' AND (PP.DATA_VALIDADE < @Hoje OR PP.QUANTIDADE_SESSOES <= " + Usadas + ")");
                break;
        }

        await using var con = await _db.AbrirAsync(ct);
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1)" + De + onde, p, ct: ct));

        p.Add("Deslocamento", (f.Pagina - 1) * f.Tamanho);
        p.Add("Tamanho", f.Tamanho);
        var linhas = await con.QueryAsync<Linha>(Sql.Cmd(
            SelectPacote + onde + " ORDER BY PP.DATA_VENDA DESC, PP.CODIGO_PACOTE_PACIENTE DESC OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct));

        return new PacotePacientePaginaDto { Itens = linhas.Select(l => ParaDto(l)).ToList(), Pagina = f.Pagina, Tamanho = f.Tamanho, Total = total };
    }

    private static async Task<Linha> CarregarAsync(SqlConnection con, SqlTransaction? tx, Guid id, UsuarioAtual usuario, CancellationToken ct)
    {
        var l = await con.QueryFirstOrDefaultAsync<Linha>(Sql.Cmd(SelectPacote + " WHERE PP.CODIGO_PUBLIC = @Id;", new { Id = id }, tx, ct))
            ?? throw Erro.NaoEncontrado("Pacote não encontrado.");
        if (!usuario.Administrador && usuario.EhMedico && usuario.ProfissionalId != l.Responsavel)
            throw Erro.NaoEncontrado("Pacote não encontrado.");
        return l;
    }

    private sealed class SessaoLinha
    {
        public Guid ConsultaId { get; set; }
        public DateTime Inicio { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Profissional { get; set; } = string.Empty;
    }

    public async Task<PacotePacienteDetalheDto> ObterAsync(Guid id, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        var l = await CarregarAsync(con, null, id, usuario, ct);
        var sessoes = await con.QueryAsync<SessaoLinha>(Sql.Cmd(@"
SELECT C.CODIGO_PUBLIC AS ConsultaId, C.DATA_INICIO AS Inicio, C.STATUS AS Status, PR.NOME AS Profissional
FROM DBO.CONSULTA C
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = C.CODIGO_PROFISSIONAL
WHERE C.CODIGO_PACOTE_PACIENTE = @Pacote
ORDER BY C.DATA_INICIO, C.CODIGO_CONSULTA;", new { Pacote = l.Codigo }, ct: ct));

        var d = ParaDto(l, new PacotePacienteDetalheDto());
        d.Sessoes = sessoes.Select(s => new SessaoPacoteDto
        {
            ConsultaId = s.ConsultaId,
            Inicio = DataBrasil.Utc(s.Inicio),
            Status = s.Status,
            Profissional = s.Profissional,
            Consome = s.Status is not ("CANCELADA" or "REMARCADA")
        }).ToList();

        await _auditoria.RegistrarAsync("VISUALIZAR", "PACOTE_PACIENTE", l.Codigo, null, usuario.Id, null, con, ct: ct);
        return d;
    }

    // ------------------------------------------------------------------- venda
    public async Task<PacotePacienteDto> VenderAsync(Guid pacienteId, VendaPacoteEntrada? venda, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        var paciente = await ExigirPacienteAsync(con, tx, pacienteId, usuario, ct);
        var codigo = await VenderNaTransacaoAsync(con, tx, _auditoria, paciente.Codigo, venda, usuario, ct);

        var l = await con.QueryFirstAsync<Linha>(Sql.Cmd(SelectPacote + " WHERE PP.CODIGO_PACOTE_PACIENTE = @C;", new { C = codigo }, tx, ct));
        await tx.CommitAsync(ct);
        return ParaDto(l);
    }

    private sealed class ModeloLinha
    {
        public int Codigo { get; set; }
        public bool Ativo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public int EspecialidadeId { get; set; }
        public int QuantidadeSessoes { get; set; }
        public decimal Valor { get; set; }
        public int ValidadeDias { get; set; }
    }

    private sealed class FormaLinha
    {
        public bool Ativo { get; set; }
        public string Tipo { get; set; } = string.Empty;
    }

    /// <summary>
    /// Grava a venda do pacote dentro da transação de quem chama (a venda avulsa e a venda feita ao agendar a consulta usam
    /// o mesmo código). Guarda uma cópia das condições do modelo; o valor da sessão é o total dividido pela quantidade.
    /// </summary>
    internal static async Task<int> VenderNaTransacaoAsync(SqlConnection con, SqlTransaction tx, IAuditoria auditoria, int codigoPaciente,
        VendaPacoteEntrada? venda, UsuarioAtual usuario, CancellationToken ct)
    {
        if (venda is null) throw Erro.Validacao("vendaPacote", NaoPreenchido);
        var e = new List<ErroCampo>();
        if (venda.PacoteId is null) e.Add(new ErroCampo("vendaPacote.pacoteId", NaoPreenchido));
        if (venda.FormaPagamentoId is null) e.Add(new ErroCampo("vendaPacote.formaPagamentoId", NaoPreenchido));
        if (e.Count > 0) throw Erro.Validacao(e);

        var modelo = await con.QueryFirstOrDefaultAsync<ModeloLinha>(Sql.Cmd(@"
SELECT CODIGO_PACOTE AS Codigo, ATIVO AS Ativo, NOME AS Nome, CODIGO_ESPECIALIDADE AS EspecialidadeId,
       QUANTIDADE_SESSOES AS QuantidadeSessoes, VALOR AS Valor, VALIDADE_DIAS AS ValidadeDias
FROM DBO.PACOTE WHERE CODIGO_PACOTE = @Id;", new { Id = venda.PacoteId }, tx, ct))
            ?? throw Erro.Validacao("vendaPacote.pacoteId", "Pacote não encontrado");
        if (!modelo.Ativo) throw Erro.Validacao("vendaPacote.pacoteId", "O pacote está inativo");

        var forma = await con.QueryFirstOrDefaultAsync<FormaLinha>(Sql.Cmd(
            "SELECT ATIVO AS Ativo, TIPO AS Tipo FROM DBO.FORMA_PAGAMENTO WHERE CODIGO_PAGAMENTO = @Id;", new { Id = venda.FormaPagamentoId }, tx, ct))
            ?? throw Erro.Validacao("vendaPacote.formaPagamentoId", "Forma de pagamento não encontrada");
        if (!forma.Ativo) throw Erro.Validacao("vendaPacote.formaPagamentoId", "A forma de pagamento está inativa");
        if (forma.Tipo == "CONVENIO") throw Erro.Validacao("vendaPacote.formaPagamentoId", "O pacote é particular: não use a forma de pagamento de convênio");

        var inicio = HojeComoData();
        var validade = inicio.AddDays(modelo.ValidadeDias - 1);
        var publico = Guid.NewGuid();
        var codigo = await con.ExecuteScalarAsync<int>(Sql.Cmd(@"
INSERT INTO DBO.PACOTE_PACIENTE (CODIGO_PUBLIC, CODIGO_PACOTE, CODIGO_PACIENTE, NOME, CODIGO_ESPECIALIDADE, QUANTIDADE_SESSOES, VALOR, VALOR_SESSAO,
                                 DATA_INICIO, DATA_VALIDADE, CODIGO_PAGAMENTO, PAGAMENTO_CONFIRMADO, DATA_PAGAMENTO, PAGAMENTO_USUARIO, VENDA_USUARIO)
VALUES (@Publico, @Pacote, @Paciente, @Nome, @Especialidade, @Quantidade, @Valor, @ValorSessao,
        @Inicio, @Validade, @Forma, @Confirmado, CASE WHEN @Confirmado = 1 THEN SYSUTCDATETIME() END, CASE WHEN @Confirmado = 1 THEN @Usuario END, @Usuario);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
            new
            {
                Publico = publico,
                Pacote = modelo.Codigo,
                Paciente = codigoPaciente,
                modelo.Nome,
                Especialidade = modelo.EspecialidadeId,
                Quantidade = modelo.QuantidadeSessoes,
                modelo.Valor,
                ValorSessao = PacoteService.ValorDaSessao(modelo.Valor, modelo.QuantidadeSessoes),
                Inicio = inicio,
                Validade = validade,
                Forma = venda.FormaPagamentoId,
                Confirmado = venda.PagamentoConfirmado,
                Usuario = usuario.Id
            }, tx, ct));

        await auditoria.RegistrarAsync("INCLUIR", "PACOTE_PACIENTE", codigo, codigoPaciente, usuario.Id,
            new { pacoteId = modelo.Codigo, sessoes = modelo.QuantidadeSessoes, valor = modelo.Valor, pagamentoConfirmado = venda.PagamentoConfirmado }, con, tx, ct);
        return codigo;
    }

    // ------------------------------------------------------------------- pagamento e cancelamento
    public async Task<PacotePacienteDto> ConfirmarPagamentoAsync(Guid id, UsuarioAtual usuario, CancellationToken ct)
    {
        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        var atual = await CarregarAsync(con, tx, id, usuario, ct);

        if (atual.Status != "ATIVO") throw Erro.Conflito("O pacote foi cancelado.", "PACOTE_CANCELADO");
        if (atual.PagamentoConfirmado) throw Erro.Conflito("O pagamento do pacote já foi confirmado.", "PAGAMENTO_JA_CONFIRMADO");

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.PACOTE_PACIENTE SET PAGAMENTO_CONFIRMADO = 1, DATA_PAGAMENTO = SYSUTCDATETIME(), PAGAMENTO_USUARIO = @Usuario
WHERE CODIGO_PACOTE_PACIENTE = @Codigo AND PAGAMENTO_CONFIRMADO = 0;", new { Usuario = usuario.Id, atual.Codigo }, tx, ct));

        // As consultas já agendadas com este pacote acompanham o pagamento (as canceladas e remarcadas ficam como estão).
        var consultas = await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.CONSULTA
SET PAGAMENTO_CONFIRMADO = 1, DATA_PAGAMENTO = SYSUTCDATETIME(), PAGAMENTO_CONFIRMADO_USUARIO = @Usuario, DATA_ATUALIZACAO = SYSUTCDATETIME()
WHERE CODIGO_PACOTE_PACIENTE = @Codigo AND PAGAMENTO_CONFIRMADO = 0 AND STATUS NOT IN ('CANCELADA','REMARCADA');",
            new { Usuario = usuario.Id, atual.Codigo }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "PACOTE_PACIENTE", atual.Codigo, null, usuario.Id,
            new { pagamentoConfirmado = true, consultasAtualizadas = consultas }, con, tx, ct);

        var nova = await con.QueryFirstAsync<Linha>(Sql.Cmd(SelectPacote + " WHERE PP.CODIGO_PACOTE_PACIENTE = @C;", new { C = atual.Codigo }, tx, ct));
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    public async Task<PacotePacienteDto> CancelarAsync(Guid id, CancelamentoPacoteEntrada? d, UsuarioAtual usuario, CancellationToken ct)
    {
        if (d is null) throw Erro.Validacao("corpo", "Corpo da requisição inválido");
        var motivo = d.Motivo?.Trim();
        if (string.IsNullOrEmpty(motivo)) throw Erro.Validacao("motivo", NaoPreenchido);
        if (motivo.Length > 500) throw Erro.Validacao("motivo", "Máximo de 500 caracteres");

        await using var con = await _db.AbrirAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        var atual = await CarregarAsync(con, tx, id, usuario, ct);

        // Trava o pacote enquanto confere: ninguém agenda uma consulta nele no meio da conferência.
        await con.ExecuteScalarAsync<string>(Sql.Cmd(
            "SELECT STATUS FROM DBO.PACOTE_PACIENTE WITH (UPDLOCK, HOLDLOCK) WHERE CODIGO_PACOTE_PACIENTE = @C;", new { C = atual.Codigo }, tx, ct));
        if (atual.Status != "ATIVO") throw Erro.Conflito("O pacote já está cancelado.", "PACOTE_JA_CANCELADO");

        var usadas = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.CONSULTA WHERE CODIGO_PACOTE_PACIENTE = @C AND STATUS NOT IN ('CANCELADA','REMARCADA');", new { C = atual.Codigo }, tx, ct));
        if (usadas > 0)
            throw Erro.Conflito($"O pacote já tem {usadas} consulta(s) agendada(s) ou realizada(s). Cancele as consultas agendadas antes, ou deixe o pacote até acabar.", "PACOTE_EM_USO");

        await con.ExecuteAsync(Sql.Cmd(@"
UPDATE DBO.PACOTE_PACIENTE SET STATUS = 'CANCELADO', DATA_CANCELAMENTO = SYSUTCDATETIME(), CANCELAMENTO_USUARIO = @Usuario, MOTIVO_CANCELAMENTO = @Motivo
WHERE CODIGO_PACOTE_PACIENTE = @Codigo AND STATUS = 'ATIVO';", new { Usuario = usuario.Id, Motivo = motivo, atual.Codigo }, tx, ct));

        await _auditoria.RegistrarAsync("EDITAR", "PACOTE_PACIENTE", atual.Codigo, null, usuario.Id,
            new { status = "CANCELADO", pagamentoConfirmado = atual.PagamentoConfirmado }, con, tx, ct);

        var nova = await con.QueryFirstAsync<Linha>(Sql.Cmd(SelectPacote + " WHERE PP.CODIGO_PACOTE_PACIENTE = @C;", new { C = atual.Codigo }, tx, ct));
        await tx.CommitAsync(ct);
        return ParaDto(nova);
    }

    // ------------------------------------------------------------------- uso na consulta (chamado pelo ConsultaService)
    private sealed class PacoteUsoLinha
    {
        public int Codigo { get; set; }
        public string Nome { get; set; } = string.Empty;
        public int EspecialidadeId { get; set; }
        public string EspecialidadeNome { get; set; } = string.Empty;
        public int Quantidade { get; set; }
        public decimal ValorSessao { get; set; }
        public DateTime DataValidade { get; set; }
        public int FormaId { get; set; }
        public bool PagamentoConfirmado { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Confere e reserva uma sessão do pacote para a consulta que está sendo agendada: usa o pacote já vendido ao paciente
    /// ou vende um novo na hora. O pacote fica travado até o fim da transação, então duas consultas ao mesmo tempo não passam do saldo.
    /// </summary>
    internal static async Task<UsoPacote> PrepararUsoAsync(SqlConnection con, SqlTransaction tx, IAuditoria auditoria, int codigoPaciente,
        int especialidadeId, DateTime inicioUtc, Guid? pacotePacienteId, VendaPacoteEntrada? venda, UsuarioAtual usuario, CancellationToken ct)
    {
        if (pacotePacienteId.HasValue && venda is not null)
            throw Erro.Validacao("pacotePacienteId", "Escolha um pacote do paciente ou a venda de um novo, não os dois");

        int codigo;
        if (venda is not null)
        {
            codigo = await VenderNaTransacaoAsync(con, tx, auditoria, codigoPaciente, venda, usuario, ct);
        }
        else
        {
            codigo = await con.ExecuteScalarAsync<int?>(Sql.Cmd(
                "SELECT CODIGO_PACOTE_PACIENTE FROM DBO.PACOTE_PACIENTE WHERE CODIGO_PUBLIC = @Id AND CODIGO_PACIENTE = @Pac;",
                new { Id = pacotePacienteId, Pac = codigoPaciente }, tx, ct))
                ?? throw Erro.Validacao("pacotePacienteId", "Pacote não encontrado para este paciente");
        }

        var p = await con.QueryFirstAsync<PacoteUsoLinha>(Sql.Cmd(@"
SELECT PP.CODIGO_PACOTE_PACIENTE AS Codigo, PP.NOME AS Nome, PP.CODIGO_ESPECIALIDADE AS EspecialidadeId, E.NOME AS EspecialidadeNome,
       PP.QUANTIDADE_SESSOES AS Quantidade, PP.VALOR_SESSAO AS ValorSessao, PP.DATA_VALIDADE AS DataValidade,
       PP.CODIGO_PAGAMENTO AS FormaId, PP.PAGAMENTO_CONFIRMADO AS PagamentoConfirmado, PP.STATUS AS Status
FROM DBO.PACOTE_PACIENTE PP WITH (UPDLOCK, HOLDLOCK)
JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = PP.CODIGO_ESPECIALIDADE
WHERE PP.CODIGO_PACOTE_PACIENTE = @C;", new { C = codigo }, tx, ct));

        if (p.Status != "ATIVO")
            throw Erro.Regra("PACOTE_CANCELADO", "O pacote está cancelado.");
        if (p.EspecialidadeId != especialidadeId)
            throw Erro.Regra("PACOTE_ESPECIALIDADE", $"Este pacote é de {p.EspecialidadeNome}. Escolha a mesma especialidade ou outro pacote.");

        var dia = DateOnly.FromDateTime(DataBrasil.ParaBrasilia(inicioUtc));
        var validade = DateOnly.FromDateTime(p.DataValidade);
        if (dia > validade)
            throw Erro.Regra("PACOTE_VENCIDO", $"O pacote vale até {validade:dd/MM/yyyy}. Escolha uma data até esse dia.");

        var usadas = await con.ExecuteScalarAsync<int>(Sql.Cmd(
            "SELECT COUNT(1) FROM DBO.CONSULTA WHERE CODIGO_PACOTE_PACIENTE = @C AND STATUS NOT IN ('CANCELADA','REMARCADA');", new { C = codigo }, tx, ct));
        if (usadas >= p.Quantidade)
            throw Erro.Regra("PACOTE_SEM_SALDO", $"O pacote não tem mais sessões disponíveis ({usadas} de {p.Quantidade} usadas).");

        return new UsoPacote(codigo, p.ValorSessao, p.FormaId, p.PagamentoConfirmado);
    }

    /// <summary>Liga a consulta recém-criada ao pacote.</summary>
    internal static Task VincularAsync(SqlConnection con, SqlTransaction tx, int codigoConsulta, int codigoPacotePaciente, CancellationToken ct) =>
        con.ExecuteAsync(Sql.Cmd("UPDATE DBO.CONSULTA SET CODIGO_PACOTE_PACIENTE = @P WHERE CODIGO_CONSULTA = @C;",
            new { P = codigoPacotePaciente, C = codigoConsulta }, tx, ct));

    private sealed class VinculoLinha
    {
        public DateTime DataValidade { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>Na remarcação de uma consulta de pacote, a nova data precisa estar dentro da validade do pacote.</summary>
    internal static async Task ValidarRemarcacaoAsync(SqlConnection con, SqlTransaction tx, int codigoConsultaOriginal, DateTime novoInicioUtc, CancellationToken ct)
    {
        var v = await con.QueryFirstOrDefaultAsync<VinculoLinha>(Sql.Cmd(@"
SELECT PP.DATA_VALIDADE AS DataValidade, PP.STATUS AS Status
FROM DBO.CONSULTA C JOIN DBO.PACOTE_PACIENTE PP ON PP.CODIGO_PACOTE_PACIENTE = C.CODIGO_PACOTE_PACIENTE
WHERE C.CODIGO_CONSULTA = @C;", new { C = codigoConsultaOriginal }, tx, ct));
        if (v is null) return;

        var dia = DateOnly.FromDateTime(DataBrasil.ParaBrasilia(novoInicioUtc));
        var validade = DateOnly.FromDateTime(v.DataValidade);
        if (dia > validade)
            throw Erro.Regra("PACOTE_VENCIDO", $"Esta consulta usa um pacote que vale até {validade:dd/MM/yyyy}. Escolha uma data até esse dia.");
    }

    // ------------------------------------------------------------------- mapeamento
    private static PacotePacienteDto ParaDto(Linha l) => ParaDto(l, new PacotePacienteDto());

    private static T ParaDto<T>(Linha l, T d) where T : PacotePacienteDto
    {
        var validade = DateOnly.FromDateTime(l.DataValidade);
        var restantes = Math.Max(0, l.QuantidadeSessoes - l.SessoesUsadas);
        d.Id = l.Id;
        d.Nome = l.Nome;
        d.Paciente = new PacienteReferenciaDto { Id = l.PacienteId, Nome = l.PacienteNome };
        d.Especialidade = new ReferenciaDto { Id = l.EspecialidadeId, Nome = l.EspecialidadeNome };
        d.QuantidadeSessoes = l.QuantidadeSessoes;
        d.SessoesUsadas = l.SessoesUsadas;
        d.SessoesRestantes = restantes;
        d.Valor = l.Valor;
        d.ValorSessao = l.ValorSessao;
        d.DataInicio = DateOnly.FromDateTime(l.DataInicio);
        d.DataValidade = validade;
        d.FormaPagamento = new ReferenciaDto { Id = l.FormaId, Nome = l.FormaNome };
        d.PagamentoConfirmado = l.PagamentoConfirmado;
        d.PagamentoConfirmadoEm = DataBrasil.Utc(l.PagamentoConfirmadoEm);
        d.Status = l.Status;
        d.Situacao = l.Status == "CANCELADO" ? "CANCELADO" : validade < DataBrasil.Hoje() || restantes == 0 ? "ENCERRADO" : "DISPONIVEL";
        d.VendidoEm = DataBrasil.Utc(l.VendidoEm);
        d.VendidoPor = l.VendidoPorId.HasValue
            ? new UsuarioReferenciaDto { Id = l.VendidoPorId.Value, NomeCompleto = l.VendidoPorNome ?? string.Empty }
            : null;
        d.CanceladoEm = DataBrasil.Utc(l.CanceladoEm);
        d.MotivoCancelamento = l.MotivoCancelamento;
        return d;
    }
}
