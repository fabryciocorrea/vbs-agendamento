import { useState } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { Alert, Button, Card, Form, Input, Typography } from "antd";
import { LockOutlined, UserOutlined } from "@ant-design/icons";
import { useAuth } from "../auth/AuthContext";
import { ApiErro } from "../api/cliente";
import { cores } from "../tema";

interface Valores {
  nomeUsuario: string;
  senha: string;
}

function mensagemDoErro(e: unknown): string {
  if (e instanceof ApiErro) {
    if (e.status === 401) return "Usuário ou senha inválidos.";
    if (e.status === 423)
      return "Usuário bloqueado por tentativas excessivas. Procure o administrador.";
    if (e.status === 429)
      return "Muitas tentativas. Aguarde um minuto e tente novamente.";
    return e.message;
  }
  return "Erro inesperado ao entrar.";
}

export default function Login() {
  const { sessao, entrar } = useAuth();
  const navegar = useNavigate();
  const local = useLocation();
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const destino = (local.state as { de?: string } | null)?.de ?? "/";

  if (sessao) return <Navigate to={destino} replace />;

  async function aoEnviar(v: Valores) {
    setEnviando(true);
    setErro(null);
    try {
      await entrar(v.nomeUsuario.trim(), v.senha);
      navegar(destino, { replace: true });
    } catch (e) {
      setErro(mensagemDoErro(e));
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="tela-login">
      <Card style={{ width: 380, borderTop: `4px solid ${cores.amarelo}` }}>
        <img
          className="logo-login"
          src="/logo-completo.png"
          alt="Viver Bem - Centro Integrado de Psicologia"
        />
        <Typography.Title
          level={4}
          style={{ textAlign: "center", marginTop: 0 }}
        >
          Viver Bem Saúde - Gestão de Agendamento de Consultas
        </Typography.Title>
        {erro && (
          <Alert
            type="error"
            showIcon
            message={erro}
            style={{ marginBottom: 16 }}
          />
        )}
        <Form<Valores>
          layout="vertical"
          onFinish={aoEnviar}
          requiredMark={false}
        >
          <Form.Item
            name="nomeUsuario"
            label="Usuário"
            rules={[{ required: true, message: "Campo Não Preenchido" }]}
          >
            <Input
              prefix={<UserOutlined />}
              autoFocus
              autoComplete="username"
            />
          </Form.Item>
          <Form.Item
            name="senha"
            label="Senha"
            rules={[{ required: true, message: "Campo Não Preenchido" }]}
          >
            <Input.Password
              prefix={<LockOutlined />}
              autoComplete="current-password"
            />
          </Form.Item>
          <Button type="primary" htmlType="submit" block loading={enviando}>
            Entrar
          </Button>
        </Form>
      </Card>
    </div>
  );
}
