using VBS.Agendamento.Api.Common;

namespace VBS.Agendamento.Api.Features.Pacientes;

/// <summary>Regras de validação do cadastro de paciente (RN25, E3, E4). Só olha o corpo da requisição; as conferências no banco ficam no serviço.</summary>
internal static class PacienteValidador
{
    private const string NaoPreenchido = "Campo Não Preenchido";

    public static List<ErroCampo> ValidarBase(PacienteBaseEntrada d, DateOnly hoje)
    {
        var e = new List<ErroCampo>();

        void Obrigatorio(string campo, string? valor, int max)
        {
            if (string.IsNullOrWhiteSpace(valor)) e.Add(new ErroCampo(campo, NaoPreenchido));
            else if (valor.Trim().Length > max) e.Add(new ErroCampo(campo, $"Máximo de {max} caracteres"));
        }

        void Opcional(string campo, string? valor, int max)
        {
            if (!string.IsNullOrWhiteSpace(valor) && valor.Trim().Length > max)
                e.Add(new ErroCampo(campo, $"Máximo de {max} caracteres"));
        }

        Obrigatorio("nome", d.Nome, 150);
        Obrigatorio("endereco", d.Endereco, 250);
        Obrigatorio("numero", d.Numero, 10);
        Opcional("complemento", d.Complemento, 60);
        Obrigatorio("cidade", d.Cidade, 80);
        Opcional("rg", d.Rg, 20);
        Opcional("escolaridade", d.Escolaridade, 40);

        // Data de nascimento e idade (menor de 18 anos, calculada no horário de Brasília).
        var menor = false;
        if (d.DataNascimento is null)
        {
            e.Add(new ErroCampo("dataNascimento", NaoPreenchido));
        }
        else if (d.DataNascimento.Value > hoje || d.DataNascimento.Value < new DateOnly(1900, 1, 1))
        {
            e.Add(new ErroCampo("dataNascimento", "Data inválida"));
        }
        else
        {
            menor = DataBrasil.Idade(d.DataNascimento.Value, hoje) < 18;
        }

        // Telefone e e-mail: obrigatórios para o maior de idade; o menor pode não ter (o contato é o do responsável legal).
        if (string.IsNullOrWhiteSpace(d.Telefone))
        {
            if (!menor) e.Add(new ErroCampo("telefone", NaoPreenchido));
        }
        else if (!Formatos.Telefone.IsMatch(d.Telefone)) e.Add(new ErroCampo("telefone", "Informe somente dígitos, com DDD (10 a 13 números)"));

        if (string.IsNullOrWhiteSpace(d.Email))
        {
            if (!menor) e.Add(new ErroCampo("email", NaoPreenchido));
        }
        else if (d.Email.Trim().Length > 150 || !Formatos.Email.IsMatch(d.Email.Trim())) e.Add(new ErroCampo("email", "E-mail inválido"));

        // UF.
        if (string.IsNullOrWhiteSpace(d.Uf)) e.Add(new ErroCampo("uf", NaoPreenchido));
        else if (!Formatos.Ufs.Contains(d.Uf)) e.Add(new ErroCampo("uf", "UF inválida"));

        // RN25: CPF e documento do estrangeiro.
        var estrangeiro = d.Estrangeiro ?? false;
        var temCpf = !string.IsNullOrWhiteSpace(d.Cpf);
        var temTipoDoc = !string.IsNullOrWhiteSpace(d.TipoDocumentoEstrangeiro);
        var temNumeroDoc = !string.IsNullOrWhiteSpace(d.NumeroDocumentoEstrangeiro);

        if (temCpf && !CpfUtil.Valido(d.Cpf)) e.Add(new ErroCampo("cpf", "CPF inválido"));

        if (estrangeiro)
        {
            if (!temCpf)
            {
                if (!temTipoDoc) e.Add(new ErroCampo("tipoDocumentoEstrangeiro", NaoPreenchido));
                if (!temNumeroDoc) e.Add(new ErroCampo("numeroDocumentoEstrangeiro", NaoPreenchido));
            }
            Opcional("tipoDocumentoEstrangeiro", d.TipoDocumentoEstrangeiro, 30);
            Opcional("numeroDocumentoEstrangeiro", d.NumeroDocumentoEstrangeiro, 30);
        }
        else
        {
            if (temTipoDoc) e.Add(new ErroCampo("tipoDocumentoEstrangeiro", "Só vale para paciente estrangeiro"));
            if (temNumeroDoc) e.Add(new ErroCampo("numeroDocumentoEstrangeiro", "Só vale para paciente estrangeiro"));

            // CPF é obrigatório para todo paciente brasileiro, inclusive o menor de idade.
            if (!temCpf) e.Add(new ErroCampo("cpf", NaoPreenchido));
        }

        // E4: menor de 18 anos exige responsável legal.
        if (menor && d.ResponsavelLegalId is null)
            e.Add(new ErroCampo("responsavelLegalId", NaoPreenchido));

        // Convênio, número e validade da carteirinha andam juntos.
        if (d.Convenio is not null)
        {
            if (d.Convenio.ConvenioId is null or <= 0) e.Add(new ErroCampo("convenio.convenioId", NaoPreenchido));
            if (string.IsNullOrWhiteSpace(d.Convenio.NumeroCarteirinha)) e.Add(new ErroCampo("convenio.numeroCarteirinha", NaoPreenchido));
            else if (d.Convenio.NumeroCarteirinha.Trim().Length > 30) e.Add(new ErroCampo("convenio.numeroCarteirinha", "Máximo de 30 caracteres"));
            if (d.Convenio.DataValidadeCarteirinha is null) e.Add(new ErroCampo("convenio.dataValidadeCarteirinha", NaoPreenchido));
        }

        return e;
    }

