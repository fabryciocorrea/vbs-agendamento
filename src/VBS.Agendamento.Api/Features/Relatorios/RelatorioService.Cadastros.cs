using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using VBS.Agendamento.Api.Common;
using VBS.Agendamento.Api.Infra.Db;
using VBS.Agendamento.Api.Infra.Seguranca;

namespace VBS.Agendamento.Api.Features.Relatorios;

/// <summary>
/// Relatórios cadastrais: pacientes, profissionais, colaboradores e prestadores, convênios e credenciamentos.
/// Trazem todos os dados do cadastro, inclusive o CPF completo (sem máscara), por isso cada um tem permissão própria,
/// nunca vale para o médico e toda visualização e toda exportação vão para o log (LGPD).
/// </summary>
public sealed partial class RelatorioService
{
    private sealed class DefinicaoCadastro
    {
        public string Titulo { get; init; } = string.Empty;
        public string Entidade { get; init; } = string.Empty;
        public List<ColunaTabela> Colunas { get; init; } = new();
        public string Select { get; init; } = string.Empty;
        public string De { get; init; } = string.Empty;
        public string Onde { get; init; } = string.Empty;
        public string Ordem { get; init; } = string.Empty;
        public DynamicParameters Parametros { get; init; } = new();
        public List<string> Filtros { get; init; } = new();
        public Func<IDictionary<string, object>, List<string>> Linha { get; init; } = _ => new List<string>();
    }

    // ------------------------------------------------------------ formatação das células

    private static object? V(IDictionary<string, object> r, string k) => r.TryGetValue(k, out var v) && v is not DBNull ? v : null;
    private static string T(IDictionary<string, object> r, string k) => V(r, k)?.ToString()?.Trim() ?? string.Empty;
    private static string Dt(IDictionary<string, object> r, string k) => V(r, k) is DateTime d ? d.ToString("dd/MM/yyyy", Pt) : string.Empty;
    private static string DtHora(IDictionary<string, object> r, string k) =>
        V(r, k) is DateTime d ? DataBrasil.ParaBrasilia(DataBrasil.Utc(d)).ToString("dd/MM/yyyy HH:mm", Pt) : string.Empty;
    private static string Situacao(IDictionary<string, object> r, string k = "Ativo") => V(r, k) is true ? "Ativo" : "Inativo";
    private static string Dinheiro(IDictionary<string, object> r, string k) => V(r, k) is decimal m ? Moeda(m) : string.Empty;

    private static string Cpf(IDictionary<string, object> r, string k)
    {
        var s = T(r, k);
        return s.Length == 11 && s.All(char.IsDigit) ? $"{s[..3]}.{s[3..6]}.{s[6..9]}-{s[9..]}" : s;
    }

    private static string Cnpj(IDictionary<string, object> r, string k)
    {
        var s = T(r, k);
        return s.Length == 14 && s.All(char.IsDigit) ? $"{s[..2]}.{s[2..5]}.{s[5..8]}/{s[8..12]}-{s[12..]}" : s;
    }

    private static string Endereco(IDictionary<string, object> r)
    {
        var partes = new[] { T(r, "Endereco"), T(r, "Numero"), T(r, "Complemento") }.Where(x => x.Length > 0);
        return string.Join(", ", partes);
    }

    private static string Cep(IDictionary<string, object> r, string k)
    {
        var s = T(r, k);
        return s.Length == 8 && s.All(char.IsDigit) ? $"{s[..5]}-{s[5..]}" : s;
    }

    private static string NomeDoSexo(IDictionary<string, object> r) => T(r, "Sexo") switch { "M" => "Masculino", "F" => "Feminino", "O" => "Outro / não informado", _ => string.Empty };

    private static string CidadeUf(IDictionary<string, object> r) => string.Join("/", new[] { T(r, "Cidade"), T(r, "Uf") }.Where(x => x.Length > 0));

    private static string ValidarFiltroAtivo(FiltroRelatorio f) => f.Ativo switch { true => "Ativos", false => "Inativos", _ => "Todos" };

    private static void AcrescentarAtivo(StringBuilder onde, DynamicParameters p, FiltroRelatorio f, string coluna)
    {
        if (!f.Ativo.HasValue) return;
        onde.Append($" AND {coluna} = @Ativo");
        p.Add("Ativo", f.Ativo.Value);
    }

