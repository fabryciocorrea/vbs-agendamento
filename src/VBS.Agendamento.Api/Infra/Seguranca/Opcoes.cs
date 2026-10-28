namespace VBS.Agendamento.Api.Infra.Seguranca;

/// <summary>Seção "Seguranca" do appsettings.json (RNF06, D1, D2, D4, D5).</summary>
public sealed class SegurancaOpcoes
{
    /// <summary>D1: a sessão expira após este tempo sem uso.</summary>
    public int InatividadeMinutos { get; set; } = 30;

    /// <summary>D5: bloqueio após este número de senhas erradas seguidas.</summary>
    public int MaxTentativasLogin { get; set; } = 5;

    public int IteracoesPbkdf2 { get; set; } = 210000;

    /// <summary>D2: validade do link de recuperação de senha.</summary>
    public int ValidadeTokenRecuperacaoMinutos { get; set; } = 30;

    /// <summary>Endereço da tela de redefinição; {token} é trocado pelo token.</summary>
    public string UrlRedefinicaoSenha { get; set; } = "http://localhost:5173/redefinir-senha?token={token}";
}

/// <summary>Seção "Inicializacao": carga do catálogo de telas e do administrador inicial.</summary>
public sealed class InicializacaoOpcoes
{
    public bool CarregarCatalogoDeTelas { get; set; } = true;
    public bool CriarAdministradorSeNaoExistir { get; set; } = true;
    public string AdminNomeUsuario { get; set; } = string.Empty;
    public string AdminNomeCompleto { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminCargo { get; set; } = "Administrador";

    /// <summary>Se vazia, o servidor gera uma senha aleatória e mostra uma única vez no console.</summary>
    public string? AdminSenhaInicial { get; set; }
}
