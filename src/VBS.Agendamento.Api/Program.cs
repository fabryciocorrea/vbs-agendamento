using System.Text.Json;
using System.Threading.RateLimiting;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Features.Apoio;
using VBS.Agendamento.Api.Features.Auth;
using VBS.Agendamento.Api.Features.Convenios;
using VBS.Agendamento.Api.Features.Credenciamentos;
using VBS.Agendamento.Api.Features.Faturamento;
using VBS.Agendamento.Api.Features.Especialidades;
using VBS.Agendamento.Api.Features.FormasPagamento;
using VBS.Agendamento.Api.Features.Pacientes;
using VBS.Agendamento.Api.Features.Perfis;
using VBS.Agendamento.Api.Features.Profissionais;
using VBS.Agendamento.Api.Features.Agenda;
using VBS.Agendamento.Api.Features.Colaboradores;
using VBS.Agendamento.Api.Features.Consultas;
using VBS.Agendamento.Api.Features.Salas;
using VBS.Agendamento.Api.Features.Unidades;
using VBS.Agendamento.Api.Features.Usuarios;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Inicializacao;
using VBS.Agendamento.Api.Infra.Seguranca;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------ configuração
builder.Services.Configure<SegurancaOpcoes>(builder.Configuration.GetSection("Seguranca"));
builder.Services.Configure<InicializacaoOpcoes>(builder.Configuration.GetSection("Inicializacao"));

// ------------------------------------------------------------------ serviços
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IConexaoFactory, ConexaoFactory>();
builder.Services.AddSingleton<HashSenha>();
builder.Services.AddScoped<IAuditoria, Auditoria>();
builder.Services.AddScoped<IPermissaoService, PermissaoService>();
builder.Services.AddScoped<IEnviadorEmail, EnviadorEmailConsole>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PacienteService>();
builder.Services.AddScoped<ResponsavelLegalService>();
builder.Services.AddScoped<UnidadeService>();
builder.Services.AddScoped<SalaService>();
builder.Services.AddScoped<ColaboradorService>();
builder.Services.AddScoped<EspecialidadeService>();
builder.Services.AddScoped<ConvenioService>();
builder.Services.AddScoped<CredenciamentoService>();
builder.Services.AddScoped<GuiaService>();
builder.Services.AddScoped<FinanceiroService>();
builder.Services.AddScoped<FormaPagamentoService>();
builder.Services.AddScoped<ApoioService>();
builder.Services.AddScoped<ProfissionalService>();
builder.Services.AddScoped<PerfilService>();
builder.Services.AddScoped<UsuarioService>();
builder.Services.AddScoped<ConsultaService>();
builder.Services.AddScoped<AgendaService>();
builder.Services.AddHostedService<Inicializador>();

builder.Services
    .AddControllers(o =>
    {
        // As validações são feitas à mão (mensagens do contrato), então o campo não-nulo não vira "obrigatório" sozinho.
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(o =>
    {
        // camelCase, datas ISO 8601 (contrato). Valores nulos continuam saindo (os campos "nullable" do contrato).
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    })
    .ConfigureApiBehaviorOptions(o =>
    {
        o.InvalidModelStateResponseFactory = Problema.DeModelState;
    });

// Sessão guardada no servidor + exigência de login em tudo, exceto onde há [AllowAnonymous].
builder.Services
    .AddAuthentication(SessaoAuthHandler.Esquema)
    .AddScheme<AuthenticationSchemeOptions, SessaoAuthHandler>(SessaoAuthHandler.Esquema, null);

builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// 429: no máximo 10 tentativas por minuto por endereço nas rotas públicas de /auth.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", contexto => RateLimitPartition.GetFixedWindowLimiter(
        contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    o.OnRejected = async (contexto, ct) =>
    {
        contexto.HttpContext.Response.Headers["Retry-After"] = "60";
        await Problema.Escrever(contexto.HttpContext, 429, "Muitas requisições",
            "Muitas tentativas em pouco tempo. Aguarde e tente novamente.");
    };
});

