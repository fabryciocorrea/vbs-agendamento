import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, App, Button, Checkbox, Form, Input, InputNumber, Modal, Select, Space, Table, Tabs, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { CheckCircleOutlined, EditOutlined, EyeOutlined, GiftOutlined as IconeTela, PlusOutlined, PoweroffOutlined, StopOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio, PacienteResumo, Pagina } from '../../api/tipos';
import {
  NOMES_SITUACAO_PACOTE,
  formatarDiaPacote,
  formatarMoeda,
  type Pacote,
  type PacotePaciente,
  type PacotePacienteDetalhe,
  type SituacaoPacote
} from '../../api/tiposPacotes';
import { useAuth } from '../../auth/AuthContext';
import CabecalhoPagina from '../../layout/CabecalhoPagina';
import CampoBusca from '../../componentes/CampoBusca';
import { NOMES_STATUS } from '../agenda/tipos';
import type { StatusConsulta } from '../agenda/tipos';
import { formatarDataHora } from '../../util/formatos';

const OBRIGATORIO = 'Campo Não Preenchido';
const TAMANHO = 10;

type FiltroSituacao = 'todos' | 'ativos' | 'inativos';

// ====================================================================== modelos (cadastro)
interface ValoresModelo {
  nome: string;
  especialidadeId: number;
  quantidadeSessoes: number;
  valor: number;
  validadeDias: number;
}

function FormularioPacote({
  aberto,
  somenteLeitura,
  pacote,
  aoFechar,
  aoSalvar
}: {
  aberto: boolean;
  somenteLeitura?: boolean;
  pacote: Pacote | null;
  aoFechar: () => void;
  aoSalvar: () => void;
}) {
  const { message } = App.useApp();
  const [form] = Form.useForm<ValoresModelo>();
  const [salvando, setSalvando] = useState(false);
  const [especialidades, setEspecialidades] = useState<ItemApoio[]>([]);
  const quantidade = Form.useWatch('quantidadeSessoes', form);
  const valor = Form.useWatch('valor', form);

  useEffect(() => {
    if (!aberto) return;
    api
      .get<ItemApoio[]>('/apoio/especialidades')
      .then(setEspecialidades)
      .catch(() => message.error('Não foi possível carregar as especialidades.'));
  }, [aberto, message]);

  const valorSessao = quantidade && valor ? Math.round((valor / quantidade) * 100) / 100 : null;

  async function enviar() {
    let v: ValoresModelo;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    const corpo = {
      nome: v.nome.trim(),
      especialidadeId: v.especialidadeId,
      quantidadeSessoes: v.quantidadeSessoes,
      valor: v.valor,
      validadeDias: v.validadeDias
    };
    setSalvando(true);
    try {
      if (pacote) await api.put<Pacote>(`/pacotes/${pacote.id}`, corpo);
      else await api.post<Pacote>('/pacotes', corpo);
      message.success(pacote ? 'Pacote atualizado.' : 'Pacote cadastrado.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(e.erros.map((x) => ({ name: x.campo as keyof ValoresModelo, errors: [x.mensagem] })));
      } else if (e instanceof ApiErro && e.codigo === 'PACOTE_DUPLICADO') {
        form.setFields([{ name: 'nome', errors: [e.message] }]);
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
      title={somenteLeitura ? 'Visualizar pacote' : pacote ? 'Editar pacote' : 'Novo pacote'}
      okText="Salvar"
      okButtonProps={{ style: somenteLeitura ? { display: 'none' } : undefined }}
      cancelText={somenteLeitura ? 'Fechar' : 'Cancelar'}
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
    >
      <Form<ValoresModelo>
        form={form}
        disabled={somenteLeitura}
        layout="vertical"
        requiredMark
        preserve={false}
        initialValues={
          pacote
            ? {
                nome: pacote.nome,
                especialidadeId: pacote.especialidade.id,
                quantidadeSessoes: pacote.quantidadeSessoes,
                valor: pacote.valor,
                validadeDias: pacote.validadeDias
              }
            : { quantidadeSessoes: 4, validadeDias: 30 }
        }
      >
        {!somenteLeitura && pacote && (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 16 }}
            message="Mudar o pacote vale só para as próximas vendas. Os pacotes que pacientes já compraram não mudam."
          />
        )}
        <Form.Item name="nome" label="Nome do pacote" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
          <Input maxLength={100} placeholder="Ex.: Psicologia mensal (4 sessões)" />
        </Form.Item>
        <Form.Item name="especialidadeId" label="Especialidade" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select
            showSearch
            optionFilterProp="label"
            placeholder="Selecione"
            options={especialidades.map((e) => ({ value: e.id, label: e.nome }))}
          />
        </Form.Item>
        <Space align="start" wrap>
          <Form.Item name="quantidadeSessoes" label="Quantidade de sessões" rules={[{ required: true, message: OBRIGATORIO }]}>
            <InputNumber min={1} max={60} precision={0} style={{ width: 170 }} />
          </Form.Item>
          <Form.Item name="validadeDias" label="Validade (dias)" rules={[{ required: true, message: OBRIGATORIO }]}>
            <InputNumber min={1} max={365} precision={0} style={{ width: 140 }} />
          </Form.Item>
          <Form.Item name="valor" label="Valor total (R$)" rules={[{ required: true, message: OBRIGATORIO }]}>
            <InputNumber min={0.01} step={10} precision={2} decimalSeparator="," style={{ width: 170 }} />
          </Form.Item>
        </Space>
        {valorSessao !== null && (
          <Typography.Text type="secondary">
            Cada sessão sai por {formatarMoeda(valorSessao)}. A validade conta a partir do dia da venda.
          </Typography.Text>
        )}
      </Form>
    </Modal>
  );
}

