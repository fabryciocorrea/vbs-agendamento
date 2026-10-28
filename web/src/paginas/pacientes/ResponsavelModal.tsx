import { useEffect, useState } from 'react';
import { Alert, App, Checkbox, Col, Descriptions, Divider, Form, Input, Modal, Row, Select, Tag } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import type { Paciente, ResponsavelLegal } from '../../api/tipos';
import CampoNumeroEndereco from '../../componentes/CampoNumeroEndereco';
import { celularValido, cpfValido, formatarCelular, formatarCpf, formatarDataIso, formatarTelefone, hojeIso, somenteDigitos, UFS } from '../../util/formatos';

const OBRIGATORIO = 'Campo Não Preenchido';
const ufOpcoes = UFS.map((u) => ({ value: u, label: u }));

interface Valores {
  nome: string;
  cpf: string;
  rg?: string;
  dataNascimento: string;
  escolaridade?: string;
  telefone: string;
  email: string;
  endereco: string;
  numero: string;
  complemento?: string;
  cidade: string;
  uf: string;
  consTratamento?: boolean;
  consContato?: boolean;
  consOrigem?: 'PRESENCIAL' | 'TELEFONE';
}

interface Props {
  aberto: boolean;
  /** Nulo para cadastrar um novo responsável legal. */
  responsavel: ResponsavelLegal | null;
  /** CPF já digitado na busca, para começar o cadastro preenchido. */
  cpfInicial?: string;
  /** Dados de um paciente adulto com o mesmo CPF, usados para adiantar o preenchimento. */
  sugestao?: Paciente | null;
  aoFechar: () => void;
  aoSalvar: (responsavel: ResponsavelLegal) => void;
}

// Nome do campo do servidor -> nome do campo deste formulário.
function campoDoFormulario(campo: string): keyof Valores | null {
  switch (campo) {
    case 'nome': case 'cpf': case 'rg': case 'dataNascimento': case 'escolaridade': case 'telefone': case 'email':
    case 'endereco': case 'numero': case 'complemento': case 'cidade': case 'uf':
      return campo;
    default:
      return /^consentimentos\[\d+\]\.origem$/.test(campo) ? 'consOrigem' : null;
  }
}

