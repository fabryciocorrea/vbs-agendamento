import { useEffect, useMemo, useState } from 'react';
import { Alert, App, Col, Divider, Form, Input, Modal, Row, Select } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import type { Horario, ItemApoio, Profissional } from '../../api/tipos';
import { celularValido, cpfValido, formatarCelular, formatarCpf, formatarTelefone, hojeIso, somenteDigitos, UFS } from '../../util/formatos';
import CampoNumeroEndereco from '../../componentes/CampoNumeroEndereco';
import HorariosEditor from './HorariosEditor';

const OBRIGATORIO = 'Campo Não Preenchido';

interface ValoresFormulario {
  nome: string;
  cpf: string;
  rg?: string;
  dataNascimento: string;
  telefone: string;
  email: string;
  endereco: string;
  numero: string;
  complemento?: string;
  cidade: string;
  uf: string;
  escolaridade?: string;
  registroProfissional: 'CRM' | 'CRP';
  numeroConselho: string;
  ufConselho: string;
  especialidadeIds: number[];
  unidadeIds: number[];
  horarios: Horario[];
}

interface Props {
  aberto: boolean;
  /** Nulo para cadastrar um novo profissional. */
  profissional: Profissional | null;
  aoFechar: () => void;
  aoSalvar: () => void;
}

const ufOpcoes = UFS.map((u) => ({ value: u, label: u }));

/** Junta as opções ativas com as já vinculadas (que podem estar inativas e não vêm na lista de apoio). */
function opcoes(itens: ItemApoio[], vinculadas: { id: number; nome: string }[]) {
  const lista = itens.map((i) => ({ value: i.id, label: i.nome }));
  for (const v of vinculadas) if (!lista.some((o) => o.value === v.id)) lista.push({ value: v.id, label: v.nome });
  return lista;
}