    private static List<ColunaTabela> Cols(params string[] titulos) => titulos.Select(ColunaTabela.Texto).ToList();

    // ------------------------------------------------------------ definição de cada relatório

    private async Task<DefinicaoCadastro> DefinirCadastroAsync(SqlConnection con, string tipo, FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        var p = new DynamicParameters();
        var onde = new StringBuilder(" WHERE 1 = 1");
        var filtros = new List<string>();

        switch (tipo)
        {
            case "pacientes":
            {
                AcrescentarAtivo(onde, p, f, "P.ATIVO");
                filtros.Add("Situação: " + ValidarFiltroAtivo(f));
                if (f.Estrangeiro.HasValue)
                {
                    onde.Append(" AND P.ESTRANGEIRO = @Estrangeiro");
                    p.Add("Estrangeiro", f.Estrangeiro.Value);
                }
                filtros.Add("Pacientes: " + (f.Estrangeiro switch { true => "Estrangeiros", false => "Nacionais", _ => "Nacionais e estrangeiros" }));
                if (f.ProfissionalId.HasValue)
                {
                    var nome = await con.QueryFirstOrDefaultAsync<string?>(Sql.Cmd("SELECT NOME FROM DBO.PROFISSIONAL WHERE CODIGO_PROFISSIONAL = @Id;", new { Id = f.ProfissionalId.Value }, ct: ct))
                        ?? throw Erro.Validacao("profissionalId", "Profissional não encontrado");
                    onde.Append(" AND P.CODIGO_PROFISSIONAL_RESPONSAVEL = @Prof");
                    p.Add("Prof", f.ProfissionalId.Value);
                    filtros.Add("Profissional responsável: " + nome);
                }
                else filtros.Add("Profissional responsável: Todos");
                if (f.ConvenioId.HasValue)
                {
                    var nome = await con.QueryFirstOrDefaultAsync<string?>(Sql.Cmd("SELECT COALESCE(NOME_FANTASIA, RAZAO_SOCIAL) FROM DBO.CONVENIO WHERE CODIGO_CONVENIO = @Id;", new { Id = f.ConvenioId.Value }, ct: ct))
                        ?? throw Erro.Validacao("convenioId", "Convênio não encontrado");
                    onde.Append(" AND P.CODIGO_CONVENIO = @Conv");
                    p.Add("Conv", f.ConvenioId.Value);
                    filtros.Add("Convênio: " + nome);
                }
                else filtros.Add("Convênio: Todos");

                return new DefinicaoCadastro
                {
                    Titulo = "Pacientes",
                    Entidade = "RELATORIO_CADASTRO_PACIENTES",
                    Colunas = Cols("Nome", "Situação", "CPF", "Tipo de documento", "Número do documento", "RG", "Sexo", "Nascimento", "Menor de idade", "Nome da mãe", "Nome do pai", "Telefone", "E-mail", "Endereço", "Bairro", "Cidade/UF", "CEP", "País", "Escolaridade",
                        "Convênio", "Carteirinha", "Validade da carteirinha", "Profissional responsável", "Responsável legal", "CPF do responsável", "Telefone do responsável", "Cadastrado em", "Atualizado em"),
                    Select = @"
SELECT P.NOME AS Nome, P.ATIVO AS Ativo, P.CPF AS Cpf, P.TIPO_DOCUMENTO_ESTRANGEIRO AS TipoDoc, P.NUMERO_DOCUMENTO_ESTRANGEIRO AS NumeroDoc, P.RG AS Rg,
       P.DATA_NASCIMENTO AS Nascimento, P.TELEFONE AS Telefone, P.EMAIL AS Email, P.ENDERECO AS Endereco, P.NUMERO AS Numero, P.COMPLEMENTO AS Complemento,
       P.CIDADE AS Cidade, P.UF AS Uf, P.ESCOLARIDADE AS Escolaridade, P.SEXO AS Sexo, P.NOME_MAE AS NomeMae, P.MAE_NAO_POSSUI AS MaeNaoPossui, P.NOME_PAI AS NomePai,
       P.BAIRRO AS Bairro, P.CEP AS Cep, P.PAIS AS Pais, COALESCE(C.NOME_FANTASIA, C.RAZAO_SOCIAL) AS Convenio,
       P.NUMERO_CARTEIRINHA AS Carteirinha, P.DATA_VALIDADE_CARTEIRINHA AS ValidadeCarteirinha, PR.NOME AS Profissional,
       R.NOME AS Responsavel, R.CPF AS CpfResponsavel, R.TELEFONE AS TelefoneResponsavel, P.DATA_CRIACAO AS CriadoEm, P.DATA_ATUALIZACAO AS AtualizadoEm",
                    De = @"
FROM DBO.PACIENTE P
LEFT JOIN DBO.CONVENIO C ON C.CODIGO_CONVENIO = P.CODIGO_CONVENIO
JOIN DBO.PROFISSIONAL PR ON PR.CODIGO_PROFISSIONAL = P.CODIGO_PROFISSIONAL_RESPONSAVEL
LEFT JOIN DBO.RESPONSAVEL_LEGAL R ON R.CODIGO_RESPONSAVEL = P.CODIGO_RESPONSAVEL_LEGAL",
                    Onde = onde.ToString(),
                    Ordem = " ORDER BY P.NOME, P.CODIGO_PACIENTE",
                    Parametros = p,
                    Filtros = filtros,
                    Linha = r =>
                    {
                        var nasc = V(r, "Nascimento") as DateTime?;
                        var hoje = DataBrasil.Hoje();
                        var menor = nasc.HasValue && nasc.Value.Date.AddYears(18) > hoje.ToDateTime(TimeOnly.MinValue);
                        return new List<string>
                        {
                            T(r, "Nome"), Situacao(r), Cpf(r, "Cpf"), T(r, "TipoDoc"), T(r, "NumeroDoc"), T(r, "Rg"), NomeDoSexo(r), Dt(r, "Nascimento"), nasc.HasValue ? (menor ? "Sim" : "Não") : string.Empty,
                            V(r, "MaeNaoPossui") is true ? "Não possui" : T(r, "NomeMae"), T(r, "NomePai"),
                            T(r, "Telefone"), T(r, "Email"), Endereco(r), T(r, "Bairro"), CidadeUf(r), Cep(r, "Cep"), T(r, "Pais"), T(r, "Escolaridade"),
                            T(r, "Convenio"), T(r, "Carteirinha"), Dt(r, "ValidadeCarteirinha"), T(r, "Profissional"),
                            T(r, "Responsavel"), Cpf(r, "CpfResponsavel"), T(r, "TelefoneResponsavel"), DtHora(r, "CriadoEm"), DtHora(r, "AtualizadoEm")
                        };
                    }
                };
            }

            case "profissionais":
            {
                AcrescentarAtivo(onde, p, f, "P.ATIVO");
                filtros.Add("Situação: " + ValidarFiltroAtivo(f));
                if (f.UnidadeId.HasValue)
                {
                    var nome = await con.QueryFirstOrDefaultAsync<string?>(Sql.Cmd("SELECT NOME_FANTASIA FROM DBO.UNIDADE WHERE CODIGO_UNIDADE = @Id;", new { Id = f.UnidadeId.Value }, ct: ct))
                        ?? throw Erro.Validacao("unidadeId", "Unidade não encontrada");
                    onde.Append(" AND EXISTS (SELECT 1 FROM DBO.PROFISSIONAL_UNIDADE PU WHERE PU.CODIGO_PROFISSIONAL = P.CODIGO_PROFISSIONAL AND PU.CODIGO_UNIDADE = @Unidade)");
                    p.Add("Unidade", f.UnidadeId.Value);
                    filtros.Add("Unidade: " + nome);
                }
                else filtros.Add("Unidade: Todas");

                return new DefinicaoCadastro
                {
                    Titulo = "Profissionais",
                    Entidade = "RELATORIO_CADASTRO_PROFISSIONAIS",
                    Colunas = Cols("Nome", "Situação", "CPF", "RG", "Nascimento", "Registro profissional", "Especialidades", "Unidades", "Telefone", "E-mail", "Endereço", "Cidade/UF",
                        "Escolaridade", "Cadastrado em", "Atualizado em"),
                    Select = @"
SELECT P.NOME AS Nome, P.ATIVO AS Ativo, P.CPF AS Cpf, P.RG AS Rg, P.DATA_NASCIMENTO AS Nascimento,
       P.REGISTRO_PROFISSIONAL + ' ' + P.NUMERO_CONSELHO + '/' + P.UF_CONSELHO AS Registro,
       STUFF((SELECT ', ' + E.NOME FROM DBO.PROFISSIONAL_ESPECIALIDADE PE JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = PE.CODIGO_ESPECIALIDADE
              WHERE PE.CODIGO_PROFISSIONAL = P.CODIGO_PROFISSIONAL ORDER BY E.NOME FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS Especialidades,
       STUFF((SELECT ', ' + U.NOME_FANTASIA FROM DBO.PROFISSIONAL_UNIDADE PU JOIN DBO.UNIDADE U ON U.CODIGO_UNIDADE = PU.CODIGO_UNIDADE
              WHERE PU.CODIGO_PROFISSIONAL = P.CODIGO_PROFISSIONAL ORDER BY U.NOME_FANTASIA FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS Unidades,
       P.TELEFONE AS Telefone, P.EMAIL AS Email, P.ENDERECO AS Endereco, P.NUMERO AS Numero, P.COMPLEMENTO AS Complemento, P.CIDADE AS Cidade, P.UF AS Uf,
       P.ESCOLARIDADE AS Escolaridade, P.DATA_CRIACAO AS CriadoEm, P.DATA_ATUALIZACAO AS AtualizadoEm",
                    De = "\nFROM DBO.PROFISSIONAL P",
                    Onde = onde.ToString(),
                    Ordem = " ORDER BY P.NOME, P.CODIGO_PROFISSIONAL",
                    Parametros = p,
                    Filtros = filtros,
                    Linha = r => new List<string>
                    {
                        T(r, "Nome"), Situacao(r), Cpf(r, "Cpf"), T(r, "Rg"), Dt(r, "Nascimento"), T(r, "Registro"), T(r, "Especialidades"), T(r, "Unidades"),
                        T(r, "Telefone"), T(r, "Email"), Endereco(r), CidadeUf(r), T(r, "Escolaridade"), DtHora(r, "CriadoEm"), DtHora(r, "AtualizadoEm")
                    }
                };
            }

            case "colaboradores":
            {
                AcrescentarAtivo(onde, p, f, "C.ATIVO");
                filtros.Add("Situação: " + ValidarFiltroAtivo(f));
                var vinculo = string.IsNullOrWhiteSpace(f.Vinculo) ? null : f.Vinculo.Trim().ToUpperInvariant();
                if (vinculo is not null and not ("CLT" or "MEI")) throw Erro.Validacao("vinculo", "Informe CLT ou MEI");
                if (vinculo is not null)
                {
                    onde.Append(" AND C.TIPO_VINCULO = @Vinculo");
                    p.Add("Vinculo", vinculo);
                }
                filtros.Add("Vínculo: " + (vinculo ?? "Todos"));

                // Os dados trabalhistas (CTPS, PIS/PASEP, admissão) só saem para quem também pode ver o cadastro de colaboradores.
                var comClt = await _permissoes.TemAsync(usuario.PerfilId, usuario.Administrador, "colaborador.cadastro:visualizar", ct);
                var colunas = Cols("Nome / razão social", "Vínculo", "Situação", "CPF", "CNPJ", "Nome fantasia", "Cargo", "Telefone", "E-mail", "Endereço", "Cidade/UF");
                if (comClt) colunas.AddRange(Cols("CTPS", "PIS/PASEP", "Admissão"));
                colunas.AddRange(Cols("Cadastrado em", "Atualizado em"));
                if (!comClt) filtros.Add("Dados trabalhistas (CTPS, PIS/PASEP e admissão) ocultos: o perfil não tem acesso ao cadastro de colaboradores");

                return new DefinicaoCadastro
                {
                    Titulo = "Colaboradores e prestadores",
                    Entidade = "RELATORIO_CADASTRO_COLABORADORES",
                    Colunas = colunas,
                    Select = @"
SELECT C.NOME AS Nome, C.RAZAO_SOCIAL AS RazaoSocial, C.NOME_FANTASIA AS NomeFantasia, C.TIPO_VINCULO AS Vinculo, C.ATIVO AS Ativo, C.CPF AS Cpf, C.CNPJ AS Cnpj,
       C.CARGO AS Cargo, C.TELEFONE AS Telefone, C.EMAIL AS Email, C.ENDERECO AS Endereco, C.NUMERO AS Numero, C.COMPLEMENTO AS Complemento, C.CIDADE AS Cidade, C.UF AS Uf,
       C.CTPS_NUMERO AS CtpsNumero, C.CTPS_SERIE AS CtpsSerie, C.CTPS_UF AS CtpsUf, C.CTPS_DATA_EMISSAO AS CtpsEmissao, C.PIS_PASEP AS Pis, C.DATA_ADMISSAO AS Admissao,
       C.DATA_CRIACAO AS CriadoEm, C.DATA_ATUALIZACAO AS AtualizadoEm",
                    De = "\nFROM DBO.COLABORADOR_PRESTADOR C",
                    Onde = onde.ToString(),
                    Ordem = " ORDER BY COALESCE(C.NOME, C.RAZAO_SOCIAL), C.CODIGO_CLB_PRT",
                    Parametros = p,
                    Filtros = filtros,
                    Linha = r =>
                    {
                        var nome = T(r, "Nome").Length > 0 ? T(r, "Nome") : T(r, "RazaoSocial");
                        var linha = new List<string>
                        {
                            nome, T(r, "Vinculo"), Situacao(r), Cpf(r, "Cpf"), Cnpj(r, "Cnpj"), T(r, "NomeFantasia"), T(r, "Cargo"),
                            T(r, "Telefone"), T(r, "Email"), Endereco(r), CidadeUf(r)
                        };
                        if (comClt)
                        {
                            var ctps = T(r, "CtpsNumero").Length == 0 ? string.Empty
                                : $"{T(r, "CtpsNumero")} série {T(r, "CtpsSerie")}/{T(r, "CtpsUf")}" + (V(r, "CtpsEmissao") is null ? string.Empty : $" emitida em {Dt(r, "CtpsEmissao")}");
                            linha.AddRange(new[] { ctps, T(r, "Pis"), Dt(r, "Admissao") });
                        }
                        linha.AddRange(new[] { DtHora(r, "CriadoEm"), DtHora(r, "AtualizadoEm") });
                        return linha;
                    }
                };
            }

            case "convenios":
            {
                AcrescentarAtivo(onde, p, f, "C.ATIVO");
                filtros.Add("Situação: " + ValidarFiltroAtivo(f));
                return new DefinicaoCadastro
                {
                    Titulo = "Convênios",
                    Entidade = "RELATORIO_CADASTRO_CONVENIOS",
                    Colunas = Cols("Nome fantasia", "Razão social", "Situação", "CNPJ", "Código ANS", "Data de inscrição", "Inscrição municipal", "Inscrição estadual", "Especialidades", "Cadastrado em", "Atualizado em"),
                    Select = @"
SELECT C.NOME_FANTASIA AS NomeFantasia, C.RAZAO_SOCIAL AS RazaoSocial, C.ATIVO AS Ativo, C.CNPJ AS Cnpj, C.CODIGO_ANS AS Ans, C.DATA_INSCRICAO AS Inscricao,
       C.INSCRICAO_MUNICIPAL AS InscMunicipal, C.INSCRICAO_ESTADUAL AS InscEstadual,
       STUFF((SELECT ', ' + E.NOME + CASE WHEN CE.VALOR > 0 THEN ' (R$ ' + REPLACE(CONVERT(VARCHAR(20), CE.VALOR), '.', ',') + ')' ELSE '' END
              FROM DBO.CONVENIO_ESPECIALIDADE CE JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = CE.CODIGO_ESPECIALIDADE
              WHERE CE.CODIGO_CONVENIO = C.CODIGO_CONVENIO ORDER BY E.NOME FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS Especialidades,
       C.DATA_CRIACAO AS CriadoEm, C.DATA_ATUALIZACAO AS AtualizadoEm",
                    De = "\nFROM DBO.CONVENIO C",
                    Onde = onde.ToString(),
                    Ordem = " ORDER BY COALESCE(C.NOME_FANTASIA, C.RAZAO_SOCIAL), C.CODIGO_CONVENIO",
                    Parametros = p,
                    Filtros = filtros,
                    Linha = r => new List<string>
                    {
                        T(r, "NomeFantasia").Length > 0 ? T(r, "NomeFantasia") : T(r, "RazaoSocial"), T(r, "RazaoSocial"), Situacao(r), Cnpj(r, "Cnpj"), T(r, "Ans"), Dt(r, "Inscricao"),
                        T(r, "InscMunicipal"), T(r, "InscEstadual"), T(r, "Especialidades"), DtHora(r, "CriadoEm"), DtHora(r, "AtualizadoEm")
                    }
                };
            }

            case "credenciamentos":
            {
                AcrescentarAtivo(onde, p, f, "K.ATIVO");
                filtros.Add("Situação do cadastro: " + ValidarFiltroAtivo(f));
                if (f.ConvenioId.HasValue)
                {
                    var nome = await con.QueryFirstOrDefaultAsync<string?>(Sql.Cmd("SELECT COALESCE(NOME_FANTASIA, RAZAO_SOCIAL) FROM DBO.CONVENIO WHERE CODIGO_CONVENIO = @Id;", new { Id = f.ConvenioId.Value }, ct: ct))
                        ?? throw Erro.Validacao("convenioId", "Convênio não encontrado");
                    onde.Append(" AND K.CODIGO_CONVENIO = @Conv");
                    p.Add("Conv", f.ConvenioId.Value);
                    filtros.Add("Convênio: " + nome);
                }
                else filtros.Add("Convênio: Todos");
                if (f.UnidadeId.HasValue)
                {
                    var nome = await con.QueryFirstOrDefaultAsync<string?>(Sql.Cmd("SELECT NOME_FANTASIA FROM DBO.UNIDADE WHERE CODIGO_UNIDADE = @Id;", new { Id = f.UnidadeId.Value }, ct: ct))
                        ?? throw Erro.Validacao("unidadeId", "Unidade não encontrada");
                    onde.Append(" AND K.CODIGO_UNIDADE = @Unidade");
                    p.Add("Unidade", f.UnidadeId.Value);
                    filtros.Add("Unidade: " + nome);
                }
                else filtros.Add("Unidade: Todas");
                var sit = string.IsNullOrWhiteSpace(f.Situacao) ? null : f.Situacao.Trim().ToUpperInvariant();
                if (sit is not null and not ("APROVADO" or "EM_ANALISE" or "NAO_CREDENCIADO")) throw Erro.Validacao("situacao", "Informe APROVADO, EM_ANALISE ou NAO_CREDENCIADO");
                if (sit is not null)
                {
                    onde.Append(" AND K.SITUACAO = @Sit");
                    p.Add("Sit", sit);
                }
                filtros.Add("Credenciamento: " + (sit is null ? "Todos" : NomeDoCredenciamento(sit)));

                return new DefinicaoCadastro
                {
                    Titulo = "Credenciamentos de convênios",
                    Entidade = "RELATORIO_CADASTRO_CREDENCIAMENTOS",
                    Colunas = Cols("Convênio", "Unidade", "Cidade/UF", "Especialidade", "Credenciamento", "Cadastro", "Início", "Fim", "Protocolo", "Valor da consulta", "Observação", "Atualizado em"),
                    Select = @"
SELECT COALESCE(C.NOME_FANTASIA, C.RAZAO_SOCIAL) AS Convenio, U.NOME_FANTASIA AS Unidade, U.CIDADE AS Cidade, U.UF AS Uf, E.NOME AS Especialidade,
       K.SITUACAO AS Credenciamento, K.ATIVO AS Ativo, K.DATA_INICIO AS Inicio, K.DATA_FIM AS Fim, K.PROTOCOLO AS Protocolo,
       CASE WHEN K.VALOR > 0 THEN K.VALOR ELSE ISNULL(CE.VALOR, 0) END AS Valor, K.OBSERVACAO AS Observacao, K.DATA_ATUALIZACAO AS AtualizadoEm",
                    De = @"
FROM DBO.CREDENCIAMENTO_CONVENIO K
JOIN DBO.CONVENIO C ON C.CODIGO_CONVENIO = K.CODIGO_CONVENIO
JOIN DBO.UNIDADE U ON U.CODIGO_UNIDADE = K.CODIGO_UNIDADE
JOIN DBO.ESPECIALIDADE E ON E.CODIGO_ESPECIALIDADE = K.CODIGO_ESPECIALIDADE
LEFT JOIN DBO.CONVENIO_ESPECIALIDADE CE ON CE.CODIGO_CONVENIO = K.CODIGO_CONVENIO AND CE.CODIGO_ESPECIALIDADE = K.CODIGO_ESPECIALIDADE",
                    Onde = onde.ToString(),
                    Ordem = " ORDER BY COALESCE(C.NOME_FANTASIA, C.RAZAO_SOCIAL), U.NOME_FANTASIA, E.NOME, K.CODIGO_CREDENCIAMENTO",
                    Parametros = p,
                    Filtros = filtros,
                    Linha = r => new List<string>
                    {
                        T(r, "Convenio"), T(r, "Unidade"), CidadeUf(r), T(r, "Especialidade"), NomeDoCredenciamento(T(r, "Credenciamento")), Situacao(r),
                        Dt(r, "Inicio"), Dt(r, "Fim"), T(r, "Protocolo"), Dinheiro(r, "Valor"), T(r, "Observacao"), DtHora(r, "AtualizadoEm")
                    }
                };
            }

            default:
                throw Erro.NaoEncontrado("Relatório não encontrado.", "RELATORIO_NAO_ENCONTRADO");
        }
    }

    private static string NomeDoCredenciamento(string s) => s switch
    {
        "APROVADO" => "Aprovado",
        "EM_ANALISE" => "Em análise",
        "NAO_CREDENCIADO" => "Não credenciado",
        _ => s
    };

    // ------------------------------------------------------------ organização da tela (resumo e detalhes por seção)

    private static readonly Dictionary<string, (string[] Resumo, (string Grupo, string[] Campos)[] Grupos)> Layouts = new()
    {
        ["pacientes"] = (
            new[] { "Nome", "Situação", "CPF", "Nascimento", "Menor de idade", "Telefone", "Convênio", "Profissional responsável" },
            new[]
            {
                ("Identificação", new[] { "Nome", "Situação", "CPF", "Tipo de documento", "Número do documento", "RG", "Sexo", "Nascimento", "Menor de idade", "Escolaridade" }),
                ("Filiação", new[] { "Nome da mãe", "Nome do pai" }),
                ("Contato", new[] { "Telefone", "E-mail" }),
                ("Endereço", new[] { "Endereço", "Bairro", "Cidade/UF", "CEP", "País" }),
                ("Convênio", new[] { "Convênio", "Carteirinha", "Validade da carteirinha" }),
                ("Responsáveis", new[] { "Profissional responsável", "Responsável legal", "CPF do responsável", "Telefone do responsável" }),
                ("Registro", new[] { "Cadastrado em", "Atualizado em" })
            }),
        ["profissionais"] = (
            new[] { "Nome", "Situação", "CPF", "Registro profissional", "Especialidades", "Telefone" },
            new[]
            {
                ("Identificação", new[] { "Nome", "Situação", "CPF", "RG", "Nascimento", "Escolaridade" }),
                ("Atuação", new[] { "Registro profissional", "Especialidades", "Unidades" }),
                ("Contato", new[] { "Telefone", "E-mail" }),
                ("Endereço", new[] { "Endereço", "Cidade/UF" }),
                ("Registro", new[] { "Cadastrado em", "Atualizado em" })
            }),
        ["colaboradores"] = (
            new[] { "Nome / razão social", "Vínculo", "Situação", "CPF", "CNPJ", "Cargo", "Telefone" },
            new[]
            {
                ("Identificação", new[] { "Nome / razão social", "Vínculo", "Situação", "CPF", "CNPJ", "Nome fantasia", "Cargo" }),
                ("Contato", new[] { "Telefone", "E-mail" }),
                ("Endereço", new[] { "Endereço", "Cidade/UF" }),
                ("Dados trabalhistas", new[] { "CTPS", "PIS/PASEP", "Admissão" }),
                ("Registro", new[] { "Cadastrado em", "Atualizado em" })
            }),
        ["convenios"] = (
            new[] { "Nome fantasia", "Razão social", "Situação", "CNPJ", "Código ANS" },
            new[]
            {
                ("Identificação", new[] { "Nome fantasia", "Razão social", "Situação", "CNPJ", "Código ANS", "Data de inscrição", "Inscrição municipal", "Inscrição estadual" }),
                ("Especialidades atendidas", new[] { "Especialidades" }),
                ("Registro", new[] { "Cadastrado em", "Atualizado em" })
            }),
        ["credenciamentos"] = (
            new[] { "Convênio", "Unidade", "Especialidade", "Credenciamento", "Cadastro", "Início", "Fim", "Valor da consulta" },
            new[]
            {
                ("Credenciamento", new[] { "Convênio", "Unidade", "Cidade/UF", "Especialidade", "Credenciamento", "Cadastro" }),
                ("Vigência e valor", new[] { "Início", "Fim", "Protocolo", "Valor da consulta" }),
                ("Observação", new[] { "Observação", "Atualizado em" })
            })
    };

    private static void AplicarLayout(string tipo, List<ColunaTabela> colunas, FiltroRelatorio f)
    {
        if (!Layouts.TryGetValue(tipo, out var layout)) return;
        // Pacientes: o documento do estrangeiro só entra na tabela resumida quando o filtro de nacionalidade pede (ou mostra todos).
        var extras = tipo == "pacientes"
            ? f.Estrangeiro switch { true => new[] { "Tipo de documento", "Número do documento" }, null => new[] { "Número do documento" }, _ => Array.Empty<string>() }
            : Array.Empty<string>();
        foreach (var c in colunas)
        {
            c.Resumo = layout.Resumo.Contains(c.Titulo) || extras.Contains(c.Titulo);
            c.Grupo = layout.Grupos.FirstOrDefault(g => g.Campos.Contains(c.Titulo)).Grupo ?? "Outros";
        }
    }

    // ------------------------------------------------------------ consulta paginada e exportação

    private async Task<RelatorioCadastroDto> ConsultarCadastroAsync(SqlConnection con, DefinicaoCadastro d, int deslocamento, int tamanho, CancellationToken ct)
    {
        var total = await con.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1)" + d.De + d.Onde + ";", d.Parametros, ct: ct));

        var p = new DynamicParameters(d.Parametros);
        p.Add("Deslocamento", deslocamento);
        p.Add("Tamanho", tamanho);
        var linhas = (await con.QueryAsync(Sql.Cmd(d.Select + d.De + d.Onde + d.Ordem + " OFFSET @Deslocamento ROWS FETCH NEXT @Tamanho ROWS ONLY;", p, ct: ct))).ToList();

        return new RelatorioCadastroDto
        {
            Titulo = d.Titulo,
            Filtros = d.Filtros,
            Colunas = d.Colunas,
            Linhas = linhas.Select(r => d.Linha((IDictionary<string, object>)r)).ToList(),
            Total = total,
            Pagina = deslocamento / Math.Max(1, tamanho) + 1,
            Tamanho = tamanho,
            GeradoEm = DateTime.UtcNow
        };
    }

    public async Task<RelatorioCadastroDto> CadastroAsync(string tipo, FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        if (f.Pagina < 1) throw Erro.Validacao("pagina", "Deve ser 1 ou mais");
        if (f.Tamanho is < 1 or > 100) throw Erro.Validacao("tamanho", "Deve estar entre 1 e 100");

        await using var con = await _db.AbrirAsync(ct);
        var d = await DefinirCadastroAsync(con, tipo, f, usuario, ct);
        AplicarLayout(tipo, d.Colunas, f);
        var r = await ConsultarCadastroAsync(con, d, (f.Pagina - 1) * f.Tamanho, f.Tamanho, ct);

        await _auditoria.RegistrarAsync("VISUALIZAR", d.Entidade, null, null, usuario.Id,
            new { ativo = f.Ativo, estrangeiro = f.Estrangeiro, profissional = f.ProfissionalId, convenio = f.ConvenioId, unidade = f.UnidadeId, vinculo = f.Vinculo, situacao = f.Situacao, pagina = f.Pagina }, con, ct: ct);
        return r;
    }

    private async Task<TabelaExportacao> TabelaCadastroAsync(string tipo, FiltroRelatorio f, UsuarioAtual usuario, CancellationToken ct)
    {
        ExigirNaoMedico(usuario);
        await using var con = await _db.AbrirAsync(ct);
        var d = await DefinirCadastroAsync(con, tipo, f, usuario, ct);
        var r = await ConsultarCadastroAsync(con, d, 0, LimiteExportacao + 1, ct);
        if (r.Total > LimiteExportacao) throw Erro.Regra("EXPORTACAO_MUITO_GRANDE", "O relatório passa de 10.000 linhas. Reduza o período ou use filtros.");

        return new TabelaExportacao { Titulo = d.Titulo, Filtros = d.Filtros, Colunas = d.Colunas, Linhas = r.Linhas, Totais = null };
    }
}
