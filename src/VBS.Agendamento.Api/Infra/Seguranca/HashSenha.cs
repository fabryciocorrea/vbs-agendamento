using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace VBS.Agendamento.Api.Infra.Seguranca;

/// <summary>
/// Hash de senha com PBKDF2-SHA256 e sal aleatório (RNF06). Formato gravado em USUARIO.SENHA_HASH:
/// PBKDF2-SHA256$iteracoes$salBase64$hashBase64
/// </summary>
public sealed class HashSenha
{
    private const string Prefixo = "PBKDF2-SHA256";
    private readonly int _iteracoes;
    private readonly Lazy<string> _hashFalso;

    public HashSenha(IOptions<SegurancaOpcoes> opcoes)
    {
        _iteracoes = opcoes.Value.IteracoesPbkdf2;
        _hashFalso = new Lazy<string>(() => Gerar(Guid.NewGuid().ToString("N")));
    }

    /// <summary>Hash usado quando o usuário não existe, para o tempo de resposta não revelar quem existe.</summary>
    public string HashFalso => _hashFalso.Value;

    public string Gerar(string senha)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, sal, _iteracoes, HashAlgorithmName.SHA256, 32);
        return $"{Prefixo}${_iteracoes}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public bool Verificar(string senha, string armazenado)
    {
        try
        {
            var partes = armazenado.Split('$');
            if (partes.Length != 4 || partes[0] != Prefixo || !int.TryParse(partes[1], out var iteracoes)) return false;
            var sal = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(senha, sal, iteracoes, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class Tokens
{
    /// <summary>Token aleatório de 256 bits, seguro para URL.</summary>
    public static string Gerar() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Só o hash do token vai para o banco (SESSAO_USUARIO e TOKEN_RECUPERACAO_SENHA).</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
