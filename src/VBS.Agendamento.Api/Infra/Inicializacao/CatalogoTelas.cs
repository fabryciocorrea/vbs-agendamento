namespace VBS.Agendamento.Api.Infra.Inicializacao;

/// <summary>
/// Catálogo de telas do sistema (TELA_SISTEMA), extraído das liberações (x-permissao) do contrato OpenAPI 1.0.0-rc.1.
/// O servidor grava estas linhas ao iniciar (insere as novas e atualiza nome e módulo das existentes).
/// Os nomes e os módulos são provisórios: ajuste quando o catálogo de telas e ações for aprovado.
/// </summary>
public static class CatalogoTelas
{
    public static readonly IReadOnlyList<(string Codigo, string Nome, string Modulo)> Telas = new (string, string, string)[]
    {
        ("agenda.parametros", "Parâmetros da agenda", "Agenda"),
        ("agenda.profissional", "Agenda do profissional", "Agenda"),
        ("apoio.listas", "Listas de apoio", "Cadastros"),
        ("auditoria.log", "Log de acessos", "Auditoria"),
        ("colaborador.cadastro", "Cadastro de colaboradores e prestadores", "Cadastros"),
        ("consulta.agendamento", "Agendamento de consultas", "Agenda"),
        ("consulta.status", "Status da consulta", "Agenda"),
        ("convenio.cadastro", "Cadastro de convênios", "Cadastros"),
        ("convenio.credenciamento", "Credenciamento de convênios", "Cadastros"),
        ("dashboard.financeiro", "Painel financeiro", "Dashboards"),
        ("dashboard.painel", "Painel do dia", "Dashboards"),
        ("especialidade.cadastro", "Cadastro de especialidades", "Cadastros"),
        ("faturamento.guias", "Faturamento de guias de convênio", "Faturamento"),
        ("formapagamento.cadastro", "Cadastro de formas de pagamento", "Cadastros"),
        ("listaespera.cadastro", "Lista de espera", "Agenda"),
        ("notificacao.interna", "Notificações internas", "Notificações"),
        ("notificacao.parametros", "Parâmetros das notificações", "Notificações"),
        ("paciente.cadastro", "Cadastro de pacientes", "Cadastros"),
        ("perfil.cadastro", "Perfis de acesso", "Segurança"),
        ("profissional.cadastro", "Cadastro de profissionais", "Cadastros"),
        ("prontuario.correcao", "Correções do prontuário", "Prontuário"),
        ("prontuario.registro", "Registros do prontuário", "Prontuário"),
        ("relatorio.cancelamentos_faltas", "Relatório de cancelamentos e faltas", "Relatórios"),
        ("relatorio.consultas_profissional", "Relatório de consultas por profissional", "Relatórios"),
        ("relatorio.exportacao", "Exportação de relatórios", "Relatórios"),
        ("relatorio.faturamento_mensal", "Relatório de faturamento mensal", "Relatórios"),
        ("relatorio.faturamento_tipo", "Relatório de faturamento por tipo", "Relatórios"),
        ("relatorio.ocupacao", "Relatório de ocupação", "Relatórios"),
        ("relatorio.prontuarios", "Relatório de prontuários", "Relatórios"),
        ("sala.cadastro", "Cadastro de salas", "Cadastros"),
        ("unidade.cadastro", "Cadastro de unidades", "Cadastros"),
        ("usuario.cadastro", "Cadastro de usuários", "Segurança"),
    };
}
