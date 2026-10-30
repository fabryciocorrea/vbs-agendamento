import { useEffect, useState } from 'react';
import { Alert, App, Button, Form, Input, Modal, Radio, Space, Spin } from 'antd';
import { CheckOutlined, CloseOutlined, CalendarOutlined, DollarOutlined, LoginOutlined, PlayCircleOutlined, StopOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import { useAuth } from '../../auth/AuthContext';
import { diaBrasilia, hojeBrasilia, horaBrasilia } from './datas';
import { pedirJustificativa } from './liberacao';
import type { Consulta, Slot } from './tipos';

const OBRIGATORIO = 'Campo Não Preenchido';
const ATIVAS = ['AGENDADA', 'CONFIRMADA', 'CHECK_IN', 'EM_ATENDIMENTO'];

/** Bloqueios que o administrador pode liberar informando uma justificativa (fica gravada na consulta e no log). */
const CODIGOS_STATUS = [
  'GUIA_NAO_AUTORIZADA',
  'GUIA_AUTORIZADA_CANCELAR_ANTES',
  'PAGAMENTO_PENDENTE',
  'CANCELAMENTO_FORA_DO_PRAZO',
  'FALTA_ANTES_DO_HORARIO',
  'CONCLUSAO_ANTES_DO_HORARIO',
  'TRANSICAO_INVALIDA'
];
const CODIGOS_HORARIO = ['HORARIO_NO_PASSADO', 'HORARIO_BLOQUEADO', 'FORA_DO_EXPEDIENTE'];
const CODIGOS_PAGAMENTO = ['STATUS_NAO_PERMITE'];

type Liberacao = { justificativa: string };

/**
 * Executa uma chamada. Se o servidor bloquear por uma regra liberável e o usuário for administrador, abre a janela só com o
 * campo de justificativa e repete a chamada com a liberação. Devolve true quando deu certo; ao voltar sem liberar, false sem erro.
 */
function useLiberacao() {
  const { message, modal } = App.useApp();
  const { sessao } = useAuth();
  const admin = !!sessao?.administrador;

  async function executar(
    fn: (l?: Liberacao) => Promise<void>,
    aoErro: (e: unknown) => void,
    codigos: string[] = CODIGOS_STATUS
  ): Promise<boolean> {
    try {
      await fn(undefined);
      return true;
    } catch (e) {
      if (admin && e instanceof ApiErro && e.codigo && codigos.includes(e.codigo)) {
        const justificativa = await pedirJustificativa(modal, message, e.message);
        if (justificativa === null) return false;
        try {
          await fn({ justificativa });
          return true;
        } catch (e2) {
          aoErro(e2);
          return false;
        }
      }
      aoErro(e);
      return false;
    }
  }

  return { admin, executar };
}

function mensagemErro(e: unknown): string {
  return e instanceof ApiErro || e instanceof Error ? e.message : 'Erro inesperado.';
}

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
  const { executar } = useLiberacao();
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
    const ok = await executar(
      async (lib) => {
        await api.patch(`/consultas/${consulta.id}/status`, {
          status: tipo,
          motivo: v.motivo.trim(),
          canceladoPor: tipo === 'CANCELADA' ? v.canceladoPor : null,
          liberacaoAdministrador: lib ?? null,
          versao: consulta.versao
        });
      },
      (e) => {
        if (e instanceof ApiErro && e.erros.length > 0) {
          form.setFields(e.erros.map((x) => ({ name: x.campo as keyof MotivoValores, errors: [x.mensagem] })));
        } else {
          setErro(mensagemErro(e));
        }
      },
      CODIGOS_STATUS
    );
    setSalvando(false);
    if (ok) {
      message.success(tipo === 'CANCELADA' ? 'Consulta cancelada.' : 'Consulta marcada como não concluída (falta do paciente).');
      aoFechar();
      aoAlterar();
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
  const { executar } = useLiberacao();
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
    let novaId: string | undefined;
    const ok = await executar(
      async (lib) => {
        const nova = await api.post<Consulta>(`/consultas/${consulta.id}/remarcacao`, {
          novoInicio: v.slotInicio,
          motivo: v.motivo.trim(),
          encaixe: false,
          liberacaoAdministrador: lib ?? null,
          liberacaoCredenciamento: lib ?? null,
          versao: consulta.versao
        });
        novaId = nova.id;
      },
      (e) => {
        if (e instanceof ApiErro && e.erros.length > 0) {
          form.setFields(
            e.erros.map((x) => ({ name: (x.campo === 'novoInicio' ? 'slotInicio' : x.campo) as keyof RemarcarValores, errors: [x.mensagem] }))
          );
        } else {
          setErro(mensagemErro(e));
        }
      },
      [...CODIGOS_HORARIO, 'CONVENIO_NAO_CREDENCIADO']
    );
    setSalvando(false);
    if (ok) {
      message.success('Consulta remarcada.');
      aoFechar();
      aoAlterar(novaId);
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
  const { admin, executar } = useLiberacao();
  const [motivo, setMotivo] = useState<'CANCELADA' | 'FALTA' | null>(null);
  const [remarcando, setRemarcando] = useState(false);

  const ativa = ATIVAS.includes(consulta.status);
  const vencida = new Date(consulta.inicio).getTime() <= Date.now();
  const podeStatus = pode('consulta.status', 'editar');
  const podeCancelarConsulta = pode('consulta.status', 'cancelar');
  const podeRemarcar = pode('consulta.agendamento', 'editar');
  const inicial = consulta.status === 'AGENDADA' || consulta.status === 'CONFIRMADA';

  const guiaPendente = !admin && consulta.tipoAtendimento === 'CONVENIO' && (consulta.guia?.status ?? 'PENDENTE') !== 'AUTORIZADA';
  const textoGuia = consulta.guia?.status === 'CANCELADA' ? 'A guia está cancelada' : 'Aguardando a autorização da guia pelo faturamento';
  const concluir = ((ativa && (vencida || admin)) || consulta.status === 'EM_ATENDIMENTO') && podeStatus;
  const proximo =
    !podeStatus ? null
    : consulta.status === 'AGENDADA' ? { status: 'CONFIRMADA', texto: 'Confirmar agendamento', ok: 'Agendamento confirmado.', icone: <CheckOutlined /> }
    : consulta.status === 'CONFIRMADA' ? { status: 'CHECK_IN', texto: 'Check-in', ok: 'Check-in realizado.', icone: <LoginOutlined /> }
    : consulta.status === 'CHECK_IN' ? { status: 'EM_ATENDIMENTO', texto: 'Iniciar atendimento', ok: 'Atendimento iniciado.', icone: <PlayCircleOutlined /> }
    : null;
  const naoConcluido = inicial && (vencida || admin) && podeStatus;
  const remarcar = inicial && podeRemarcar;
  const cancelar = ativa && (consulta.status !== 'EM_ATENDIMENTO' || admin) && podeCancelarConsulta;

  const confirmarPag = (consulta.status === 'CHECK_IN' || (admin && ativa)) && consulta.tipoAtendimento === 'PARTICULAR' && !consulta.pagamentoConfirmado && podeRemarcar;

  if (!concluir && !naoConcluido && !remarcar && !cancelar && !confirmarPag && !proximo) return null;

  /** Diálogo de confirmação; se o servidor bloquear, o administrador recebe a janela da justificativa. */
  function confirmarAcao(o: {
    titulo: string;
    texto: string;
    okText: string;
    sucesso: string;
    codigos?: string[];
    fn: (l?: Liberacao) => Promise<void>;
  }) {
    modal.confirm({
      title: o.titulo,
      width: 520,
      okText: o.okText,
      cancelText: 'Voltar',
      content: o.texto,
      onOk: async () => {
        let erro = '';
        const ok = await executar(o.fn, (e) => (erro = mensagemErro(e)), o.codigos);
        if (!ok) {
          if (erro) message.error(erro);
          return Promise.reject(new Error('falha'));
        }
        message.success(o.sucesso);
        aoAlterar();
      }
    });
  }

  async function avancar() {
    if (!proximo) return;
    const fn = async (lib?: Liberacao) => {
      await api.patch(`/consultas/${consulta.id}/status`, { status: proximo.status, liberacaoAdministrador: lib ?? null, versao: consulta.versao });
    };
    const ok = await executar(fn, (e) => message.error(mensagemErro(e)));
    if (ok) {
      message.success(proximo.ok);
      aoAlterar();
    }
  }

  function confirmarPagamento() {
    confirmarAcao({
      titulo: 'Confirmar pagamento',
      texto: `Confirmar o recebimento do pagamento de ${consulta.paciente.nome} (${consulta.formaPagamento.nome})?`,
      okText: 'Confirmar pagamento',
      sucesso: 'Pagamento confirmado.',
      codigos: CODIGOS_PAGAMENTO,
      fn: async (lib) => {
        await api.post(`/consultas/${consulta.id}/pagamento/confirmacao`, { liberacaoAdministrador: lib ?? null, versao: consulta.versao });
      }
    });
  }

  function confirmarConclusao() {
    confirmarAcao({
      titulo: 'Concluir consulta',
      texto: `Marcar a consulta de ${consulta.paciente.nome} como concluída?`,
      okText: 'Concluído',
      sucesso: 'Consulta concluída.',
      fn: async (lib) => {
        await api.patch(`/consultas/${consulta.id}/status`, { status: 'CONCLUIDA', liberacaoAdministrador: lib ?? null, versao: consulta.versao });
      }
    });
  }

  return (
    <>
      <Space wrap style={{ marginTop: 16 }}>
        {proximo && (
          <Button
            type="primary"
            icon={proximo.icone}
            onClick={avancar}
            disabled={proximo.status === 'EM_ATENDIMENTO' && guiaPendente}
            title={proximo.status === 'EM_ATENDIMENTO' && guiaPendente ? textoGuia : undefined}
          >
            {proximo.texto}
          </Button>
        )}
        {confirmarPag && (
          <Button type="primary" ghost icon={<DollarOutlined />} onClick={confirmarPagamento}>
            Confirmar pagamento
          </Button>
        )}
        {concluir && (
          <Button
            type="primary"
            icon={<CheckOutlined />}
            onClick={confirmarConclusao}
            disabled={guiaPendente}
            title={guiaPendente ? textoGuia : undefined}
          >
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
            Cancelar
          </Button>
        )}
      </Space>

      <MotivoModal aberto={motivo !== null} consulta={consulta} aoAlterar={aoAlterar} tipo={motivo ?? 'CANCELADA'} aoFechar={() => setMotivo(null)} />
      <RemarcarModal aberto={remarcando} consulta={consulta} aoAlterar={aoAlterar} aoFechar={() => setRemarcando(false)} />
    </>
  );
}
