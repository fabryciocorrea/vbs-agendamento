import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, App, Checkbox, Form, Input, InputNumber, Modal, Radio, Select, Space, Spin } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import { useAuth } from '../../auth/AuthContext';
import { pedirJustificativa } from './liberacao';
import type { ItemApoio, PacienteResumo, Pagina } from '../../api/tipos';
import { adicionarDias, diaBrasilia, diaDaSemana, hojeBrasilia, horaBrasilia } from './datas';
import type { AgendaSaida, Consulta, ConsultaEntrada, Slot } from './tipos';

const OBRIGATORIO = 'Campo Não Preenchido';

interface Valores {
  pacienteId: string;
  profissionalId: number;
  unidadeId: number;
  especialidadeId: number;
  modalidade: 'PRESENCIAL' | 'ONLINE';
  salaId?: number;
  tipoAtendimento: 'PARTICULAR' | 'CONVENIO';
  convenioId?: number;
  formaPagamentoId?: number;
  valor: number | null;
  dia: string;
  slotInicio?: string;
  encaixe: boolean;
  encaixeInicio?: string;
  pagamentoConfirmado: boolean;
}

interface Props {
  aberto: boolean;
  profissionalId?: number;
  unidadeId?: number;
  especialidadeId?: number;
  /** Paciente já escolhido (por exemplo, vindo da lista de espera). */
  pacienteInicial?: { id: string; nome: string };
  dia?: string;
  aoFechar: () => void;
  aoSalvar: () => void;
}

const LIBERAVEIS = ['CONVENIO_NAO_CREDENCIADO', 'HORARIO_NO_PASSADO', 'HORARIO_BLOQUEADO', 'FORA_DO_EXPEDIENTE', 'CONVENIO_DIVERGENTE', 'CARTEIRINHA_VENCIDA'];

const MENSAGENS_ERRO: Record<string, string> = {
  HORARIO_INDISPONIVEL: 'Esse horário acabou de ser ocupado. Escolha outro.',
  AGENDA_EM_USO: 'A agenda do profissional está em uso por outra operação. Tente novamente.'
};

