namespace VBS.Agendamento.Api.Common;

/// <summary>
/// Tabela pronta para virar arquivo (PDF ou CSV): o servidor monta e registra no log, o navegador desenha o arquivo (RF23).
/// Todos os valores já vão como texto formatado em português (datas, horas, valores em R$ e porcentagens).
/// </summary>
public sealed class TabelaExportacao
{
    public string Titulo { get; set; } = string.Empty;
    /// <summary>Linhas de filtros usados, uma por item (ex.: "Período: 01/11/2026 a 30/11/2026").</summary>
    public List<string> Filtros { get; set; } = new();
    public List<ColunaTabela> Colunas { get; set; } = new();
    public List<List<string>> Linhas { get; set; } = new();
    /// <summary>Última linha em destaque (totais), com o mesmo número de colunas. Vazia quando não há.</summary>
    public List<string>? Totais { get; set; }
    public DateTime GeradoEm { get; set; } = DateTime.UtcNow;
}

public sealed class ColunaTabela
{
    public string Titulo { get; set; } = string.Empty;
    /// <summary>"esquerda" (texto) ou "direita" (números).</summary>
    public string Alinhamento { get; set; } = "esquerda";
    /// <summary>Só na tela dos relatórios cadastrais: seção em que o campo aparece nos detalhes da linha.</summary>
    public string? Grupo { get; set; }
    /// <summary>Só na tela dos relatórios cadastrais: a coluna aparece na tabela resumida (as demais ficam nos detalhes da linha).</summary>
    public bool Resumo { get; set; }

    public static ColunaTabela Texto(string titulo) => new() { Titulo = titulo, Alinhamento = "esquerda" };
    public static ColunaTabela Numero(string titulo) => new() { Titulo = titulo, Alinhamento = "direita" };
}
