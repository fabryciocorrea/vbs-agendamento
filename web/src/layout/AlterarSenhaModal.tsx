import { useState } from 'react';
import { App, Form, Input, Modal } from 'antd';
import { api, ApiErro } from '../api/cliente';
import { TEXTO_REGRA_SENHA } from '../paginas/usuarios/UsuarioFormulario';

const OBRIGATORIO = 'Campo Não Preenchido';
const REGRA_SENHA = /^(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[^A-Za-z0-9]).{10,128}$/;

interface Valores {
  senhaAtual: string;
  novaSenha: string;
  confirmarSenha: string;
}

/** P70: o próprio usuário troca a senha informando a atual. As outras sessões dele são encerradas pelo servidor. */
export default function AlterarSenhaModal({ aberto, aoFechar }: { aberto: boolean; aoFechar: () => void }) {
  const { message } = App.useApp();
  const [form] = Form.useForm<Valores>();
  const [salvando, setSalvando] = useState(false);

  async function enviar() {
    let v: Valores;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    setSalvando(true);
    try {
      await api.post('/auth/alterar-senha', { senhaAtual: v.senhaAtual, novaSenha: v.novaSenha });
      message.success('Senha alterada. Seus outros acessos abertos foram encerrados.');
      aoFechar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(e.erros.map((x) => ({ name: x.campo as keyof Valores, errors: [x.mensagem] })));
      } else {
        message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      }
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Modal
      open={aberto}
      title="Alterar minha senha"
      okText="Salvar senha"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
    >
      <Form<Valores> form={form} layout="vertical" requiredMark preserve={false}>
        <Form.Item name="senhaAtual" label="Senha atual" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Input.Password maxLength={128} autoFocus autoComplete="current-password" />
        </Form.Item>
        <Form.Item
          name="novaSenha"
          label="Nova senha"
          extra={TEXTO_REGRA_SENHA}
          dependencies={['senhaAtual']}
          rules={[
            { required: true, message: OBRIGATORIO },
            { pattern: REGRA_SENHA, message: TEXTO_REGRA_SENHA },
            ({ getFieldValue }) => ({
              validator: (_, valor) =>
                !valor || getFieldValue('senhaAtual') !== valor ? Promise.resolve() : Promise.reject(new Error('A nova senha deve ser diferente da atual.'))
            })
          ]}
        >
          <Input.Password maxLength={128} autoComplete="new-password" />
        </Form.Item>
        <Form.Item
          name="confirmarSenha"
          label="Confirmar nova senha"
          dependencies={['novaSenha']}
          rules={[
            { required: true, message: OBRIGATORIO },
            ({ getFieldValue }) => ({
              validator: (_, valor) =>
                !valor || getFieldValue('novaSenha') === valor ? Promise.resolve() : Promise.reject(new Error('As senhas não conferem'))
            })
          ]}
        >
          <Input.Password maxLength={128} autoComplete="new-password" />
        </Form.Item>
      </Form>
    </Modal>
  );
}
