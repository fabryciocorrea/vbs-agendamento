using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Infra.Inicializacao;

/// <summary>
/// Roda uma vez ao iniciar o servidor:
/// 1) grava o catálogo de telas (TELA_SISTEMA), como pede o script 01 ("carregado pelo sistema a cada versão");
/// 2) cria o usuário administrador inicial se ainda não existir nenhum usuário (o script 01 deixa isso para a aplicação,
///    porque precisa do hash da senha).
/// Se o banco não estiver acessível, só registra o erro no console e o servidor continua de pé (veja /saude).
/// </summary>
public sealed class Inicializador : IHostedService
{
    private readonly IConexaoFactory _db;
    private readonly HashSenha _hash;
    private readonly InicializacaoOpcoes _opcoes;
    private readonly ILogger<Inicializador> _log;

    public Inicializador(IConexaoFactory db, HashSenha hash, IOptions<InicializacaoOpcoes> opcoes, ILogger<Inicializador> log)
    {
        _db = db;
        _hash = hash;
        _opcoes = opcoes.Value;
        _log = log;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await using var con = await _db.AbrirAsync(ct);
            if (_opcoes.CarregarCatalogoDeTelas) await CarregarCatalogoAsync(con, ct);
            if (_opcoes.CriarAdministradorSeNaoExistir) await CriarAdministradorAsync(con, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Falha na inicialização do banco. Confira a cadeia de conexão (ConnectionStrings:VBSAGENDA) e se os scripts 01 a 11 foram executados. " +
                "O servidor continua no ar; use GET /vbsagenda para testar a conexão.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task CarregarCatalogoAsync(SqlConnection con, CancellationToken ct)
    {
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        foreach (var (codigo, nome, modulo) in CatalogoTelas.Telas)
        {
            await con.ExecuteAsync(Sql.Cmd(@"
IF EXISTS (SELECT 1 FROM DBO.TELA_SISTEMA WHERE CODIGO = @Codigo)
    UPDATE DBO.TELA_SISTEMA SET NOME = @Nome, MODULO = @Modulo WHERE CODIGO = @Codigo;
ELSE
    INSERT INTO DBO.TELA_SISTEMA (CODIGO, NOME, MODULO) VALUES (@Codigo, @Nome, @Modulo);",
                new { Codigo = codigo, Nome = nome, Modulo = modulo }, tx, ct));
        }
        await tx.CommitAsync(ct);
        _log.LogInformation("Catálogo de telas carregado: {Quantidade} telas.", CatalogoTelas.Telas.Count);
    }

    private async Task CriarAdministradorAsync(SqlConnection con, CancellationToken ct)
    {
        var usuarios = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM DBO.USUARIO;", ct: ct));
        if (usuarios > 0) return;

        if (string.IsNullOrWhiteSpace(_opcoes.AdminNomeUsuario) ||
            string.IsNullOrWhiteSpace(_opcoes.AdminNomeCompleto) ||
            string.IsNullOrWhiteSpace(_opcoes.AdminEmail))
        {
            _log.LogWarning("Não há usuários no banco e a seção Inicializacao (AdminNomeUsuario, AdminNomeCompleto, AdminEmail) está incompleta: administrador inicial NÃO criado.");
            return;
        }

        var perfilId = await con.ExecuteScalarAsync<int?>(Sql.Cmd(
            "SELECT TOP (1) CODIGO_PERFIL FROM DBO.PERFIL_ACESSO WHERE PROTEGIDO = 1 ORDER BY CODIGO_PERFIL;", ct: ct));
        if (perfilId is null)
        {
            _log.LogWarning("Perfil Administrador (protegido) não encontrado em PERFIL_ACESSO. Rode o script 02 (dados iniciais) e reinicie o servidor.");
            return;
        }

        var senha = string.IsNullOrWhiteSpace(_opcoes.AdminSenhaInicial) ? GerarSenha() : _opcoes.AdminSenhaInicial!;
        var senhaGerada = string.IsNullOrWhiteSpace(_opcoes.AdminSenhaInicial);

        await con.ExecuteAsync(Sql.Cmd(@"
INSERT INTO DBO.USUARIO (CODIGO_PERFIL, CODIGO_PROFISSIONAL, NOME_USUARIO, SENHA_HASH, NOME_COMPLETO, EMAIL, CARGO)
VALUES (@Perfil, NULL, @NomeUsuario, @Hash, @NomeCompleto, @Email, @Cargo);",
            new
            {
                Perfil = perfilId,
                NomeUsuario = _opcoes.AdminNomeUsuario.Trim(),
                Hash = _hash.Gerar(senha),
                NomeCompleto = _opcoes.AdminNomeCompleto.Trim(),
                Email = _opcoes.AdminEmail.Trim(),
                Cargo = string.IsNullOrWhiteSpace(_opcoes.AdminCargo) ? "Administrador" : _opcoes.AdminCargo.Trim()
            }, ct: ct));

        if (senhaGerada)
        {
            _log.LogWarning(
                "ADMINISTRADOR INICIAL CRIADO. Usuário: {Usuario} | Senha gerada (aparece só agora, anote): {Senha}",
                _opcoes.AdminNomeUsuario.Trim(), senha);
        }
        else
        {
            _log.LogWarning("ADMINISTRADOR INICIAL CRIADO com a senha informada em Inicializacao:AdminSenhaInicial. Usuário: {Usuario}",
                _opcoes.AdminNomeUsuario.Trim());
        }
    }

    /// <summary>16 caracteres, com letras, números e caracteres especiais (D4).</summary>
    private static string GerarSenha()
    {
        const string letras = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz";
        const string numeros = "23456789";
        const string especiais = "!@#$%*?-_";

        var caracteres = new List<char>();
        for (var i = 0; i < 9; i++) caracteres.Add(letras[RandomNumberGenerator.GetInt32(letras.Length)]);
        for (var i = 0; i < 4; i++) caracteres.Add(numeros[RandomNumberGenerator.GetInt32(numeros.Length)]);
        for (var i = 0; i < 3; i++) caracteres.Add(especiais[RandomNumberGenerator.GetInt32(especiais.Length)]);

        // Embaralha (Fisher-Yates).
        for (var i = caracteres.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (caracteres[i], caracteres[j]) = (caracteres[j], caracteres[i]);
        }
        return new string(caracteres.ToArray());
    }
}