export default function FormularioProfissional({ aberto, profissional, aoFechar, aoSalvar }: Props) {
  const { message } = App.useApp();
  const [form] = Form.useForm<ValoresFormulario>();
  const [salvando, setSalvando] = useState(false);
  const [especialidades, setEspecialidades] = useState<ItemApoio[]>([]);
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [errosHorarios, setErrosHorarios] = useState<string[]>([]);
  const unidadeIds = Form.useWatch('unidadeIds', form);

  useEffect(() => {
    if (!aberto) return;
    Promise.all([api.get<ItemApoio[]>('/apoio/especialidades'), api.get<ItemApoio[]>('/apoio/unidades')])
      .then(([e, u]) => {
        setEspecialidades(e);
        setUnidades(u);
      })
      .catch(() => message.error('Não foi possível carregar as listas de especialidades e unidades.'));
  }, [aberto, message]);

  const opcoesEspecialidades = useMemo(() => opcoes(especialidades, profissional?.especialidades ?? []), [especialidades, profissional]);
  const opcoesUnidades = useMemo(() => opcoes(unidades, profissional?.unidades ?? []), [unidades, profissional]);
  // O expediente só aceita as unidades escolhidas para o profissional.
  const unidadesDoExpediente = useMemo(
    () => opcoesUnidades.filter((o) => (unidadeIds ?? []).includes(o.value)),
    [opcoesUnidades, unidadeIds]
  );

  async function enviar() {
    setErrosHorarios([]);
    let v: ValoresFormulario;
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
      telefone: somenteDigitos(v.telefone),
      email: v.email.trim(),
      endereco: v.endereco.trim(),
      numero: v.numero.trim(),
      complemento: opcional(v.complemento),
      cidade: v.cidade.trim(),
      uf: v.uf,
      escolaridade: opcional(v.escolaridade),
      registroProfissional: v.registroProfissional,
      numeroConselho: v.numeroConselho.trim(),
      ufConselho: v.ufConselho,
      especialidadeIds: v.especialidadeIds,
      unidadeIds: v.unidadeIds,
      // O expediente só vai no cadastro; depois ele é alterado pelo botão próprio da lista.
      ...(profissional ? {} : { horarios: v.horarios })
    };

    setSalvando(true);
    try {
      if (profissional) await api.put<Profissional>(`/profissionais/${profissional.id}`, corpo);
      else await api.post<Profissional>('/profissionais', corpo);
      message.success(profissional ? 'Profissional atualizado.' : 'Profissional cadastrado.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        const deHorarios = e.erros.filter((x) => x.campo.startsWith('horarios'));
        const outros = e.erros.filter((x) => !x.campo.startsWith('horarios'));
        if (outros.length > 0)
          form.setFields(outros.map((x) => ({ name: x.campo as keyof ValoresFormulario, errors: [x.mensagem] })));
        if (deHorarios.length > 0)
          setErrosHorarios(
            deHorarios.map((x) => {
              const m = /^horarios\[(\d+)\]/.exec(x.campo);
              return m ? `Linha ${Number(m[1]) + 1}: ${x.mensagem}` : x.mensagem;
            })
          );
        if (outros.length === 0 && deHorarios.length === 0) message.error(e.message);
      } else if (e instanceof ApiErro && e.codigo === 'CPF_DUPLICADO') {
        form.setFields([{ name: 'cpf', errors: [e.message] }]);
      } else if (e instanceof ApiErro && e.codigo === 'CONSELHO_DUPLICADO') {
        form.setFields([{ name: 'numeroConselho', errors: [e.message] }]);
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
      title={profissional ? 'Editar profissional' : 'Novo profissional'}
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
      <Form<ValoresFormulario>
        form={form}
        layout="vertical"
        requiredMark
        preserve={false}
        initialValues={
          profissional
            ? {
                nome: profissional.nome,
                cpf: formatarCpf(profissional.cpf),
                rg: profissional.rg ?? '',
                dataNascimento: profissional.dataNascimento.slice(0, 10),
                telefone: formatarTelefone(profissional.telefone),
                email: profissional.email,
                endereco: profissional.endereco,
                numero: profissional.numero,
                complemento: profissional.complemento ?? undefined,
                cidade: profissional.cidade,
                uf: profissional.uf,
                escolaridade: profissional.escolaridade ?? '',
                registroProfissional: profissional.registroProfissional,
                numeroConselho: profissional.numeroConselho,
                ufConselho: profissional.ufConselho,
                especialidadeIds: profissional.especialidades.map((x) => x.id),
                unidadeIds: profissional.unidades.map((x) => x.id)
              }
            : { horarios: [] }
        }
      >
        <Divider orientation="left" plain>
          Dados pessoais
        </Divider>
        <Row gutter={16}>
          <Col xs={24} md={16}>
            <Form.Item name="nome" label="Nome" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={150} />
            </Form.Item>
          </Col>
          <Col xs={24} md={8}>
            <Form.Item name="dataNascimento" label="Data de nascimento" rules={[{ required: true, message: OBRIGATORIO }]}>
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
                { required: true, message: OBRIGATORIO },
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
          <Col xs={24} md={8}>
            <Form.Item name="cidade" label="Cidade" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={80} />
            </Form.Item>
          </Col>
          <Col xs={24} md={4}>
            <Form.Item name="uf" label="UF" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Select showSearch options={ufOpcoes} />
            </Form.Item>
          </Col>
        </Row>

        <Divider orientation="left" plain>
          Conselho profissional
        </Divider>
        <Row gutter={16}>
          <Col xs={24} md={8}>
            <Form.Item name="registroProfissional" label="Registro" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Select
                options={[
                  { value: 'CRM', label: 'CRM (médico)' },
                  { value: 'CRP', label: 'CRP (psicólogo)' }
                ]}
              />
            </Form.Item>
          </Col>
          <Col xs={24} md={10}>
            <Form.Item name="numeroConselho" label="Número" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={20} />
            </Form.Item>
          </Col>
          <Col xs={24} md={6}>
            <Form.Item name="ufConselho" label="UF do conselho" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Select showSearch options={ufOpcoes} />
            </Form.Item>
          </Col>
        </Row>

        <Divider orientation="left" plain>
          Atendimento
        </Divider>
        <Row gutter={16}>
          <Col xs={24} md={12}>
            <Form.Item
              name="especialidadeIds"
              label="Especialidades"
              rules={[{ required: true, type: 'array', min: 1, message: 'Informe ao menos uma especialidade.' }]}
            >
              <Select mode="multiple" showSearch optionFilterProp="label" options={opcoesEspecialidades} placeholder="Selecione" />
            </Form.Item>
          </Col>
          <Col xs={24} md={12}>
            <Form.Item
              name="unidadeIds"
              label="Unidades em que atende"
              rules={[{ required: true, type: 'array', min: 1, message: 'Informe ao menos uma unidade.' }]}
            >
              <Select mode="multiple" showSearch optionFilterProp="label" options={opcoesUnidades} placeholder="Selecione" />
            </Form.Item>
          </Col>
        </Row>

        {!profissional && (
          <>
            <Divider orientation="left" plain>
              Expediente
            </Divider>
            {errosHorarios.length > 0 && (
              <Alert
                type="error"
                showIcon
                style={{ marginBottom: 12 }}
                message="Confira o expediente"
                description={
                  <ul style={{ margin: 0, paddingLeft: 18 }}>
                    {errosHorarios.map((t, i) => (
                      <li key={i}>{t}</li>
                    ))}
                  </ul>
                }
              />
            )}
            <Form.Item
              name="horarios"
              rules={[
                {
                  validator: (_, v: Horario[] | undefined) =>
                    v && v.some((h) => h.tipo === 'ATENDIMENTO')
                      ? Promise.resolve()
                      : Promise.reject(new Error('Informe ao menos um horário de atendimento.'))
                }
              ]}
            >
              <HorariosEditor unidades={unidadesDoExpediente} />
            </Form.Item>
          </>
        )}
      </Form>
    </Modal>
  );
}
