import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Alert, App, Button, Form, Input } from 'antd';
import { LockOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../api/cliente';
import LoginMoldura from './LoginMoldura';

interface Valores {
  novaSenha: string;
  confirmacao: string;
}

const REGRA_SENHA = /^(?=.*[A-Za-z])(?=.*\d)(?=.*[^A-Za-z\d]).{10,128}$/;

/** Tela aberta pelo link do e-mail (/redefinir-senha?token=...): define a nova senha. */
export default function RedefinirSenha() {
  const { message } = App.useApp();
  const navegar = useNavigate();
  const [params] = useSearchParams();
  const token = params.get('token') ?? '';
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState('');

  async function enviar(v: Valores) {
    setEnviando(true);
    setErro('');
    try {
      await api.post('/auth/redefinir-senha', { token, novaSenha: v.novaSenha });
      message.success('Senha alterada. Entre com a nova senha.');
      navegar('/login', { replace: true });
    } catch (e) {
      if (e instanceof ApiErro && e.status === 410) setErro('Este link é inválido, expirou ou já foi usado. Peça um novo em "Esqueci minha senha".');
      else setErro(e instanceof ApiErro ? e.erros[0]?.mensagem ?? e.message : 'Não foi possível alterar a senha.');
    } finally {
      setEnviando(false);
    }
  }

  return (
    <LoginMoldura titulo="Nova senha" subtitulo="Crie uma senha nova para o seu usuário">
      {!token && <Alert type="error" showIcon message="Link incompleto. Abra o link do e-mail novamente." style={{ marginBottom: 16 }} />}
      {erro && <Alert type="error" showIcon message={erro} style={{ marginBottom: 16 }} />}
      <Form<Valores> layout="vertical" onFinish={enviar} requiredMark={false}>
        <Form.Item
          name="novaSenha"
          extra={<span style={{ color: 'rgba(255,255,255,0.7)' }}>De 10 a 128 caracteres, com letras, números e pelo menos um caractere especial.</span>}
          rules={[
            { required: true, message: 'Campo Não Preenchido' },
            { pattern: REGRA_SENHA, message: 'A senha não atende às regras' }
          ]}
        >
          <Input.Password prefix={<LockOutlined />} placeholder="Nova senha" aria-label="Nova senha" autoComplete="new-password" autoFocus />
        </Form.Item>
        <Form.Item
          name="confirmacao"
          dependencies={['novaSenha']}
          rules={[
            { required: true, message: 'Campo Não Preenchido' },
            ({ getFieldValue }) => ({
              validator: (_, valor) => (!valor || getFieldValue('novaSenha') === valor ? Promise.resolve() : Promise.reject(new Error('As senhas não são iguais')))
            })
          ]}
        >
          <Input.Password prefix={<LockOutlined />} placeholder="Repita a nova senha" aria-label="Repita a nova senha" autoComplete="new-password" />
        </Form.Item>
        <Button type="primary" htmlType="submit" block loading={enviando} disabled={!token} className="login-botao">
          Salvar senha
        </Button>
        <div className="login-linha" style={{ justifyContent: 'center', marginTop: 16 }}>
          <Button type="link" className="login-link" onClick={() => navegar('/login')}>
            Voltar ao login
          </Button>
        </div>
      </Form>
    </LoginMoldura>
  );
}
