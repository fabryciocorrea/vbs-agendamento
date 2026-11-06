import { useMemo, useState, type ReactNode } from "react";
import { Outlet, useLocation, useNavigate } from "react-router-dom";
import { Button, Layout, Menu, Space, Typography } from "antd";
import {
  ApartmentOutlined,
  BankOutlined,
  HomeOutlined,
  CreditCardOutlined,
  IdcardOutlined,
  LogoutOutlined,
  MedicineBoxOutlined,
  SafetyOutlined,
  SolutionOutlined,
  TeamOutlined,
  UserOutlined,
  ContactsOutlined,
  CalendarOutlined,
  SettingOutlined,
  SafetyCertificateOutlined,
  DollarOutlined,
  ClockCircleOutlined,
  LineChartOutlined,
  BarChartOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  GiftOutlined,
  FolderOpenOutlined,
} from "@ant-design/icons";
import { useAuth } from "../auth/AuthContext";
import { cores } from "../tema";
import SinoNotificacoes from "./SinoNotificacoes";
import MenuUsuario from "./MenuUsuario";
import BotaoTema from "./BotaoTema";

const { Sider, Header, Content } = Layout;

interface ItemMenu {
  key: string;
  label: string;
  icon: ReactNode;
  tela?: string | string[];
}

// Cada item só aparece se o perfil puder visualizar a tela correspondente.
// Novas telas entram aqui conforme as fatias forem sendo entregues.
const ITENS: ItemMenu[] = [
  { key: "/", label: "Início", icon: <HomeOutlined /> },
  {
    key: "/agenda",
    label: "Agenda",
    icon: <CalendarOutlined />,
    tela: "agenda.profissional",
  },
  {
    key: "/lista-espera",
    label: "Lista de espera",
    icon: <ClockCircleOutlined />,
    tela: "listaespera.cadastro",
  },
  {
    key: "/pacientes",
    label: "Pacientes",
    icon: <SolutionOutlined />,
    tela: "paciente.cadastro",
  },
  {
    key: "/convenios",
    label: "Convênios",
    icon: <IdcardOutlined />,
    tela: "convenio.cadastro",
  },
  {
    key: "/pacotes",
    label: "Pacotes",
    icon: <GiftOutlined />,
    tela: "pacote.venda",
  },
  {
    key: "/credenciamentos",
    label: "Credenciamentos",
    icon: <SafetyCertificateOutlined />,
    tela: "convenio.credenciamento",
  },
  {
    key: "/faturamento",
    label: "Faturamento",
    icon: <DollarOutlined />,
    tela: "faturamento.financeiro",
  },
  {
    key: "/painel-financeiro",
    label: "Painel financeiro",
    icon: <LineChartOutlined />,
    tela: "dashboard.financeiro",
  },
  {
    key: "/documentos",
    label: "Documentos",
    icon: <FolderOpenOutlined />,
    tela: "documento.interno",
  },
  {
    key: "/relatorios",
    label: "Relatórios",
    icon: <BarChartOutlined />,
    tela: [
      "relatorio.consultas_profissional",
      "relatorio.ocupacao",
      "relatorio.cancelamentos_faltas",
      "relatorio.lista_espera",
      "relatorio.faturamento_tipo",
      "relatorio.faturamento_particular",
      "relatorio.faturamento_convenio",
      "relatorio.faturamento_mensal",
      "relatorio.cadastro_pacientes",
      "relatorio.cadastro_profissionais",
      "relatorio.cadastro_colaboradores",
      "relatorio.cadastro_convenios",
      "relatorio.cadastro_credenciamentos",
      "relatorio.prontuarios",
    ],
  },
  {
    key: "/colaboradores",
    label: "Colaboradores",
    icon: <ContactsOutlined />,
    tela: "colaborador.cadastro",
  },
  {
    key: "/profissionais",
    label: "Profissionais",
    icon: <TeamOutlined />,
    tela: "profissional.cadastro",
  },
  {
    key: "/especialidades",
    label: "Especialidades",
    icon: <MedicineBoxOutlined />,
    tela: "especialidade.cadastro",
  },
  {
    key: "/formas-pagamento",
    label: "Formas de pagamento",
    icon: <CreditCardOutlined />,
    tela: "formapagamento.cadastro",
  },
  {
    key: "/unidades",
    label: "Unidades",
    icon: <BankOutlined />,
    tela: "unidade.cadastro",
  },
  {
    key: "/salas",
    label: "Salas",
    icon: <ApartmentOutlined />,
    tela: "sala.cadastro",
  },
  {
    key: "/parametros-agenda",
    label: "Parâmetros da agenda",
    icon: <SettingOutlined />,
    tela: "agenda.parametros",
  },
  {
    key: "/perfis",
    label: "Perfis de acesso",
    icon: <SafetyOutlined />,
    tela: "perfil.cadastro",
  },
  {
    key: "/usuarios",
    label: "Usuários",
    icon: <UserOutlined />,
    tela: "usuario.cadastro",
  },
];

export default function LayoutPrincipal() {
  const { sessao, pode, sair } = useAuth();
  const navegar = useNavigate();
  const { pathname } = useLocation();
  const [recolhido, setRecolhido] = useState(false);

  const itens = useMemo(
    () =>
      ITENS.filter(
        (i) => !i.tela || [i.tela].flat().some((t) => pode(t, "visualizar")),
      ).map(({ key, label, icon }) => ({ key, label, icon })),
    [pode],
  );
  const selecionado =
    itens.find((i) => i.key !== "/" && pathname.startsWith(i.key))?.key ?? "/";

  return (
    <Layout style={{ minHeight: "100vh" }}>
      <Sider
        breakpoint="lg"
        trigger={null}
        collapsed={recolhido}
        width={240}
        collapsedWidth={64}
        theme="dark"
      >
        <div className="logo-menu">
          <img src="/logo-icone.png" alt="Viver Bem Saúde" />
          {!recolhido && (
            <div className="logo-nome">
              <strong>Viver Bem Saúde</strong>
              <small>Gestão de Agendamentos e Consultas</small>
            </div>
          )}
        </div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[selecionado]}
          items={itens}
          onClick={(e) => navegar(e.key)}
        />
      </Sider>
      <Layout className="area-principal">
        <Header
          className="topo-sistema"
          style={{
            background: "var(--vbs-superficie)",
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            padding: "0 24px",
          }}
        >
          <Button
            type="text"
            className="botao-menu"
            aria-label="Recolher ou expandir o menu"
            icon={recolhido ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
            onClick={() => setRecolhido((v) => !v)}
          />
          <Space size="middle">
            <BotaoTema />
            <SinoNotificacoes />
            <MenuUsuario />
          </Space>
        </Header>
        <Content style={{ margin: 24 }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  );
}