export default function NovaConsultaModal({ aberto, profissionalId, unidadeId, especialidadeId, pacienteInicial, dia, aoFechar, aoSalvar }: Props) {
  const { message, modal } = App.useApp();
  const { sessao } = useAuth();
  const administrador = !!sessao?.administrador;
  const [form] = Form.useForm<Valores>();
  const [salvando, setSalvando] = useState(false);
  const [erroGeral, setErroGeral] = useState('');

  const [pacientes, setPacientes] = useState<PacienteResumo[]>([]);
  const [buscandoPaciente, setBuscandoPaciente] = useState(false);
  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [especialidades, setEspecialidades] = useState<ItemApoio[]>([]);
  const [salas, setSalas] = useState<ItemApoio[]>([]);
  const [formas, setFormas] = useState<ItemApoio[]>([]);
  const [convenios, setConvenios] = useState<ItemApoio[]>([]);
  const [slots, setSlots] = useState<Slot[]>([]);
  const [carregandoSlots, setCarregandoSlots] = useState(false);
  const [motivoSemHorario, setMotivoSemHorario] = useState('');
  const temporizador = useRef<ReturnType<typeof setTimeout> | null>(null);

  const pacienteId = Form.useWatch('pacienteId', form);
  const profId = Form.useWatch('profissionalId', form);
  const uniId = Form.useWatch('unidadeId', form);
  const modalidade = Form.useWatch('modalidade', form);
  const tipo = Form.useWatch('tipoAtendimento', form);
  const diaEscolhido = Form.useWatch('dia', form);
  const encaixe = Form.useWatch('encaixe', form);

  const paciente = pacientes.find((p) => p.id === pacienteId);
  const especialidadeEscolhida = Form.useWatch('especialidadeId', form);

  // Valor do convênio: vem do credenciamento (convênio + unidade + especialidade). Só leitura; o servidor também o aplica sozinho.
  const [valorConvenio, setValorConvenio] = useState<number | null>(null);
  const [semValorConvenio, setSemValorConvenio] = useState(false);
  // O administrador pode escolher qualquer convênio; os demais perfis usam o convênio do cadastro do paciente.
  const convenioForm = Form.useWatch('convenioId', form);
  const convenioDoPaciente = convenioForm;
  useEffect(() => {
    setValorConvenio(null);
    setSemValorConvenio(false);
    if (tipo !== 'CONVENIO' || !convenioDoPaciente || !uniId || !especialidadeEscolhida) return;
    let atual = true;
    api
      .get<{ valor?: number | null; credenciado: boolean }>(`/convenios/${convenioDoPaciente}/valor-consulta`, {
        unidadeId: uniId,
        especialidadeId: especialidadeEscolhida,
        data: diaEscolhido || undefined
      })
      .then((r) => {
        if (!atual) return;
        setValorConvenio(r.valor ?? null);
        setSemValorConvenio(r.valor === null || r.valor === undefined);
      })
      .catch(() => atual && setSemValorConvenio(true));
    return () => {
      atual = false;
    };
  }, [tipo, convenioDoPaciente, uniId, especialidadeEscolhida, diaEscolhido]);

  // Listas fixas, carregadas ao abrir.
  useEffect(() => {
    if (!aberto) return;
    setErroGeral('');
    setSlots([]);
    api.get<ItemApoio[]>('/apoio/profissionais').then(setProfissionais).catch(() => message.error('Não foi possível carregar os profissionais.'));
    api
      .get<ItemApoio[]>('/apoio/formas-pagamento')
      .then((l) => setFormas(l.filter((f) => f.complemento !== 'CONVENIO')))
      .catch(() => message.error('Não foi possível carregar as formas de pagamento.'));
    api.get<ItemApoio[]>('/apoio/convenios').then(setConvenios).catch(() => message.error('Não foi possível carregar os convênios.'));
    if (pacienteInicial) setPacientes([{ id: pacienteInicial.id, nome: pacienteInicial.nome } as PacienteResumo]);
    buscarPacientes(pacienteInicial?.nome ?? '');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto]);

  // Convênio: começa pelo do cadastro do paciente (pode trocar). Em atendimento particular o campo fica vazio, assim o valor
  // do convênio só aparece quando há um convênio escolhido.
  useEffect(() => {
    form.setFieldValue('convenioId', tipo === 'CONVENIO' ? paciente?.convenio?.id : undefined);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tipo, pacienteId, paciente?.convenio?.id]);

  // Unidades e especialidades do profissional escolhido.
  useEffect(() => {
    if (!profId) {
      setUnidades([]);
      setEspecialidades([]);
      return;
    }
    Promise.all([
      api.get<ItemApoio[]>('/apoio/unidades', { profissionalId: profId }),
      api.get<ItemApoio[]>('/apoio/especialidades', { profissionalId: profId })
    ])
      .then(([u, e]) => {
        setUnidades(u);
        setEspecialidades(e);
        const atual = form.getFieldsValue();
        if (atual.unidadeId && !u.some((x) => x.id === atual.unidadeId)) form.setFieldValue('unidadeId', undefined);
        if (!atual.unidadeId && u.length === 1) form.setFieldValue('unidadeId', u[0].id);
        if (atual.especialidadeId && !e.some((x) => x.id === atual.especialidadeId)) form.setFieldValue('especialidadeId', undefined);
        if (!atual.especialidadeId && e.length === 1) form.setFieldValue('especialidadeId', e[0].id);
      })
      .catch(() => message.error('Não foi possível carregar as unidades e especialidades do profissional.'));
  }, [profId, form, message]);

  // Salas da unidade (consulta presencial).
  useEffect(() => {
    if (!uniId) {
      setSalas([]);
      return;
    }
    api
      .get<ItemApoio[]>('/apoio/salas', { unidadeId: uniId })
      .then((l) => {
        setSalas(l);
        const atual = form.getFieldValue('salaId') as number | undefined;
        if (atual && !l.some((s) => s.id === atual)) form.setFieldValue('salaId', undefined);
      })
      .catch(() => message.error('Não foi possível carregar as salas.'));
  }, [uniId, form, message]);

  // Horários livres do dia.
  const carregarSlots = useCallback(async () => {
    setMotivoSemHorario('');
    if (!profId || !uniId || !diaEscolhido || encaixe) {
      setSlots([]);
      return;
    }
    setCarregandoSlots(true);
    try {
      const lista = await api.get<Slot[]>('/agenda/horarios-disponiveis', {
        profissionalId: profId,
        unidadeId: uniId,
        dataInicial: diaEscolhido,
        dataFinal: diaEscolhido
      });
      // O dia é o de Brasília; o servidor já devolve só os horários desse dia.
      const doDia = lista.filter((s) => diaBrasilia(s.inicio) === diaEscolhido);
      setSlots(doDia);
      if (doDia.length === 0) setMotivoSemHorario(await descobrirMotivo(profId, uniId, diaEscolhido));
      const escolhido = form.getFieldValue('slotInicio') as string | undefined;
      if (escolhido && !lista.some((s) => s.inicio === escolhido)) form.setFieldValue('slotInicio', undefined);
    } catch (e) {
      setSlots([]);
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os horários.');
    } finally {
      setCarregandoSlots(false);
    }
  }, [profId, uniId, diaEscolhido, encaixe, form, message]);

  useEffect(() => {
    void carregarSlots();
  }, [carregarSlots]);

  /** Explica por que o dia não tem horários: fim de semana, feriado, recesso, sem expediente ou dia cheio. */
  async function descobrirMotivo(prof: number, uni: number, diaConsulta: string): Promise<string> {
    if (diaDaSemana(diaConsulta) >= 6) return 'Não é possível agendar para esta data, pois a clínica não atende aos sábados e domingos.';
    try {
      const ag = await api.get<AgendaSaida>(`/agenda/profissionais/${prof}`, { dataInicial: diaConsulta, dataFinal: diaConsulta, unidadeId: uni });
      const feriado = ag.bloqueios.find((b) => b.tipo === 'FERIADO');
      if (feriado) return `Não é possível agendar para esta data, pois é feriado de ${(feriado.descricao ?? 'feriado').split(' (')[0]}.`;
      if (ag.bloqueios.some((b) => b.tipo === 'RECESSO')) return 'Não é possível agendar para esta data, pois a clínica está em recesso de fim de ano.';
      if (ag.expediente.length === 0) return 'O profissional não atende neste dia da semana nesta unidade.';
    } catch {
      // Sem a liberação da agenda, cai na mensagem geral.
    }
    return 'Não há horários livres neste dia (todos ocupados ou já passaram). Escolha outra data ou marque encaixe.';
  }

  function buscarPacientes(texto: string) {
    if (temporizador.current) clearTimeout(temporizador.current);
    temporizador.current = setTimeout(async () => {
      setBuscandoPaciente(true);
      try {
        const r = await api.get<Pagina<PacienteResumo>>('/pacientes', { busca: texto.trim(), ativo: true, pagina: 1, tamanho: 20 });
        // Mantém o paciente já escolhido na lista, mesmo que a nova busca não o traga.
        setPacientes((antes) => {
          const escolhido = antes.find((p) => p.id === form.getFieldValue('pacienteId'));
          return escolhido && !r.itens.some((p) => p.id === escolhido.id) ? [escolhido, ...r.itens] : r.itens;
        });
      } catch {
        setPacientes([]);
      } finally {
        setBuscandoPaciente(false);
      }
    }, 300);
  }

  async function enviar(justificativa?: string) {
    let v: Valores;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    setErroGeral('');

    let inicio: string;
    if (v.encaixe) {
      if (!v.encaixeInicio) {
        form.setFields([{ name: 'encaixeInicio', errors: [OBRIGATORIO] }]);
        return;
      }
      inicio = new Date(`${v.encaixeInicio}:00-03:00`).toISOString();
    } else {
      if (!v.slotInicio) {
        form.setFields([{ name: 'slotInicio', errors: ['Escolha um horário'] }]);
        return;
      }
      inicio = v.slotInicio;
    }

    const convenioEscolhido = v.convenioId;
    if (v.tipoAtendimento === 'CONVENIO' && !convenioEscolhido) {
      setErroGeral('Escolha o convênio.');
      return;
    }

    const corpo: ConsultaEntrada = {
      pacienteId: v.pacienteId,
      profissionalId: v.profissionalId,
      especialidadeId: v.especialidadeId,
      unidadeId: v.unidadeId,
      salaId: v.modalidade === 'PRESENCIAL' ? v.salaId ?? null : null,
      modalidade: v.modalidade,
      tipoAtendimento: v.tipoAtendimento,
      convenioId: v.tipoAtendimento === 'CONVENIO' ? convenioEscolhido ?? null : null,
      valor: v.tipoAtendimento === 'CONVENIO' ? 0 : v.valor ?? 0,
      formaPagamentoId: v.tipoAtendimento === 'CONVENIO' ? null : v.formaPagamentoId ?? null,
      inicio,
      encaixe: v.encaixe,
      pagamentoConfirmado: v.tipoAtendimento === 'CONVENIO' ? false : !!v.pagamentoConfirmado,
      liberacaoCredenciamento: justificativa ? { justificativa } : null,
      liberacaoAdministrador: justificativa ? { justificativa } : null
    };

    setSalvando(true);
    try {
      const consulta = await api.post<Consulta>('/consultas', corpo);
      message.success('Consulta agendada.');
      if (consulta.avisos && consulta.avisos.length > 0) {
        modal.info({
          title: 'Consulta agendada',
          content: (
            <Space direction="vertical">
              {consulta.avisos.map((a) => (
                <span key={a.codigo}>{a.mensagem}</span>
              ))}
            </Space>
          )
        });
      }
      aoSalvar();
    } catch (e) {
      if (!(e instanceof ApiErro)) {
        message.error('Erro inesperado.');
      } else if (administrador && LIBERAVEIS.includes(e.codigo ?? '')) {
        const aviso = ['CONVENIO_NAO_CREDENCIADO', 'CONVENIO_DIVERGENTE'].includes(e.codigo ?? '') ? 'O convênio informado não possui credenciamento ativo no sistema.' : e.message;
        setSalvando(false);
        void pedirJustificativa(modal, message, aviso).then((j) => {
          if (j) void enviar(j);
        });
      } else if (e.codigo === 'CONVENIO_NAO_CREDENCIADO') {
        setErroGeral(e.message);
      } else if (e.erros.length > 0) {
        form.setFields(
          e.erros.map((x) => ({ name: (x.campo === 'inicio' ? 'slotInicio' : x.campo) as keyof Valores, errors: [x.mensagem] }))
        );
        setErroGeral(e.erros.map((x) => x.mensagem).join(' '));
      } else if (e.status === 409 || e.status === 400 || e.status === 403) {
        setErroGeral(MENSAGENS_ERRO[e.codigo ?? ''] ?? e.message);
        if (e.codigo === 'HORARIO_INDISPONIVEL') void carregarSlots();
      } else {
        message.error(e.message);
      }
    } finally {
      setSalvando(false);
    }
  }

  const hoje = hojeBrasilia();

  return (
    <Modal
      open={aberto}
      title="Nova consulta"
      okText="Agendar"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
      width={640}
    >
      <Form<Valores>
        form={form}
        layout="vertical"
        requiredMark
        preserve={false}
        initialValues={{
          pacienteId: pacienteInicial?.id,
          profissionalId,
          unidadeId,
          especialidadeId,
          dia: dia ?? adicionarDias(hoje, 0),
          modalidade: 'PRESENCIAL',
          tipoAtendimento: 'PARTICULAR',
          valor: null,
          encaixe: false,
          pagamentoConfirmado: false
        }}
      >
        {erroGeral && <Alert type="error" showIcon message={erroGeral} style={{ marginBottom: 16 }} />}

        <Form.Item name="pacienteId" label="Paciente" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select
            showSearch
            filterOption={false}
            onSearch={buscarPacientes}
            notFoundContent={buscandoPaciente ? <Spin size="small" /> : 'Nenhum paciente encontrado'}
            placeholder="Digite o nome ou o CPF do paciente"
            options={pacientes.map((p) => ({ value: p.id, label: p.cpfMascarado ? `${p.nome} - ${p.cpfMascarado}` : p.nome }))}
          />
        </Form.Item>

        <Space align="start" wrap style={{ width: '100%' }}>
          <Form.Item name="profissionalId" label="Profissional" rules={[{ required: true, message: OBRIGATORIO }]}>
            <Select
              showSearch
              optionFilterProp="label"
              style={{ width: 290 }}
              placeholder="Selecione o profissional"
              options={profissionais.map((p) => ({ value: p.id, label: p.nome }))}
            />
          </Form.Item>
          <Form.Item name="unidadeId" label="Unidade" rules={[{ required: true, message: OBRIGATORIO }]}>
            <Select
              style={{ width: 270 }}
              placeholder="Selecione a unidade"
              options={unidades.map((u) => ({ value: u.id, label: u.nome }))}
              disabled={!profId}
            />
          </Form.Item>
        </Space>

        <Form.Item name="especialidadeId" label="Especialidade" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select
            placeholder="Selecione a especialidade"
            options={especialidades.map((e) => ({ value: e.id, label: e.nome }))}
            disabled={!profId}
          />
        </Form.Item>

        <Space align="start" wrap style={{ width: '100%' }}>
          <Form.Item name="modalidade" label="Modalidade" rules={[{ required: true, message: OBRIGATORIO }]}>
            <Radio.Group
              optionType="button"
              options={[
                { value: 'PRESENCIAL', label: 'Presencial' },
                { value: 'ONLINE', label: 'Online' }
              ]}
            />
          </Form.Item>
          {modalidade === 'PRESENCIAL' && (
            <Form.Item name="salaId" label="Sala" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Select
                style={{ width: 300 }}
                placeholder="Selecione a sala"
                disabled={!uniId}
                options={salas.map((s) => ({ value: s.id, label: s.complemento ? `${s.nome} (${s.complemento})` : s.nome }))}
              />
            </Form.Item>
          )}
        </Space>
        {modalidade === 'ONLINE' && (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 16 }}
            message="O link da consulta online ainda não é gerado automaticamente. Informe o link ao paciente por outro meio."
          />
        )}

        <Form.Item name="tipoAtendimento" label="Tipo de atendimento" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Radio.Group
            optionType="button"
            options={[
              { value: 'PARTICULAR', label: 'Particular' },
              { value: 'CONVENIO', label: 'Convênio' }
            ]}
          />
        </Form.Item>
        {tipo === 'CONVENIO' && (
          <Form.Item
            name="convenioId"
            label="Convênio"
            extra={
              administrador && paciente?.convenio && convenioForm && convenioForm !== paciente.convenio.id
                ? `Diferente do convênio do cadastro do paciente (${paciente.convenio.nomeFantasia}). Será pedida a justificativa da liberação ao agendar.`
                : undefined
            }
          >
            <Select
              showSearch
              optionFilterProp="label"
              placeholder="Escolha o convênio"
              options={convenios.map((c) => ({ value: c.id, label: c.nome }))}
            />
          </Form.Item>
        )}

        {tipo === 'CONVENIO' && (
          <Form.Item label="Valor" extra={semValorConvenio ? 'Sem valor cadastrado para este convênio, unidade e especialidade (veja Credenciamentos).' : undefined}>
            <Input
              readOnly
              style={{ width: 150 }}
              value={valorConvenio !== null ? valorConvenio.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' }) : ''}
            />
          </Form.Item>
        )}

        <Space align="start" wrap style={{ width: '100%' }}>
          {tipo !== 'CONVENIO' && (
            <Form.Item name="valor" label="Valor (R$)" rules={[{ required: true, message: OBRIGATORIO }]}>
              <InputNumber min={0} step={10} precision={2} decimalSeparator="," style={{ width: 150 }} />
            </Form.Item>
          )}
          {tipo !== 'CONVENIO' && (
            <Form.Item name="formaPagamentoId" label="Forma de pagamento" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Select
                style={{ width: 280 }}
                placeholder="Selecione"
                options={formas.map((f) => ({ value: f.id, label: f.nome }))}
              />
            </Form.Item>
          )}
        </Space>

        <Form.Item name="dia" label="Data" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Input type="date" min={hoje} style={{ width: 180 }} />
        </Form.Item>

        {!encaixe && (
          <Form.Item name="slotInicio" label="Horário">
            {carregandoSlots ? (
              <Spin size="small" />
            ) : slots.length === 0 ? (
              profId && uniId && motivoSemHorario ? <Alert type="warning" showIcon message={motivoSemHorario} /> : <span style={{ color: '#999' }}>Escolha o profissional, a unidade e a data</span>
            ) : (
              <Radio.Group
                optionType="button"
                buttonStyle="solid"
                options={slots.map((s) => ({ value: s.inicio, label: horaBrasilia(s.inicio) }))}
              />
            )}
          </Form.Item>
        )}

        <Form.Item name="encaixe" valuePropName="checked">
          <Checkbox>Encaixe (fora do expediente ou dos intervalos; nunca sobrepõe outra consulta)</Checkbox>
        </Form.Item>
        {encaixe && (
          <Form.Item name="encaixeInicio" label="Data e hora do encaixe (horário de Brasília)">
            <Input type="datetime-local" style={{ width: 220 }} />
          </Form.Item>
        )}

        {tipo !== 'CONVENIO' && (
        <Form.Item name="pagamentoConfirmado" valuePropName="checked">
          <Checkbox>Pagamento já recebido (por exemplo, PIX adiantado)</Checkbox>
        </Form.Item>
        )}
      </Form>
    </Modal>
  );
}
