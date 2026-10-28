using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace VBS.Agendamento.Api.Common;

/// <summary>Erro de um campo, no formato do contrato: { campo, mensagem }.</summary>
public sealed record ErroCampo(string Campo, string Mensagem);

/// <summary>Erro de regra de negócio. O middleware converte em application/problem+json (RFC 7807).</summary>
public sealed class ApiException : Exception
{
    public int Status { get; }
    public string Titulo { get; }
    public string? Detalhe { get; }
    public string? Codigo { get; }
    public IReadOnlyList<ErroCampo>? Erros { get; }
    public IReadOnlyDictionary<string, object?>? Extras { get; }

    public ApiException(
        int status,
        string titulo,
        string? detalhe = null,
        string? codigo = null,
        IReadOnlyList<ErroCampo>? erros = null,
        IReadOnlyDictionary<string, object?>? extras = null)
        : base(detalhe ?? titulo)
    {
        Status = status;
        Titulo = titulo;
        Detalhe = detalhe;
        Codigo = codigo;
        Erros = erros;
        Extras = extras;
    }
}

/// <summary>Atalhos para os erros mais comuns.</summary>
public static class Erro
{
    public static ApiException Validacao(IReadOnlyList<ErroCampo> erros) =>
        new(400, "Dados inválidos", erros: erros);

    public static ApiException Validacao(string campo, string mensagem) =>
        new(400, "Dados inválidos", erros: new[] { new ErroCampo(campo, mensagem) });

    public static ApiException Regra(string codigo, string detalhe, IReadOnlyList<ErroCampo>? erros = null) =>
        new(400, "Dados inválidos", detalhe, codigo, erros);

    public static ApiException Conflito(string detalhe, string? codigo = null, IReadOnlyDictionary<string, object?>? extras = null) =>
        new(409, "Conflito", detalhe, codigo, extras: extras);

    public static ApiException NaoEncontrado(string? detalhe = null, string? codigo = null) =>
        new(404, "Não encontrado", detalhe ?? "Registro não encontrado.", codigo);

    public static ApiException SemPermissao(string? detalhe = null, string? codigo = null) =>
        new(403, "Sem permissão", detalhe ?? "O seu perfil não tem liberação para esta ação.", codigo);

    public static ApiException NaoAutenticado(string? detalhe = null) =>
        new(401, "Não autenticado", detalhe ?? "Usuário ou senha inválidos.");
}

/// <summary>Escreve o corpo application/problem+json do contrato.</summary>
public static class Problema
{
    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static Task Escrever(HttpContext ctx, ApiException ex) =>
        Escrever(ctx, ex.Status, ex.Titulo, ex.Detalhe, ex.Codigo, ex.Erros, ex.Extras);

    public static async Task Escrever(
        HttpContext ctx,
        int status,
        string titulo,
        string? detalhe = null,
        string? codigo = null,
        IReadOnlyList<ErroCampo>? erros = null,
        IReadOnlyDictionary<string, object?>? extras = null)
    {
        if (ctx.Response.HasStarted) return;

        var corpo = new Dictionary<string, object?>
        {
            ["title"] = titulo,
            ["status"] = status
        };
        if (detalhe is not null) corpo["detail"] = detalhe;
        if (codigo is not null) corpo["codigo"] = codigo;
        if (erros is { Count: > 0 }) corpo["erros"] = erros;
        if (extras is not null)
        {
            foreach (var par in extras) corpo[par.Key] = par.Value;
        }

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json; charset=utf-8";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, corpo, Opcoes, ctx.RequestAborted);
    }

    /// <summary>Título padrão para respostas de erro sem corpo (404 de rota, 405, 415...).</summary>
    public static string TituloPadrao(int status) => status switch
    {
        400 => "Dados inválidos",
        401 => "Não autenticado",
        403 => "Sem permissão",
        404 => "Não encontrado",
        405 => "Método não permitido",
        409 => "Conflito",
        415 => "Tipo de conteúdo não suportado",
        429 => "Muitas requisições",
        _ => "Erro"
    };

    /// <summary>Converte erros de leitura do corpo (JSON malformado, tipo errado) em 400 do contrato, sem expor detalhes internos.</summary>
    public static IActionResult DeModelState(ActionContext ctx)
    {
        var erros = new List<ErroCampo>();
        foreach (var par in ctx.ModelState)
        {
            if (par.Value is null || par.Value.Errors.Count == 0) continue;
            var campo = NomeDoCampo(par.Key);
            erros.Add(new ErroCampo(campo, "Valor inválido"));
        }
        if (erros.Count == 0) erros.Add(new ErroCampo("corpo", "Corpo da requisição inválido"));
        return new ProblemaResult(new ApiException(400, "Dados inválidos", erros: erros));
    }

    private static string NomeDoCampo(string chave)
    {
        var limpo = chave.TrimStart('$', '.');
        if (string.IsNullOrWhiteSpace(limpo)) return "corpo";
        return char.ToLowerInvariant(limpo[0]) + limpo[1..];
    }
}

public sealed class ProblemaResult : IActionResult
{
    private readonly ApiException _erro;
    public ProblemaResult(ApiException erro) => _erro = erro;
    public Task ExecuteResultAsync(ActionContext context) => Problema.Escrever(context.HttpContext, _erro);
}