function AbaModelos() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('pacote.cadastro', 'incluir');
  const podeEditar = pode('pacote.cadastro', 'editar');

  const [dados, setDados] = useState<Pagina<Pacote>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [situacao, setSituacao] = useState<FiltroSituacao>('todos');
  const [formAberto, setFormAberto] = useState(false);
  const [somenteLeitura, setSomenteLeitura] = useState(false);
  const [emEdicao, setEmEdicao] = useState<Pacote | null>(null);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<Pacote>>('/pacotes', {
          busca: buscaAplicada,
          ativo: situacao === 'todos' ? undefined : situacao === 'ativos',
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os pacotes.');
    } finally {
      setCarregando(false);
    }
  }, [buscaAplicada, situacao, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  function alterarStatus(p: Pacote) {
    const inativar = p.ativo;
    modal.confirm({
      title: inativar ? 'Inativar pacote' : 'Ativar pacote',
      content: inativar
        ? `Inativar o pacote "${p.nome}"? Ele deixa de ser oferecido em novas vendas; os pacotes já vendidos continuam valendo.`
        : `Ativar o pacote "${p.nome}"?`,
      okText: inativar ? 'Inativar' : 'Ativar',
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.patch(`/pacotes/${p.id}/status`, { ativo: !p.ativo });
          message.success(inativar ? 'Pacote inativado.' : 'Pacote ativado.');
          await carregar();
        } catch (e) {
          message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
        }
      }
    });
  }

  const colunas: TableColumnsType<Pacote> = [
    { title: 'Pacote', dataIndex: 'nome' },
    { title: 'Especialidade', render: (_, p) => p.especialidade.nome },
    { title: 'Sessões', dataIndex: 'quantidadeSessoes', width: 90, align: 'center' },
    { title: 'Valor total', dataIndex: 'valor', width: 120, align: 'right', render: (v: number) => formatarMoeda(v) },
    { title: 'Por sessão', dataIndex: 'valorSessao', width: 120, align: 'right', render: (v: number) => formatarMoeda(v) },
    { title: 'Validade', dataIndex: 'validadeDias', width: 100, render: (d: number) => `${d} dia(s)` },
    {
      title: 'Situação',
      dataIndex: 'ativo',
      width: 100,
      render: (ativo: boolean) => (ativo ? <Tag color="green">Ativo</Tag> : <Tag>Inativo</Tag>)
    },
    {
      title: 'Ações',
      width: 140,
      render: (_, p) => (
        <Space>
          <Button
            size="small"
            icon={<EyeOutlined />}
            title="Visualizar"
            onClick={() => {
              setEmEdicao(p);
              setSomenteLeitura(true);
              setFormAberto(true);
            }}
          />
          {podeEditar && (
            <>
              <Button
                size="small"
                icon={<EditOutlined />}
                title="Editar"
                onClick={() => {
                  setEmEdicao(p);
                  setSomenteLeitura(false);
                  setFormAberto(true);
                }}
              />
              <Button size="small" danger={p.ativo} icon={<PoweroffOutlined />} title={p.ativo ? 'Inativar' : 'Ativar'} onClick={() => alterarStatus(p)} />
            </>
          )}
        </Space>
      )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Space wrap>
          <CampoBusca
            placeholder="Buscar por nome"
            allowClear
            style={{ width: 280 }}
            value={busca}
            onChange={(e) => {
              setBusca(e.target.value);
              if (e.target.value === '') {
                setBuscaAplicada('');
                setPagina(1);
              }
            }}
            onSearch={(v) => {
              setBuscaAplicada(v.trim());
              setPagina(1);
            }}
          />
          <Select<FiltroSituacao>
            value={situacao}
            style={{ width: 160 }}
            onChange={(v) => {
              setSituacao(v);
              setPagina(1);
            }}
            options={[
              { value: 'todos', label: 'Todos' },
              { value: 'ativos', label: 'Somente ativos' },
              { value: 'inativos', label: 'Somente inativos' }
            ]}
          />
        </Space>
        {podeIncluir && (
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => {
              setEmEdicao(null);
              setSomenteLeitura(false);
              setFormAberto(true);
            }}
          >
            Novo pacote
          </Button>
        )}
      </Space>

      <Table<Pacote>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Nenhum pacote encontrado.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} pacote(s)`,
          onChange: setPagina
        }}
      />

      <FormularioPacote
        aberto={formAberto}
        somenteLeitura={somenteLeitura}
        pacote={emEdicao}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />
    </>
  );
}

// ====================================================================== pacotes vendidos
interface ValoresVenda {
  pacienteId: string;
  pacoteId: number;
  formaPagamentoId: number;
  pagamentoConfirmado: boolean;
}

function VenderPacoteModal({ aberto, aoFechar, aoSalvar }: { aberto: boolean; aoFechar: () => void; aoSalvar: () => void }) {
  const { message } = App.useApp();
  const [form] = Form.useForm<ValoresVenda>();
  const [salvando, setSalvando] = useState(false);
  const [erroGeral, setErroGeral] = useState('');
  const [pacientes, setPacientes] = useState<PacienteResumo[]>([]);
  const [buscando, setBuscando] = useState(false);
  const [modelos, setModelos] = useState<Pacote[]>([]);
  const [formas, setFormas] = useState<ItemApoio[]>([]);
  const temporizador = useRef<ReturnType<typeof setTimeout> | null>(null);
  const pacoteId = Form.useWatch('pacoteId', form);
  const modelo = modelos.find((m) => m.id === pacoteId);

  useEffect(() => {
    if (!aberto) return;
    setErroGeral('');
    api.get<Pacote[]>('/pacotes/para-venda').then(setModelos).catch(() => message.error('Não foi possível carregar os pacotes.'));
    api
      .get<ItemApoio[]>('/apoio/formas-pagamento')
      .then((l) => setFormas(l.filter((f) => f.complemento !== 'CONVENIO')))
      .catch(() => message.error('Não foi possível carregar as formas de pagamento.'));
    buscarPacientes('');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto]);

  function buscarPacientes(texto: string) {
    if (temporizador.current) clearTimeout(temporizador.current);
    temporizador.current = setTimeout(async () => {
      setBuscando(true);
      try {
        const r = await api.get<Pagina<PacienteResumo>>('/pacientes', { busca: texto.trim(), ativo: true, pagina: 1, tamanho: 20 });
        setPacientes(r.itens);
      } catch {
        setPacientes([]);
      } finally {
        setBuscando(false);
      }
    }, 300);
  }

  async function enviar() {
    let v: ValoresVenda;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    setErroGeral('');
    setSalvando(true);
    try {
      await api.post<PacotePaciente>(`/pacientes/${v.pacienteId}/pacotes`, {
        pacoteId: v.pacoteId,
        formaPagamentoId: v.formaPagamentoId,
        pagamentoConfirmado: !!v.pagamentoConfirmado
      });
      message.success('Pacote vendido.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        setErroGeral(e.erros.map((x) => x.mensagem).join(' '));
      } else {
        setErroGeral(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      }
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Modal
      open={aberto}
      title="Vender pacote"
      okText="Vender"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
    >
      <Form<ValoresVenda> form={form} layout="vertical" requiredMark preserve={false} initialValues={{ pagamentoConfirmado: false }}>
        {erroGeral && <Alert type="error" showIcon message={erroGeral} style={{ marginBottom: 16 }} />}
        <Form.Item name="pacienteId" label="Paciente" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select
            showSearch
            filterOption={false}
            onSearch={buscarPacientes}
            notFoundContent={buscando ? 'Buscando...' : 'Nenhum paciente encontrado'}
            placeholder="Digite o nome ou o CPF do paciente"
            options={pacientes.map((p) => ({ value: p.id, label: p.cpfMascarado ? `${p.nome} - ${p.cpfMascarado}` : p.nome }))}
          />
        </Form.Item>
        <Form.Item name="pacoteId" label="Pacote" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select
            showSearch
            optionFilterProp="label"
            placeholder="Selecione o pacote"
            options={modelos.map((m) => ({ value: m.id, label: `${m.nome} - ${m.especialidade.nome}` }))}
          />
        </Form.Item>
        {modelo && (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 16 }}
            message={`${modelo.quantidadeSessoes} sessão(ões) por ${formatarMoeda(modelo.valor)} (${formatarMoeda(modelo.valorSessao)} cada), válido por ${modelo.validadeDias} dia(s) a partir de hoje.`}
          />
        )}
        <Form.Item name="formaPagamentoId" label="Forma de pagamento" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select placeholder="Selecione" options={formas.map((f) => ({ value: f.id, label: f.nome }))} />
        </Form.Item>
        <Form.Item name="pagamentoConfirmado" valuePropName="checked">
          <Checkbox>Pagamento já recebido (por exemplo, PIX adiantado)</Checkbox>
        </Form.Item>
      </Form>
    </Modal>
  );
}

function SessoesPacoteModal({ pacote, aoFechar }: { pacote: PacotePaciente | null; aoFechar: () => void }) {
  const { message } = App.useApp();
  const [detalhe, setDetalhe] = useState<PacotePacienteDetalhe | null>(null);
  const [carregando, setCarregando] = useState(false);
  const id = pacote?.id;

  useEffect(() => {
    setDetalhe(null);
    if (!id) return;
    setCarregando(true);
    api
      .get<PacotePacienteDetalhe>(`/pacotes-vendidos/${id}`)
      .then(setDetalhe)
      .catch((e) => message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar o pacote.'))
      .finally(() => setCarregando(false));
  }, [id, message]);

  const base = detalhe ?? pacote;
  return (
    <Modal open={!!pacote} title={pacote ? `Sessões - ${pacote.nome}` : ''} onCancel={aoFechar} footer={null} width={720} destroyOnClose>
      {base && (
        <Space direction="vertical" style={{ width: '100%', marginBottom: 12 }} size={2}>
          <Typography.Text>
            Paciente: <strong>{base.paciente.nome}</strong> - {base.especialidade.nome}
          </Typography.Text>
          <Typography.Text>
            Valor: <strong>{formatarMoeda(base.valor)}</strong> - Forma de pagamento: <strong>{base.formaPagamento?.nome ?? '—'}</strong>
          </Typography.Text>
          <Typography.Text>
            Usadas: <strong>{base.sessoesUsadas}</strong> de {base.quantidadeSessoes} (restam {base.sessoesRestantes}) - válido até{' '}
            {formatarDiaPacote(base.dataValidade)}
          </Typography.Text>
          {base.status === 'CANCELADO' && (
            <Typography.Text type="danger">
              Pacote cancelado{base.motivoCancelamento ? `: ${base.motivoCancelamento}` : ''}
            </Typography.Text>
          )}
        </Space>
      )}
      <Table
        rowKey="consultaId"
        size="small"
        loading={carregando}
        pagination={false}
        dataSource={detalhe?.sessoes ?? []}
        locale={{ emptyText: 'Nenhuma consulta usou este pacote ainda.' }}
        columns={[
          { title: 'Data e hora', dataIndex: 'inicio', render: (i: string) => formatarDataHora(i) },
          { title: 'Profissional', dataIndex: 'profissional' },
          {
            title: 'Situação da consulta',
            dataIndex: 'status',
            render: (s: string) => NOMES_STATUS[s as StatusConsulta] ?? s
          },
          {
            title: 'Usa sessão',
            dataIndex: 'consome',
            width: 110,
            render: (c: boolean) => (c ? <Tag color="blue">Sim</Tag> : <Tag>Devolvida</Tag>)
          }
        ]}
      />
    </Modal>
  );
}

function AbaVendidos() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('pacote.venda', 'incluir');
  const podeEditar = pode('pacote.venda', 'editar');
  const podeExcluir = pode('pacote.venda', 'excluir');

  const [dados, setDados] = useState<Pagina<PacotePaciente>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [situacao, setSituacao] = useState<SituacaoPacote | 'todos'>('todos');
  const [somentePendentes, setSomentePendentes] = useState(false);
  const [vendaAberta, setVendaAberta] = useState(false);
  const [sessoesDe, setSessoesDe] = useState<PacotePaciente | null>(null);
  const [cancelando, setCancelando] = useState<PacotePaciente | null>(null);
  const [motivo, setMotivo] = useState('');
  const [erroCancelar, setErroCancelar] = useState('');
  const [enviandoCancelamento, setEnviandoCancelamento] = useState(false);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<PacotePaciente>>('/pacotes-vendidos', {
          busca: buscaAplicada,
          situacao: situacao === 'todos' ? undefined : situacao,
          pagamentoPendente: somentePendentes ? true : undefined,
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os pacotes vendidos.');
    } finally {
      setCarregando(false);
    }
  }, [buscaAplicada, situacao, somentePendentes, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  function confirmarPagamento(p: PacotePaciente) {
    modal.confirm({
      title: 'Confirmar pagamento do pacote',
      content: `Confirmar o recebimento de ${formatarMoeda(p.valor)} do pacote "${p.nome}" de ${p.paciente.nome}? As consultas já agendadas com o pacote passam a constar como pagas.`,
      okText: 'Confirmar pagamento',
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.post(`/pacotes-vendidos/${p.id}/pagamento`);
          message.success('Pagamento confirmado.');
          await carregar();
        } catch (e) {
          message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
        }
      }
    });
  }

  async function cancelarPacote() {
    if (!cancelando) return;
    if (motivo.trim() === '') {
      setErroCancelar(OBRIGATORIO);
      return;
    }
    setErroCancelar('');
    setEnviandoCancelamento(true);
    try {
      await api.post(`/pacotes-vendidos/${cancelando.id}/cancelamento`, { motivo: motivo.trim() });
      message.success('Pacote cancelado.');
      setCancelando(null);
      setMotivo('');
      await carregar();
    } catch (e) {
      setErroCancelar(e instanceof ApiErro ? e.message : 'Erro inesperado.');
    } finally {
      setEnviandoCancelamento(false);
    }
  }

  const corSituacao: Record<SituacaoPacote, string> = { DISPONIVEL: 'green', ENCERRADO: 'default', CANCELADO: 'red' };

  const colunas: TableColumnsType<PacotePaciente> = [
    { title: 'Paciente', render: (_, p) => p.paciente.nome },
    { title: 'Pacote', render: (_, p) => p.nome },
    { title: 'Especialidade', render: (_, p) => p.especialidade.nome },
    { title: 'Forma de pagamento', render: (_, p) => p.formaPagamento?.nome ?? '—' },
    {
      title: 'Sessões',
      width: 120,
      align: 'center',
      render: (_, p) => `${p.sessoesUsadas} de ${p.quantidadeSessoes}`
    },
    { title: 'Valor', dataIndex: 'valor', width: 120, align: 'right', render: (v: number) => formatarMoeda(v) },
    { title: 'Válido até', dataIndex: 'dataValidade', width: 110, render: (d: string) => formatarDiaPacote(d) },
    {
      title: 'Pagamento',
      width: 120,
      render: (_, p) => (p.pagamentoConfirmado ? <Tag color="green">Recebido</Tag> : <Tag color="orange">Pendente</Tag>)
    },
    {
      title: 'Situação',
      dataIndex: 'situacao',
      width: 110,
      render: (s: SituacaoPacote) => <Tag color={corSituacao[s]}>{NOMES_SITUACAO_PACOTE[s]}</Tag>
    },
    {
      title: 'Ações',
      width: 140,
      render: (_, p) => (
        <Space>
          <Button size="small" icon={<EyeOutlined />} title="Ver sessões" onClick={() => setSessoesDe(p)} />
          {podeEditar && p.status === 'ATIVO' && !p.pagamentoConfirmado && (
            <Button size="small" icon={<CheckCircleOutlined />} title="Confirmar pagamento" onClick={() => confirmarPagamento(p)} />
          )}
          {podeExcluir && p.status === 'ATIVO' && (
            <Button
              size="small"
              danger
              icon={<StopOutlined />}
              title="Cancelar pacote"
              onClick={() => {
                setCancelando(p);
                setMotivo('');
                setErroCancelar('');
              }}
            />
          )}
        </Space>
      )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Space wrap>
          <CampoBusca
            placeholder="Buscar por paciente ou pacote"
            allowClear
            style={{ width: 300 }}
            value={busca}
            onChange={(e) => {
              setBusca(e.target.value);
              if (e.target.value === '') {
                setBuscaAplicada('');
                setPagina(1);
              }
            }}
            onSearch={(v) => {
              setBuscaAplicada(v.trim());
              setPagina(1);
            }}
          />
          <Select<SituacaoPacote | 'todos'>
            value={situacao}
            style={{ width: 170 }}
            onChange={(v) => {
              setSituacao(v);
              setPagina(1);
            }}
            options={[
              { value: 'todos', label: 'Todas as situações' },
              { value: 'DISPONIVEL', label: 'Disponíveis' },
              { value: 'ENCERRADO', label: 'Encerrados' },
              { value: 'CANCELADO', label: 'Cancelados' }
            ]}
          />
          <Checkbox
            checked={somentePendentes}
            onChange={(e) => {
              setSomentePendentes(e.target.checked);
              setPagina(1);
            }}
          >
            Só com pagamento pendente
          </Checkbox>
        </Space>
        {podeIncluir && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setVendaAberta(true)}>
            Vender pacote
          </Button>
        )}
      </Space>

      <Table<PacotePaciente>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Nenhum pacote vendido encontrado.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} pacote(s) vendido(s)`,
          onChange: setPagina
        }}
      />

      <VenderPacoteModal
        aberto={vendaAberta}
        aoFechar={() => setVendaAberta(false)}
        aoSalvar={() => {
          setVendaAberta(false);
          void carregar();
        }}
      />
      <SessoesPacoteModal pacote={sessoesDe} aoFechar={() => setSessoesDe(null)} />

      <Modal
        open={!!cancelando}
        title="Cancelar pacote"
        okText="Cancelar pacote"
        okButtonProps={{ danger: true }}
        cancelText="Voltar"
        confirmLoading={enviandoCancelamento}
        onOk={() => void cancelarPacote()}
        onCancel={() => setCancelando(null)}
        maskClosable={false}
        destroyOnClose
      >
        {cancelando && (
          <Space direction="vertical" style={{ width: '100%' }}>
            <Typography.Text>
              Cancelar o pacote <strong>{cancelando.nome}</strong> de {cancelando.paciente.nome}? Só é possível quando nenhuma consulta o usou.
              {cancelando.pagamentoConfirmado && ' O pagamento já foi recebido: a devolução do valor é combinada fora do sistema.'}
            </Typography.Text>
            <Input.TextArea
              rows={3}
              maxLength={500}
              showCount
              placeholder="Motivo do cancelamento"
              value={motivo}
              onChange={(e) => setMotivo(e.target.value)}
            />
            {erroCancelar && <Typography.Text type="danger">{erroCancelar}</Typography.Text>}
          </Space>
        )}
      </Modal>
    </>
  );
}

// ====================================================================== página
export default function PacotesPagina() {
  const { pode } = useAuth();
  const verModelos = pode('pacote.cadastro', 'visualizar');
  const verVendidos = pode('pacote.venda', 'visualizar');

  const itens = [
    ...(verVendidos ? [{ key: 'vendidos', label: 'Pacotes vendidos', children: <AbaVendidos /> }] : []),
    ...(verModelos ? [{ key: 'modelos', label: 'Modelos de pacote', children: <AbaModelos /> }] : [])
  ];

  return (
    <>
      <div style={{ marginBottom: 16 }}>
        <CabecalhoPagina
          icone={<IconeTela />}
          titulo="Pacotes"
          subtitulo="Pacotes mensais de consultas particulares: modelos, vendas e sessões usadas"
        />
      </div>
      {itens.length === 0 ? (
        <Alert type="warning" showIcon message="O seu perfil não tem liberação para ver pacotes." />
      ) : (
        <Tabs items={itens} destroyInactiveTabPane />
      )}
    </>
  );
}
