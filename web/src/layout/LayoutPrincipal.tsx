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
} from "@ant-design/icons";
import { useAuth } from "../auth/AuthContext";
import { cores } from "../tema";

const { Sider, Header, Content } = Layout;

interface ItemMenu {
  key: string;
  label: string;
  icon: ReactNode;
  tela?: string;
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
      ITENS.filter((i) => !i.tela || pode(i.tela, "visualizar")).map(
        ({ key, label, icon }) => ({ key, label, icon }),
      ),
    [pode],
  );
  const selecionado =
    itens.find((i) => i.key !== "/" && pathname.startsWith(i.key))?.key ?? "/";

  return (
    <Layout style={{ minHeight: "100vh" }}>
      <Sider
        breakpoint="lg"
        collapsible
        collapsed={recolhido}
        onCollapse={setRecolhido}
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
      <Layout style={{ background: cores.fundo }}>
        <Header
          style={{
            background: "#fff",
            borderBottom: `3px solid ${cores.amarelo}`,
            display: "flex",
            justifyContent: "flex-end",
            alignItems: "center",
            padding: "0 24px",
          }}
        >
          <Space size="middle">
            <Typography.Text>
              {sessao?.usuario.nomeCompleto}{" "}
              <Typography.Text type="secondary">
                ({sessao?.usuario.perfil.nome})
              </Typography.Text>
            </Typography.Text>
            <Button icon={<LogoutOutlined />} onClick={() => void sair()}>
              Sair
            </Button>
          </Space>
        </Header>
        <Content style={{ margin: 24 }}>
          <Outlet />
        </Content>
      </Layout>
    </Layout>
  );
}