/** Cadastro e alteração do responsável legal (E5: a alteração vale para todos os pacientes vinculados). */
export default function ResponsavelModal({ aberto, responsavel, cpfInicial, sugestao, aoFechar, aoSalvar }: Props) {
  const { message } = App.useApp();
  const [form] = Form.useForm<Valores>();
  const [salvando, setSalvando] = useState(false);
  const [avisos, setAvisos] = useState<string[]>([]);

  useEffect(() => {
    if (aberto) setAvisos([]);
  }, [aberto]);

  // Se o responsável já tem o consentimento de tratamento de dados, só mostra o estado; senão, pede o registro.
  const consentimentosAtuais = responsavel?.consentimentosAtuais ?? [];
  const jaConsentiu = consentimentosAtuais.some((c) => c.finalidade === 'TRATAMENTO_DADOS' && c.concedido);

  async function enviar() {
    setAvisos([]);
    let v: Valores;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    const opcional = (t?: string) => (t && t.trim() !== '' ? t.trim() : null);
    const corpo = {
      nome: v.nome.trim(),
      cpf: somenteDigitos(v.cpf),
      rg: opcional(v.rg),
      dataNascimento: v.dataNascimento,
      escolaridade: opcional(v.escolaridade),
      telefone: somenteDigitos(v.telefone),
      email: v.email.trim(),
      endereco: v.endereco.trim(),
      numero: v.numero.trim(),
      complemento: opcional(v.complemento),
      cidade: v.cidade.trim(),
      uf: v.uf,
      consentimentos: jaConsentiu
        ? null
        : [
            { finalidade: 'TRATAMENTO_DADOS', concedido: v.consTratamento === true, origem: v.consOrigem },
            { finalidade: 'CONTATO_SMS_WHATSAPP', concedido: v.consContato === true, origem: v.consOrigem }
          ]
    };
    setSalvando(true);
    try {
      const salvo = responsavel
        ? await api.put<ResponsavelLegal>(`/responsaveis-legais/${responsavel.id}`, corpo)
        : await api.post<ResponsavelLegal>('/responsaveis-legais', corpo);
      message.success(responsavel ? 'Responsável legal atualizado.' : 'Responsável legal cadastrado.');
      aoSalvar(salvo);
    } catch (e) {
      if (e instanceof ApiErro && e.codigo === 'CPF_DUPLICADO') {
        form.setFields([{ name: 'cpf', errors: [e.message] }]);
      } else if (e instanceof ApiErro && e.erros.length > 0) {
        const paraCampos: { name: keyof Valores; errors: string[] }[] = [];
        const outros: string[] = [];
        for (const x of e.erros) {
          const nome = campoDoFormulario(x.campo);
          if (nome) paraCampos.push({ name: nome, errors: [x.mensagem] });
          else outros.push(x.mensagem);
        }
        if (paraCampos.length > 0) form.setFields(paraCampos);
        if (outros.length > 0) setAvisos(outros);
      } else {
        message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      }
    } finally {
      setSalvando(false);
    }
  }

  const compartilhado = responsavel && (responsavel.quantidadePacientes ?? 0) > 1;

  function valoresIniciais(): Partial<Valores> {
    if (responsavel) {
      return {
        nome: responsavel.nome,
        cpf: formatarCpf(responsavel.cpf),
        rg: responsavel.rg ?? '',
        dataNascimento: responsavel.dataNascimento.slice(0, 10),
        escolaridade: responsavel.escolaridade ?? '',
        telefone: formatarTelefone(responsavel.telefone),
        email: responsavel.email,
        endereco: responsavel.endereco ?? '',
        numero: responsavel.numero ?? '',
        complemento: responsavel.complemento ?? undefined,
        cidade: responsavel.cidade ?? '',
        uf: responsavel.uf ?? undefined
      };
    }
    if (sugestao) {
      return {
        nome: sugestao.nome,
        cpf: formatarCpf(cpfInicial ?? sugestao.cpf),
        rg: sugestao.rg ?? '',
        dataNascimento: sugestao.dataNascimento.slice(0, 10),
        escolaridade: sugestao.escolaridade ?? '',
        telefone: formatarTelefone(sugestao.telefone),
        email: sugestao.email ?? '',
        endereco: sugestao.endereco,
        numero: sugestao.numero,
        complemento: sugestao.complemento ?? undefined,
        cidade: sugestao.cidade,
        uf: sugestao.uf
      };
    }
    return { cpf: formatarCpf(cpfInicial) };
  }

  return (
    <Modal
      open={aberto}
      title={responsavel ? 'Editar responsável legal' : 'Novo responsável legal'}
      okText="Salvar"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
      width={900}
      styles={{ body: { maxHeight: '70vh', overflowY: 'auto', paddingRight: 8 } }}
    >
      {compartilhado && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          message={`Este responsável está vinculado a ${responsavel?.quantidadePacientes} pacientes. A alteração vale para todos eles.`}
        />
      )}
      {!responsavel && sugestao && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          message="Este CPF é de um paciente já cadastrado."
          description="Preenchemos os dados com o cadastro dele. Confira e salve para cadastrá-lo também como responsável legal."
        />
      )}
      <Form<Valores> form={form} layout="vertical" requiredMark preserve={false} initialValues={valoresIniciais()}>
        {avisos.length > 0 && (
          <Alert
            type="error"
            showIcon
            style={{ marginBottom: 12 }}
            message="Confira o cadastro"
            description={
              <ul style={{ margin: 0, paddingLeft: 18 }}>
                {avisos.map((t, i) => (
                  <li key={i}>{t}</li>
                ))}
              </ul>
            }
          />
        )}

        <Divider orientation="left" plain>
          Dados pessoais
        </Divider>
        <Row gutter={16}>
          <Col xs={24} md={16}>
            <Form.Item name="nome" label="Nome completo" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={150} autoFocus />
            </Form.Item>
          </Col>
          <Col xs={24} md={8}>
            <Form.Item
              name="dataNascimento"
              label="Data de nascimento"
              extra="Precisa ter 18 anos ou mais."
              rules={[{ required: true, message: OBRIGATORIO }]}
            >
              <Input type="date" max={hojeIso()} />
            </Form.Item>
          </Col>
        </Row>
        <Row gutter={16}>
          <Col xs={24} md={8}>
            <Form.Item
              name="cpf"
              label="CPF"
              normalize={(v: string) => formatarCpf(v)}
              rules={[
                { required: true, message: OBRIGATORIO },
                { validator: (_, v: string) => (!v || cpfValido(v) ? Promise.resolve() : Promise.reject(new Error('CPF inválido.'))) }
              ]}
            >
              <Input placeholder="000.000.000-00" inputMode="numeric" />
            </Form.Item>
          </Col>
          <Col xs={24} md={8}>
            <Form.Item name="rg" label="RG">
              <Input maxLength={20} />
            </Form.Item>
          </Col>
          <Col xs={24} md={8}>
            <Form.Item name="escolaridade" label="Escolaridade">
              <Input maxLength={40} />
            </Form.Item>
          </Col>
        </Row>
        <Row gutter={16}>
          <Col xs={24} md={10}>
            <Form.Item
              name="telefone"
              label="Celular (com DDD)"
              normalize={(v: string) => formatarCelular(v)}
              rules={[
                { required: true, message: OBRIGATORIO },
                {
                  validator: (_, v: string) =>
                    !v || celularValido(v) ? Promise.resolve() : Promise.reject(new Error('Informe o celular com DDD: (00) 00000-0000.'))
                }
              ]}
            >
              <Input placeholder="(00) 00000-0000" inputMode="tel" maxLength={15} />
            </Form.Item>
          </Col>
          <Col xs={24} md={14}>
            <Form.Item
              name="email"
              label="E-mail"
              rules={[
                { required: true, whitespace: true, message: OBRIGATORIO },
                { type: 'email', message: 'E-mail inválido.' }
              ]}
            >
              <Input maxLength={150} />
            </Form.Item>
          </Col>
        </Row>
        <Row gutter={16}>
          <Col xs={24} md={12}>
            <Form.Item name="endereco" label="Endereço" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={250} />
            </Form.Item>
          </Col>
          <Col xs={24} md={6}>
            <Form.Item name="numero" label="Número" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <CampoNumeroEndereco />
            </Form.Item>
          </Col>
          <Col xs={24} md={6}>
            <Form.Item name="complemento" label="Complemento">
              <Input maxLength={60} />
            </Form.Item>
          </Col>
        </Row>
        <Row gutter={16}>
          <Col xs={24} md={16}>
            <Form.Item name="cidade" label="Cidade" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={80} />
            </Form.Item>
          </Col>
          <Col xs={24} md={8}>
            <Form.Item name="uf" label="UF" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Select showSearch options={ufOpcoes} />
            </Form.Item>
          </Col>
        </Row>

        <Divider orientation="left" plain>
          Consentimentos (LGPD)
        </Divider>
        {jaConsentiu ? (
          <Descriptions size="small" column={1}>
            {consentimentosAtuais.map((c) => (
              <Descriptions.Item key={c.finalidade} label={c.finalidade === 'TRATAMENTO_DADOS' ? 'Tratamento de dados' : 'Contato por SMS e WhatsApp'}>
                {c.concedido ? <Tag color="green">Concedido</Tag> : <Tag>Não concedido</Tag>}
                <span style={{ marginLeft: 8, color: '#8c8c8c' }}>registrado em {formatarDataIso(c.dataRegistro)}</span>
              </Descriptions.Item>
            ))}
          </Descriptions>
        ) : (
          <>
            <Form.Item
              name="consTratamento"
              valuePropName="checked"
              rules={[
                {
                  validator: (_, v: boolean) =>
                    v ? Promise.resolve() : Promise.reject(new Error('O consentimento de tratamento de dados é obrigatório.'))
                }
              ]}
            >
              <Checkbox>O responsável legal autoriza o tratamento dos seus dados pessoais (obrigatório).</Checkbox>
            </Form.Item>
            <Form.Item name="consContato" valuePropName="checked">
              <Checkbox>O responsável legal autoriza o contato por SMS e WhatsApp.</Checkbox>
            </Form.Item>
            <Row gutter={16}>
              <Col xs={24} md={10}>
                <Form.Item name="consOrigem" label="Como o consentimento foi dado" rules={[{ required: true, message: OBRIGATORIO }]}>
                  <Select
                    placeholder="Selecione"
                    options={[
                      { value: 'PRESENCIAL', label: 'Presencialmente' },
                      { value: 'TELEFONE', label: 'Por telefone' }
                    ]}
                  />
                </Form.Item>
              </Col>
            </Row>
          </>
        )}
      </Form>
    </Modal>
  );
}
