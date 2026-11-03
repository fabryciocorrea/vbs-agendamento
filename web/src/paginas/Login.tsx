import { useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { Alert, App, Button, Checkbox, Form, Input, Modal } from 'antd';
import { LockOutlined, UserOutlined } from '@ant-design/icons';
import { useAuth } from '../auth/AuthContext';
import { api, ApiErro } from '../api/cliente';
import LoginMoldura from './LoginMoldura';

interface Valores {
  nomeUsuario: string;
  senha: string;
  lembrar?: boolean;
}

const CHAVE_USUARIO = 'vbs.usuario-lembrado';

function lembrado(): string {
  try {
    return localStorage.getItem(CHAVE_USUARIO) ?? '';
  } catch {
    return '';
  }
}

function guardar(usuario: string | null) {
  try {
    if (usuario) localStorage.setItem(CHAVE_USUARIO, usuario);
    else localStorage.removeItem(CHAVE_USUARIO);
  } catch {
    /* sem armazenamento do navegador: só não lembra o usuário */
  }
}

function mensagemDoErro(e: unknown): string {
  if (e instanceof ApiErro) {
    if (e.status === 401) return 'Usuário ou senha inválidos.';
    if (e.status === 423) return 'Usuário bloqueado por tentativas excessivas. Procure o administrador.';
    if (e.status === 429) return 'Muitas tentativas. Aguarde um minuto e tente novamente.';
    return e.message;
  }
  return 'Erro inesperado ao entrar.';
}

/** Pede o e-mail e solicita o link de nova senha. A resposta é sempre a mesma, exista ou não o e-mail. */
function EsqueciSenhaModal({ aberto, aoFechar }: { aberto: boolean; aoFechar: () => void }) {
  const { message } = App.useApp();
  const [form] = Form.useForm<{ email: string }>();
  const [enviando, setEnviando] = useState(false);
  const [enviado, setEnviado] = useState(false);

  async function enviar(v: { email: string }) {
    setEnviando(true);
    try {
      await api.post('/auth/recuperar-senha', { email: v.email.trim() });
      setEnviado(true);
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) form.setFields([{ name: 'email', errors: [e.erros[0].mensagem] }]);
      else message.error(e instanceof ApiErro ? e.message : 'Não foi possível enviar o pedido.');
    } finally {
      setEnviando(false);
    }
  }

  function fechar() {
    setEnviado(false);
    form.resetFields();
    aoFechar();
  }

  return (
    <Modal
      open={aberto}
      title="Esqueci minha senha"
      onCancel={fechar}
      destroyOnClose
      maskClosable={false}
      okText={enviado ? 'Fechar' : 'Enviar link'}
      cancelButtonProps={{ style: { display: enviado ? 'none' : undefined } }}
      cancelText="Voltar"
      confirmLoading={enviando}
      onOk={() => (enviado ? fechar() : form.submit())}
    >
      {enviado ? (
        <Alert
          type="success"
          showIcon
          message="Pedido recebido"
          description="Se existir um usuário ativo com esse e-mail, enviamos um link para criar uma nova senha. O link vale por 30 minutos e só pode ser usado uma vez."
        />
      ) : (
        <Form form={form} layout="vertical" onFinish={enviar} requiredMark={false}>
          <p style={{ marginTop: 0 }}>Informe o e-mail cadastrado no seu usuário. Você receberá um link para criar uma nova senha.</p>
          <Form.Item
            name="email"
            label="E-mail"
            rules={[
              { required: true, whitespace: true, message: 'Campo Não Preenchido' },
              { type: 'email', message: 'E-mail inválido' }
            ]}
          >
            <Input autoFocus autoComplete="email" />
          </Form.Item>
        </Form>
      )}
    </Modal>
  );
}

export default function Login() {
  const { sessao, entrar } = useAuth();
  const navegar = useNavigate();
  const local = useLocation();
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [esqueci, setEsqueci] = useState(false);
  const [usuarioLembrado] = useState(lembrado);
  const destino = (local.state as { de?: string } | null)?.de ?? '/';

  if (sessao) return <Navigate to={destino} replace />;

  async function aoEnviar(v: Valores) {
    setEnviando(true);
    setErro(null);
    try {
      await entrar(v.nomeUsuario.trim(), v.senha);
      guardar(v.lembrar ? v.nomeUsuario.trim() : null);
      navegar(destino, { replace: true });
    } catch (e) {
      setErro(mensagemDoErro(e));
    } finally {
      setEnviando(false);
    }
  }

  return (
    <LoginMoldura titulo="Bem-vindo" subtitulo="Viver Bem Saúde - Gestão de Agendamento de Consultas">
      {erro && <Alert type="error" showIcon message={erro} style={{ marginBottom: 16 }} />}
      <Form<Valores>
        layout="vertical"
        onFinish={aoEnviar}
        requiredMark={false}
        initialValues={{ nomeUsuario: usuarioLembrado, lembrar: usuarioLembrado !== '' }}
      >
        <Form.Item name="nomeUsuario" rules={[{ required: true, message: 'Campo Não Preenchido' }]}>
          <Input prefix={<UserOutlined />} placeholder="Usuário" aria-label="Usuário" autoFocus={usuarioLembrado === ''} autoComplete="username" />
        </Form.Item>
        <Form.Item name="senha" rules={[{ required: true, message: 'Campo Não Preenchido' }]}>
          <Input.Password prefix={<LockOutlined />} placeholder="Senha" aria-label="Senha" autoFocus={usuarioLembrado !== ''} autoComplete="current-password" />
        </Form.Item>
        <div className="login-linha">
          <Form.Item name="lembrar" valuePropName="checked" noStyle>
            <Checkbox>Lembrar usuário</Checkbox>
          </Form.Item>
          <Button type="link" className="login-link" onClick={() => setEsqueci(true)}>
            Esqueci minha senha
          </Button>
        </div>
        <Button type="primary" htmlType="submit" block loading={enviando} className="login-botao">
          Entrar
        </Button>
      </Form>
      <EsqueciSenhaModal aberto={esqueci} aoFechar={() => setEsqueci(false)} />
    </LoginMoldura>
  );
}