// CORS para a tela (React) em desenvolvimento local.
var origens = builder.Configuration.GetSection("Cors:Origens").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddPolicy("Tela", politica => politica
    .WithOrigins(origens)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Location", "Retry-After")));

var app = builder.Build();

// ------------------------------------------------------------------ pipeline
// 1) Erros de regra de negócio viram application/problem+json; erro inesperado vira 500 sem detalhes internos.
app.Use(async (contexto, proximo) =>
{
    try
    {
        await proximo();
    }
    catch (ApiException ex)
    {
        await Problema.Escrever(contexto, ex);
    }
    catch (OperationCanceledException) when (contexto.RequestAborted.IsCancellationRequested)
    {
        // O cliente desistiu da requisição; não há a quem responder.
    }
    catch (Exception ex)
    {
        contexto.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Erro")
            .LogError(ex, "Erro não tratado em {Metodo} {Caminho}", contexto.Request.Method, contexto.Request.Path);
        // Em desenvolvimento, mostra a causa na própria tela para facilitar o diagnóstico. Em produção, nunca.
        var detalhe500 = app.Environment.IsDevelopment()
            ? "Erro interno: " + ex.GetBaseException().Message
            : "Ocorreu um erro inesperado. Tente novamente; se persistir, avise a TI.";
        await Problema.Escrever(contexto, 500, "Erro interno", detalhe500);
    }
});

// 2) Dados de saúde e financeiros não ficam em cache (contrato).
app.Use(async (contexto, proximo) =>
{
    contexto.Response.OnStarting(() =>
    {
        contexto.Response.Headers["Cache-Control"] = "no-store";
        return Task.CompletedTask;
    });
    await proximo();
});

// 3) Rotas inexistentes, método errado etc. também respondem no formato do contrato.
app.UseStatusCodePages(contexto =>
    Problema.Escrever(contexto.HttpContext, contexto.HttpContext.Response.StatusCode,
        Problema.TituloPadrao(contexto.HttpContext.Response.StatusCode)));

app.UseCors("Tela");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ------------------------------------------------------------ só em desenvolvimento
if (app.Environment.IsDevelopment())
{
    // Teste da conexão com o banco: GET http://localhost:5000/vbsagenda
    app.MapGet("/vbsagenda", async (IConexaoFactory db, CancellationToken ct) =>
    {
        try
        {
            await using var con = await db.AbrirAsync(ct);
            var info = await con.QueryFirstAsync<SaudeBanco>(Sql.Cmd(@"
SELECT DB_NAME() AS Banco, @@SERVERNAME AS Servidor, SUSER_SNAME() AS Login,
       CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(50)) AS VersaoSqlServer,
       (SELECT COUNT(1) FROM SYS.TABLES) AS Tabelas,
       (SELECT COUNT(1) FROM DBO.TELA_SISTEMA) AS Telas,
       (SELECT COUNT(1) FROM DBO.USUARIO) AS Usuarios;", ct: ct));
            return Results.Ok(new { status = "ok", info.Banco, info.Servidor, info.Login, info.VersaoSqlServer, info.Tabelas, info.Telas, info.Usuarios });
        }
        catch (Exception ex)
        {
            return Results.Json(
                new { status = "falha", mensagem = ex.Message },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }).AllowAnonymous();

    // O contrato OpenAPI, para abrir em qualquer visualizador: GET http://localhost:5000/openapi.yaml
    app.MapGet("/openapi.yaml", () =>
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, "contrato", "openapi.yaml");
        return File.Exists(caminho)
            ? Results.File(caminho, "application/yaml")
            : Results.NotFound();
    }).AllowAnonymous();
}

app.Run();

internal sealed class SaudeBanco
{
    public string Banco { get; set; } = string.Empty;
    public string Servidor { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string VersaoSqlServer { get; set; } = string.Empty;
    public int Tabelas { get; set; }
    public int Telas { get; set; }
    public int Usuarios { get; set; }
}