    public static List<ErroCampo> ValidarCadastro(PacienteEntrada d, DateOnly hoje)
    {
        var e = ValidarBase(d, hoje);

        if (d.ProfissionalResponsavelId is null or <= 0)
            e.Add(new ErroCampo("profissionalResponsavelId", NaoPreenchido));

        // E3: um item por finalidade; TRATAMENTO_DADOS precisa estar concedido.
        if (d.Consentimentos is null || d.Consentimentos.Count != 2)
        {
            e.Add(new ErroCampo("consentimentos", "Informe os dois consentimentos: TRATAMENTO_DADOS e CONTATO_SMS_WHATSAPP"));
            return e;
        }

        var finalidades = new HashSet<string>();
        for (var i = 0; i < d.Consentimentos.Count; i++)
        {
            var c = d.Consentimentos[i];
            var prefixo = $"consentimentos[{i}]";

            if (c.Finalidade is not ("TRATAMENTO_DADOS" or "CONTATO_SMS_WHATSAPP"))
                e.Add(new ErroCampo($"{prefixo}.finalidade", "Finalidade inválida"));
            else if (!finalidades.Add(c.Finalidade))
                e.Add(new ErroCampo($"{prefixo}.finalidade", "Finalidade repetida"));

            if (c.Concedido is null) e.Add(new ErroCampo($"{prefixo}.concedido", NaoPreenchido));

            // A origem SISTEMA é reservada ao servidor.
            if (c.Origem is not ("PRESENCIAL" or "TELEFONE"))
                e.Add(new ErroCampo($"{prefixo}.origem", "Informe PRESENCIAL ou TELEFONE"));
        }

        var tratamento = d.Consentimentos.FirstOrDefault(c => c.Finalidade == "TRATAMENTO_DADOS");
        if (tratamento is not null && tratamento.Concedido != true)
            e.Add(new ErroCampo("consentimentos", "O consentimento de tratamento de dados é obrigatório para o cadastro"));

        return e;
    }
}
