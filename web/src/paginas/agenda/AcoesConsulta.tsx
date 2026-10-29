import { useEffect, useState } from 'react';
import { Alert, App, Button, Form, Input, Modal, Radio, Space, Spin } from 'antd';
import { CheckOutlined, CloseOutlined, CalendarOutlined, DollarOutlined, LoginOutlined, PlayCircleOutlined, StopOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import { useAuth } from '../../auth/AuthContext';
import { diaBrasilia, hojeBrasilia, horaBrasilia } from './datas';
import type { Consulta, Slot } from './tipos';

const OBRIGATORIO = 'Campo Não Preenchido';
const ATIVAS = ['AGENDADA', 'CONFIRMADA', 'CHECK_IN', 'EM_ATENDIMENTO'];

interface Props {
  consulta: Consulta;
  /** Chamado depois de qualquer mudança, para a tela recarregar a agenda e o detalhe. */
  aoAlterar: (novoId?: string) => void;
}

// ------------------------------------------------ cancelar / não concluído
interface MotivoValores {
  motivo: string;
  canceladoPor?: 'PACIENTE' | 'CLINICA';
}

function MotivoModal({
  aberto,
  consulta,
  tipo,
  aoFechar,
  aoAlterar
}: Props & { aberto: boolean; tipo: 'CANCELADA' | 'FALTA'; aoFechar: () => void }) {
  const { message } = App.useApp();
  const [form] = Form.useForm<MotivoValores>();
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState('');

  async function enviar() {
    let v: MotivoValores;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    setErro('');
    setSalvando(true);
    try {
      await api.patch(`/consultas/${consulta.id}/status`, {
        status: tipo,
        motivo: v.motivo.trim(),
        canceladoPor: tipo === 'CANCELADA' ? v.canceladoPor : null,
        versao: consulta.versao
      });
      message.success(tipo === 'CANCELADA' ? 'Consulta cancelada.' : 'Consulta marcada como não concluída (falta do paciente).');
      aoFechar();
      aoAlterar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(e.erros.map((x) => ({ name: x.campo as keyof MotivoValores, errors: [x.mensagem] })));
      } else {
        setErro(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      }
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Modal
      open={aberto}
      title={tipo === 'CANCELADA' ? 'Cancelar consulta' : 'Não concluído (paciente faltou)'}
      okText={tipo === 'CANCELADA' ? 'Cancelar consulta' : 'Registrar'}
      okButtonProps={{ danger: true }}
      cancelText="Voltar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
    >
      <Form<MotivoValores> form={form} layout="vertical" requiredMark preserve={false}>
        {erro && <Alert type="error" showIcon message={erro} style={{ marginBottom: 16 }} />}
        {tipo === 'CANCELADA' && (
          <Form.Item name="canceladoPor" label="Quem cancelou" rules={[{ required: true, message: OBRIGATORIO }]}>
            <Radio.Group
              options={[
                { value: 'PACIENTE', label: 'Paciente' },
                { value: 'CLINICA', label: 'Clínica' }
              ]}
            />
          </Form.Item>
        )}
        <Form.Item name="motivo" label="Motivo" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
          <Input.TextArea rows={3} maxLength={500} showCount />
        </Form.Item>
      </Form>
    </Modal>
  );
}

// ---------------------------------------------------------------- remarcar
interface RemarcarValores {
  dia: string;
  slotInicio?: string;
  motivo: string;
}

function RemarcarModal({ aberto, consulta, aoFechar, aoAlterar }: Props & { aberto: boolean; aoFechar: () => void }) {
  const { message } = App.useApp();
  const [form] = Form.useForm<RemarcarValores>();
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState('');
  const [slots, setSlots] = useState<Slot[]>([]);
  const [carregando, setCarregando] = useState(false);
  const dia = Form.useWatch('dia', form);

  useEffect(() => {
    setSlots([]);
    form.setFieldValue('slotInicio', undefined);
    if (!aberto || !dia) return;
    let atual = true;
    setCarregando(true);
    api
      .get<Slot[]>('/agenda/horarios-disponiveis', {
        profissionalId: consulta.profissional.id,
        unidadeId: consulta.unidade.id,
        dataInicial: dia,
        dataFinal: dia
      })
      .then((l) => atual && setSlots(l.filter((s) => diaBrasilia(s.inicio) === dia)))
      .catch((e) => atual && message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os horários.'))
      .finally(() => atual && setCarregando(false));
    return () => {
      atual = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto, dia, consulta.profissional.id, consulta.unidade.id]);

  async function enviar() {
    let v: RemarcarValores;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    if (!v.slotInicio) {
      form.setFields([{ name: 'slotInicio', errors: ['Escolha um horário'] }]);
      return;
    }
    setErro('');
    setSalvando(true);
    try {
      const nova = await api.post<Consulta>(`/consultas/${consulta.id}/remarcacao`, {
        novoInicio: v.slotInicio,
        motivo: v.motivo.trim(),
        encaixe: false,
        versao: consulta.versao
      });
      message.success('Consulta remarcada.');
      aoFechar();
      aoAlterar(nova.id);
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(
          e.erros.map((x) => ({ name: (x.campo === 'novoInicio' ? 'slotInicio' : x.campo) as keyof RemarcarValores, errors: [x.mensagem] }))
        );
      } else {
        setErro(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      }
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Modal
      open={aberto}
      title="Remarcar consulta"
      okText="Remarcar"
      cancelText="Voltar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
    >
      <Form<RemarcarValores> form={form} layout="vertical" requiredMark preserve={false} initialValues={{ dia: undefined }}>
        {erro && <Alert type="error" showIcon message={erro} style={{ marginBottom: 16 }} />}
        <p style={{ marginTop: 0 }}>
          Paciente <strong>{consulta.paciente.nome}</strong> com {consulta.profissional.nome} ({consulta.unidade.nome}). A consulta atual passa a
          Remarcada e uma nova é criada.
        </p>
        <Form.Item name="dia" label="Nova data" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Input type="date" min={hojeBrasilia()} style={{ width: 180 }} />
        </Form.Item>
        {dia && (
          <Form.Item name="slotInicio" label="Horário">
            {carregando ? (
              <Spin size="small" />
            ) : slots.length === 0 ? (
              <Alert type="warning" showIcon message="Não há horários livres nesta data para o profissional." />
            ) : (
              <Radio.Group
                optionType="button"
                buttonStyle="solid"
                options={slots.map((s) => ({ value: s.inicio, label: horaBrasilia(s.inicio) }))}
              />
            )}
          </Form.Item>
        )}
        <Form.Item name="motivo" label="Motivo da remarcação" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
          <Input.TextArea rows={3} maxLength={500} showCount />
        </Form.Item>
      </Form>
    </Modal>
  );
}

// ------------------------------------------------------------------ botões
export default function AcoesConsulta({ consulta, aoAlterar }: Props) {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const [motivo, setMotivo] = useState<'CANCELADA' | 'FALTA' | null>(null);
  const [remarcando, setRemarcando] = useState(false);

  const ativa = ATIVAS.includes(consulta.status);
  const vencida = new Date(consulta.inicio).getTime() <= Date.now();
  const podeStatus = pode('consulta.status', 'editar');
  const podeRemarcar = pode('consulta.agendamento', 'editar');
  const inicial = consulta.status === 'AGENDADA' || consulta.status === 'CONFIRMADA';

  const concluir = ((ativa && vencida) || consulta.status === 'EM_ATENDIMENTO') && podeStatus;
  const proximo =
    !podeStatus ? null
    : consulta.status === 'AGENDADA' ? { status: 'CONFIRMADA', texto: 'Confirmar agendamento', ok: 'Agendamento confirmado.', icone: <CheckOutlined /> }
    : consulta.status === 'CONFIRMADA' ? { status: 'CHECK_IN', texto: 'Check-in', ok: 'Check-in realizado.', icone: <LoginOutlined /> }
    : consulta.status === 'CHECK_IN' ? { status: 'EM_ATENDIMENTO', texto: 'Iniciar atendimento', ok: 'Atendimento iniciado.', icone: <PlayCircleOutlined /> }
    : null;
  const naoConcluido = inicial && vencida && podeStatus;
  const remarcar = inicial && podeRemarcar;
  const cancelar = ativa && consulta.status !== 'EM_ATENDIMENTO' && podeStatus;

  const confirmarPag = consulta.status === 'CHECK_IN' && consulta.tipoAtendimento === 'PARTICULAR' && !consulta.pagamentoConfirmado && podeRemarcar;

  if (!concluir && !naoConcluido && !remarcar && !cancelar && !confirmarPag && !proximo) return null;

  async function avancar() {
    if (!proximo) return;
    try {
      await api.patch(`/consultas/${consulta.id}/status`, { status: proximo.status, versao: consulta.versao });
      message.success(proximo.ok);
      aoAlterar();
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
    }
  }

  function confirmarPagamento() {
    modal.confirm({
      title: 'Confirmar pagamento',
      content: `Confirmar o recebimento do pagamento de ${consulta.paciente.nome} (${consulta.formaPagamento.nome})?`,
      okText: 'Confirmar pagamento',
      cancelText: 'Voltar',
      onOk: async () => {
        try {
          await api.post(`/consultas/${consulta.id}/pagamento/confirmacao`, { versao: consulta.versao });
          message.success('Pagamento confirmado.');
          aoAlterar();
        } catch (e) {
          message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
        }
      }
    });
  }

  function confirmarConclusao() {
    modal.confirm({
      title: 'Concluir consulta',
      content: `Marcar a consulta de ${consulta.paciente.nome} como concluída?`,
      okText: 'Concluído',
      cancelText: 'Voltar',
      onOk: async () => {
        try {
          await api.patch(`/consultas/${consulta.id}/status`, { status: 'CONCLUIDA', versao: consulta.versao });
          message.success('Consulta concluída.');
          aoAlterar();
        } catch (e) {
          message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
        }
      }
    });
  }

  return (
    <>
      <Space wrap style={{ marginTop: 16 }}>
        {proximo && (
          <Button type="primary" icon={proximo.icone} onClick={avancar}>
            {proximo.texto}
          </Button>
        )}
        {confirmarPag && (
          <Button type="primary" ghost icon={<DollarOutlined />} onClick={confirmarPagamento}>
            Confirmar pagamento
          </Button>
        )}
        {concluir && (
          <Button type="primary" icon={<CheckOutlined />} onClick={confirmarConclusao}>
            Concluído
          </Button>
        )}
        {naoConcluido && (
          <Button icon={<CloseOutlined />} onClick={() => setMotivo('FALTA')}>
            Não concluído
          </Button>
        )}
        {remarcar && (
          <Button icon={<CalendarOutlined />} onClick={() => setRemarcando(true)}>
            Remarcado
          </Button>
        )}
        {cancelar && (
          <Button danger icon={<StopOutlined />} onClick={() => setMotivo('CANCELADA')}>
            Cancelado
          </Button>
        )}
      </Space>

      <MotivoModal aberto={motivo !== null} consulta={consulta} aoAlterar={aoAlterar} tipo={motivo ?? 'CANCELADA'} aoFechar={() => setMotivo(null)} />
      <RemarcarModal aberto={remarcando} consulta={consulta} aoAlterar={aoAlterar} aoFechar={() => setRemarcando(false)} />
    </>
  );
}
