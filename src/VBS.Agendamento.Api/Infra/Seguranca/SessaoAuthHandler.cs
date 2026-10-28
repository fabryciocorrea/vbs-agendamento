using System.Security.Claims;
using System.Text.Encodings.Web;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Db;

namespace VBS.Agendamento.Api.Infra.Seguranca;

/// <summary>
/// Autenticação por sessão guardada no servidor (D1). O token que o cliente envia em
/// "Authorization: Bearer" é aleatório; no banco fica só o hash (SESSAO_USUARIO.TOKEN_HASH).
/// A cada requisição confere: sessão aberta, usuário ativo e não bloqueado, e a última atividade
/// (mais de InatividadeMinutos encerra com motivo INATIVIDADE; senão renova a janela).
/// </summary>
public sealed class SessaoAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Esquema = "Sessao";
    private const string ChaveErro = "vbs.auth.erro";

    private readonly IConexaoFactory _db;
    private readonly SegurancaOpcoes _seguranca;

    public SessaoAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> opcoes,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConexaoFactory db,
        IOptions<SegurancaOpcoes> seguranca)
        : base(opcoes, logger, encoder)
    {
        _db = db;
        _seguranca = seguranca.Value;
    }

    private sealed class SessaoLinha
    {
        public long SessaoId { get; set; }
        public int UsuarioId { get; set; }
        public DateTime UltimaAtividade { get; set; }
        public DateTime? Encerramento { get; set; }
        public DateTime Agora { get; set; }
        public bool UsuarioAtivo { get; set; }
        public DateTime? Bloqueio { get; set; }
        public int PerfilId { get; set; }
        public bool PerfilProtegido { get; set; }
        public int? ProfissionalId { get; set; }
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cabecalho = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(cabecalho)) return AuthenticateResult.NoResult();

        if (!cabecalho.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Falha("Use o cabeçalho Authorization: Bearer <accessToken>.");

        var token = cabecalho[7..].Trim();
        if (token.Length == 0 || token.Length > 200) return Falha("Token inválido.");

        var ct = Context.RequestAborted;
        await using var con = await _db.AbrirAsync(ct);

        const string sql = @"
SELECT S.CODIGO_SESSAO AS SessaoId, S.CODIGO_USUARIO AS UsuarioId, S.DATA_ULTIMA_ATIVIDADE AS UltimaAtividade,
       S.DATA_ENCERRAMENTO AS Encerramento, SYSUTCDATETIME() AS Agora,
       U.ATIVO AS UsuarioAtivo, U.DATA_BLOQUEIO AS Bloqueio, U.CODIGO_PERFIL AS PerfilId,
       P.PROTEGIDO AS PerfilProtegido, U.CODIGO_PROFISSIONAL AS ProfissionalId
FROM DBO.SESSAO_USUARIO S
JOIN DBO.USUARIO U ON U.CODIGO_USUARIO = S.CODIGO_USUARIO
JOIN DBO.PERFIL_ACESSO P ON P.CODIGO_PERFIL = U.CODIGO_PERFIL
WHERE S.TOKEN_HASH = @Hash;";

        var s = await con.QueryFirstOrDefaultAsync<SessaoLinha>(Sql.Cmd(sql, new { Hash = Tokens.Hash(token) }, ct: ct));

        if (s is null || s.Encerramento is not null)
            return Falha("Sessão inválida ou encerrada. Entre novamente.");

        if (!s.UsuarioAtivo)
        {
            await EncerrarAsync(con, s.SessaoId, "USUARIO_INATIVADO", ct);
            return Falha("Usuário inativo.");
        }

        if (s.Bloqueio is not null)
        {
            await EncerrarAsync(con, s.SessaoId, "USUARIO_BLOQUEADO", ct);
            return Falha("Usuário bloqueado. Peça o desbloqueio ao administrador.");
        }

        var parado = s.Agora - s.UltimaAtividade;
        if (parado > TimeSpan.FromMinutes(_seguranca.InatividadeMinutos))
        {
            await EncerrarAsync(con, s.SessaoId, "INATIVIDADE", ct);
            return Falha("Sessão expirada por inatividade. Entre novamente.");
        }

        // Renova a janela (a cada 10 segundos no máximo, para não gravar em toda requisição seguida).
        if (parado >= TimeSpan.FromSeconds(10))
        {
            await con.ExecuteAsync(Sql.Cmd(
                "UPDATE DBO.SESSAO_USUARIO SET DATA_ULTIMA_ATIVIDADE = SYSUTCDATETIME() WHERE CODIGO_SESSAO = @Id AND DATA_ENCERRAMENTO IS NULL;",
                new { Id = s.SessaoId }, ct: ct));
        }

        var claims = new List<Claim>
        {
            new(UsuarioAtualExtensoes.ClaimUsuario, s.UsuarioId.ToString()),
            new(UsuarioAtualExtensoes.ClaimSessao, s.SessaoId.ToString()),
            new(UsuarioAtualExtensoes.ClaimPerfil, s.PerfilId.ToString()),
            new(UsuarioAtualExtensoes.ClaimAdministrador, s.PerfilProtegido ? "1" : "0")
        };
        if (s.ProfissionalId.HasValue)
            claims.Add(new Claim(UsuarioAtualExtensoes.ClaimProfissional, s.ProfissionalId.Value.ToString()));

        var identidade = new ClaimsIdentity(claims, Esquema);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identidade), Esquema);
        return AuthenticateResult.Success(ticket);
    }

    private AuthenticateResult Falha(string mensagem)
    {
        Context.Items[ChaveErro] = mensagem;
        return AuthenticateResult.Fail(mensagem);
    }

    private static Task<int> EncerrarAsync(SqlConnection con, long sessaoId, string motivo, CancellationToken ct) =>
        con.ExecuteAsync(Sql.Cmd(
            "UPDATE DBO.SESSAO_USUARIO SET DATA_ENCERRAMENTO = SYSUTCDATETIME(), MOTIVO_ENCERRAMENTO = @Motivo WHERE CODIGO_SESSAO = @Id AND DATA_ENCERRAMENTO IS NULL;",
            new { Id = sessaoId, Motivo = motivo }, ct: ct));

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var detalhe = Context.Items.TryGetValue(ChaveErro, out var valor) && valor is string texto
            ? texto
            : "É necessário entrar no sistema (cabeçalho Authorization: Bearer).";
        Response.Headers["WWW-Authenticate"] = "Bearer";
        await Problema.Escrever(Context, 401, "Não autenticado", detalhe);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Problema.Escrever(Context, 403, "Sem permissão", "O seu perfil não tem liberação para esta ação.");
}
