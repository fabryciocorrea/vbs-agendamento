import { useEffect, useMemo, useState } from 'react';
import { Alert, App, Col, Form, Input, Modal, Row, Select } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio, PerfilResumo, Usuario } from '../../api/tipos';

const OBRIGATORIO = 'Campo Não Preenchido';
const REGRA_SENHA = /^(?=.*[A-Za-z])(?=.*[0-9])(?=.*[^A-Za-z0-9]).{10,128}$/;
export const TEXTO_REGRA_SENHA = 'Mínimo de 10 caracteres, com letras, números e pelo menos um caractere especial.';

interface ValoresFormulario {
  nomeCompleto: string;
  nomeUsuario: string;
  email: string;
  cargo: string;
  perfilId: number;
  profissionalId?: number | null;
  senha?: string;
  confirmarSenha?: string;
}

interface Props {
  aberto: boolean;
  usuario: Usuario | null;
  aoFechar: () => void;
  aoSalvar: () => void;
}

export default function UsuarioFormulario({ aberto, usuario, aoFechar, aoSalvar }: Props) {
  const { message } = App.useApp();
  const [form] = Form.useForm<ValoresFormulario>();
  const [salvando, setSalvando] = useState(false);
  const [perfis, setPerfis] = useState<PerfilResumo[]>([]);
  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);

  useEffect(() => {
    if (!aberto) return;
    api
      .get<PerfilResumo[]>('/perfis')
      .then(setPerfis)
      .catch(() => message.error('Não foi possível carregar a lista de perfis.'));
    // Sem a liberação das listas de apoio, o vínculo com profissional fica indisponível.
    api
      .get<ItemApoio[]>('/apoio/profissionais')
      .then(setProfissionais)
      .catch(() => setProfissionais([]));
  }, [aberto, message]);

  const opcoesPerfis = useMemo(() => perfis.map((p) => ({ value: p.id, label: p.nome })), [perfis]);

  // O profissional já vinculado pode estar inativo e não vir na lista de apoio.
  const opcoesProfissionais = useMemo(() => {
    const lista = profissionais.map((p) => ({ value: p.id, label: p.complemento ? `${p.nome} (${p.complemento})` : p.nome }));
    if (usuario?.profissionalId && !lista.some((o) => o.value === usuario.profissionalId)) {
      lista.push({ value: usuario.profissionalId, label: usuario.profissionalNome ?? `Profissional ${usuario.profissionalId}` });
    }
    return lista;
  }, [profissionais, usuario]);

  async function enviar() {
    let v: ValoresFormulario;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    const base = {
      nomeCompleto: v.nomeCompleto.trim(),
      nomeUsuario: v.nomeUsuario.trim(),
      email: v.email.trim(),
      cargo: v.cargo.trim(),
      perfilId: v.perfilId,
      profissionalId: v.profissionalId ?? null
    };
    setSalvando(true);
    try {
      if (usuario) await api.put<Usuario>(`/usuarios/${usuario.id}`, base);
      else await api.post<Usuario>('/usuarios', { ...base, senha: v.senha, ativo: true });
      message.success(usuario ? 'Usuário atualizado.' : 'Usuário cadastrado.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(e.erros.map((x) => ({ name: x.campo as keyof ValoresFormulario, errors: [x.mensagem] })));
      } else if (e instanceof ApiErro && e.codigo === 'NOME_USUARIO_DUPLICADO') {
        form.setFields([{ name: 'nomeUsuario', errors: [e.message] }]);
      } else if (e instanceof ApiErro && e.codigo === 'EMAIL_DUPLICADO') {
        form.setFields([{ name: 'email', errors: [e.message] }]);
      } else if (e instanceof ApiErro && e.codigo === 'PROFISSIONAL_JA_TEM_USUARIO') {
        form.setFields([{ name: 'profissionalId', errors: [e.message] }]);
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
      title={usuario ? 'Editar usuário' : 'Novo usuário'}
      width={720}
      okText="Salvar"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
    >
      <Form<ValoresFormulario>
        form={form}
        layout="vertical"
        requiredMark
        preserve={false}
        initialValues={
          usuario
            ? {
                nomeCompleto: usuario.nomeCompleto,
                nomeUsuario: usuario.nomeUsuario,
                email: usuario.email,
                cargo: usuario.cargo,
                perfilId: usuario.perfil.id,
                profissionalId: usuario.profissionalId ?? undefined
              }
            : undefined
        }
      >
        <Row gutter={16}>
          <Col xs={24} md={12}>
            <Form.Item name="nomeCompleto" label="Nome completo" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={150} autoFocus />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item
              name="nomeUsuario"
              label="Nome de usuário"
              extra="Primeiro e último nome separados por ponto, em minúsculas e sem acento (exemplo: maria.silva)."
              // Mantém o padrão do sistema enquanto a pessoa digita: minúsculas e sem espaços.
              normalize={(valor: string) => (valor ?? '').toLowerCase().replace(/\s/g, '')}
              rules={[
                { required: true, whitespace: true, message: OBRIGATORIO },
                { pattern: /^[a-z0-9]+(\.[a-z0-9]+)+$/, message: 'Use o formato nome.sobrenome, em minúsculas e sem acento' }
              ]}
            >
              <Input maxLength={60} autoComplete="off" />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item
              name="email"
              label="E-mail"
              rules={[
                { required: true, whitespace: true, message: OBRIGATORIO },
                { type: 'email', message: 'E-mail inválido' }
              ]}
            >
              <Input maxLength={150} autoComplete="off" />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item name="cargo" label="Cargo" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={80} />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item name="perfilId" label="Perfil de acesso" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Select showSearch optionFilterProp="label" options={opcoesPerfis} placeholder="Selecione" />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item
              name="profissionalId"
              label="Profissional vinculado"
              extra="Preencha para quem atende pacientes. Um profissional só pode ter um usuário."
            >
              <Select allowClear showSearch optionFilterProp="label" options={opcoesProfissionais} placeholder="Nenhum" />
            </Form.Item>
          </Col>

          {!usuario && (
            <>
              <Col xs={24} md={12}>
                <Form.Item
                  name="senha"
                  label="Senha"
                  extra={TEXTO_REGRA_SENHA}
                  rules={[
                    { required: true, message: OBRIGATORIO },
                    { pattern: REGRA_SENHA, message: TEXTO_REGRA_SENHA }
                  ]}
                >
                  <Input.Password maxLength={128} autoComplete="new-password" />
                </Form.Item>
              </Col>
              <Col xs={24} md={12}>
                <Form.Item
                  name="confirmarSenha"
                  label="Confirmar senha"
                  dependencies={['senha']}
                  rules={[
                    { required: true, message: OBRIGATORIO },
                    ({ getFieldValue }) => ({
                      validator: (_, valor) =>
                        !valor || getFieldValue('senha') === valor ? Promise.resolve() : Promise.reject(new Error('As senhas não conferem'))
                    })
                  ]}
                >
                  <Input.Password maxLength={128} autoComplete="new-password" />
                </Form.Item>
              </Col>
            </>
          )}
        </Row>
        {usuario && (
          <Alert
            type="info"
            showIcon
            message="A senha não é alterada aqui. Use o botão de chave na lista para definir uma nova senha."
          />
        )}
      </Form>
    </Modal>
  );
}
