import { useState } from 'react';
import { App, Form, Input, Modal, Typography } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import type { Usuario } from '../../api/tipos';
import { TEXTO_REGRA_SENHA } from './UsuarioFormulario';

const OBRIGATORIO = 'Campo Não Preenchido';
const REGRA_SENHA = /^(?=.*[A-Za-z])(?=.*[0-9])(?=.*[^A-Za-z0-9]).{10,128}$/;

interface Valores {
  novaSenha: string;
  confirmarSenha: string;
}

interface Props {
  usuario: Usuario | null;
  aoFechar: () => void;
}

/** O administrador define uma nova senha. As sessões abertas do usuário são encerradas pelo servidor. */
export default function SenhaModal({ usuario, aoFechar }: Props) {
  const { message } = App.useApp();
  const [form] = Form.useForm<Valores>();
  const [salvando, setSalvando] = useState(false);

  async function enviar() {
    if (!usuario) return;
    let v: Valores;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    setSalvando(true);
    try {
      await api.put(`/usuarios/${usuario.id}/senha`, { novaSenha: v.novaSenha });
      message.success('Senha redefinida. As sessões abertas do usuário foram encerradas.');
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
      open={usuario !== null}
      title="Definir nova senha"
      okText="Salvar senha"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
    >
      <Typography.Paragraph>
        Usuário: <strong>{usuario?.nomeCompleto}</strong> ({usuario?.nomeUsuario})
      </Typography.Paragraph>
      <Form<Valores> form={form} layout="vertical" requiredMark preserve={false}>
        <Form.Item
          name="novaSenha"
          label="Nova senha"
          extra={TEXTO_REGRA_SENHA}
          rules={[
            { required: true, message: OBRIGATORIO },
            { pattern: REGRA_SENHA, message: TEXTO_REGRA_SENHA }
          ]}
        >
          <Input.Password maxLength={128} autoFocus autoComplete="new-password" />
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
