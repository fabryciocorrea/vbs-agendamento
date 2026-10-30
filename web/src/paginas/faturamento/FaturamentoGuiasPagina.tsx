import { useCallback, useEffect, useState } from 'react';
import { Alert, App, Button, Form, Input, Modal, Radio, Select, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { EditOutlined, StopOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio, Pagina, Paciente } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import { formatarDataHora } from '../../util/formatos';
import FormularioPaciente from '../pacientes/FormularioPaciente';
import { CORES_STATUS, NOMES_STATUS } from '../agenda/tipos';
import type { StatusConsulta } from '../agenda/tipos';

const OBRIGATORIO = 'Campo Não Preenchido';
const TAMANHO = 10;
const REAIS = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

type StatusGuia = 'PENDENTE' | 'AUTORIZADA' | 'NEGADA' | 'CANCELADA';

interface Guia {
  consultaId: string;
  inicio: string;
  statusConsulta: StatusConsulta;
  pacienteId: string;
  paciente: string;
  convenio: { id: number; nome: string };
  numeroCarteirinha?: string | null;
  especialidade: { id: number; nome: string };
  profissional: { id: number; nome: string };
  valor: number;
  guiaStatus: StatusGuia;
  guiaNumero?: string | null;
  guiaObservacao?: string | null;
  guiaEm?: string | null;
  guiaPor?: { id: number; nomeCompleto: string } | null;
  liberacaoAdministrador?: string | null;
}

const NOMES_GUIA: Record<StatusGuia, { label: string; cor: string }> = {
  PENDENTE: { label: 'Pendente', cor: 'gold' },
  AUTORIZADA: { label: 'Autorizada', cor: 'green' },
  NEGADA: { label: 'Negada', cor: 'red' },
  CANCELADA: { label: 'Cancelada', cor: 'default' }
};

interface Valores {
  status: StatusGuia;
  numero?: string;
  observacao?: string;
}

function GuiaModal({ guia, podeEditar, podeCancelar, aoFechar, aoSalvar }: { guia: Guia | null; podeEditar: boolean; podeCancelar: boolean; aoFechar: () => void; aoSalvar: () => void }) {
  const { message } = App.useApp();
  const [form] = Form.useForm<Valores>();
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState('');
  const status = Form.useWatch('status', form);
  // Guia autorizada só pode ser cancelada (o paciente pode faltar e a guia precisa ser cancelada no plano).
  const soCancelar = guia?.guiaStatus === 'AUTORIZADA';
  const opcoes = soCancelar
    ? [{ value: 'CANCELADA', label: 'Cancelada' }]
    : [
        ...(podeEditar
          ? [
              { value: 'AUTORIZADA', label: 'Autorizada' },
              { value: 'NEGADA', label: 'Negada' },
              { value: 'PENDENTE', label: 'Pendente' }
            ]
          : []),
        ...(podeCancelar ? [{ value: 'CANCELADA', label: 'Cancelada' }] : [])
      ];

  async function enviar() {
    if (!guia) return;
    let v: Valores;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    setErro('');
    setSalvando(true);
    try {
      await api.patch(`/consultas/${guia.consultaId}/guia`, {
        status: v.status,
        numero: v.numero?.trim() || null,
        observacao: v.observacao?.trim() || null
      });
      message.success('Guia atualizada.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(e.erros.map((x) => ({ name: x.campo as keyof Valores, errors: [x.mensagem] })));
      } else {
        setErro(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      }
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Modal
      open={!!guia}
      title={soCancelar ? 'Cancelar guia do convênio' : 'Guia do convênio'}
      okText={soCancelar ? 'Cancelar guia' : 'Salvar'}
      okButtonProps={soCancelar ? { danger: true } : undefined}
      cancelText="Fechar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
    >
      {guia && (
        <Form<Valores>
          form={form}
          layout="vertical"
          requiredMark
          preserve={false}
          initialValues={{ status: opcoes.some((o) => o.value === guia.guiaStatus) ? guia.guiaStatus : opcoes[0]?.value, numero: guia.guiaNumero ?? '', observacao: guia.guiaObservacao ?? '' }}
        >
          {erro && <Alert type="error" showIcon message={erro} style={{ marginBottom: 16 }} />}
          <p style={{ marginTop: 0 }}>
            <strong>{guia.paciente}</strong> - {guia.convenio.nome}
            {guia.numeroCarteirinha ? ` (carteirinha ${guia.numeroCarteirinha})` : ''}
            <br />
            {guia.especialidade.nome} com {guia.profissional.nome}, em {formatarDataHora(guia.inicio)}
          </p>
          {soCancelar && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 16 }}
              message={
                guia.statusConsulta === 'CANCELADA'
                  ? `A consulta foi cancelada. Cancele a guia${guia.guiaNumero ? ` nº ${guia.guiaNumero}` : ''} no convênio e registre aqui.`
                  : `Guia autorizada${guia.guiaNumero ? ` (nº ${guia.guiaNumero})` : ''}: a única ação possível é cancelar. Use quando o paciente não vier ou a guia perder a validade.`
              }
            />
          )}
          <Form.Item name="status" label="Situação da guia" rules={[{ required: true, message: OBRIGATORIO }]}>
            <Radio.Group
              optionType="button"
              options={opcoes}
            />
          </Form.Item>
          {status !== 'CANCELADA' && (
            <Form.Item name="numero" label="Número da guia" rules={[{ required: status === 'AUTORIZADA', whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={30} />
            </Form.Item>
          )}
          <Form.Item
            name="observacao"
            label={status === 'NEGADA' ? 'Motivo da negativa' : status === 'CANCELADA' ? 'Motivo do cancelamento' : 'Observação'}
            rules={[{ required: status === 'NEGADA' || status === 'CANCELADA', whitespace: true, message: OBRIGATORIO }]}
          >
            <Input.TextArea rows={3} maxLength={500} showCount />
          </Form.Item>
          <Typography.Text type="secondary">Guia autorizada confirma o pagamento da consulta. Pendente ou negada desfaz a confirmação.</Typography.Text>
        </Form>
      )}
    </Modal>
  );
}

export default function GuiasConvenioAba() {
  const { message } = App.useApp();
  const { pode } = useAuth();
  const podeEditar = pode('faturamento.financeiro', 'editar');
  const podeCancelar = pode('faturamento.financeiro', 'cancelar');
  const podeVerPaciente = pode('paciente.cadastro', 'visualizar');
  const [pacienteAberto, setPacienteAberto] = useState<Paciente | null>(null);

  // Abre o cadastro do paciente numa janela sobre o faturamento, sem sair desta tela (a consulta fica registrada no log).
  async function abrirPaciente(id: string) {
    try {
      setPacienteAberto(await api.get<Paciente>(`/pacientes/${id}`));
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível abrir o cadastro do paciente.');
    }
  }

  const [dados, setDados] = useState<Pagina<Guia>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [convenios, setConvenios] = useState<ItemApoio[]>([]);
  const [status, setStatus] = useState<StatusGuia | undefined>(undefined);
  const [convenioId, setConvenioId] = useState<number | undefined>();
  const [de, setDe] = useState('');
  const [ate, setAte] = useState('');
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [emEdicao, setEmEdicao] = useState<Guia | null>(null);

  useEffect(() => {
    api.get<ItemApoio[]>('/apoio/convenios', { incluirInativos: true }).then(setConvenios).catch(() => undefined);
  }, []);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<Guia>>('/faturamento/guias', {
          status,
          convenioId,
          dataInicial: de || undefined,
          dataFinal: ate || undefined,
          busca: buscaAplicada || undefined,
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar as guias.');
    } finally {
      setCarregando(false);
    }
  }, [status, convenioId, de, ate, buscaAplicada, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  const filtrar = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    setPagina(1);
  };

  const colunas: TableColumnsType<Guia> = [
    { title: 'Data e hora', width: 150, render: (_, g) => formatarDataHora(g.inicio) },
    {
      title: 'Paciente',
      render: (_, g) =>
        podeVerPaciente ? (
          <Button type="link" style={{ padding: 0, height: 'auto', whiteSpace: 'normal', textAlign: 'left' }} title="Abrir o cadastro do paciente" onClick={() => void abrirPaciente(g.pacienteId)}>
            {g.paciente}
          </Button>
        ) : (
          g.paciente
        )
    },
    { title: 'Convênio', render: (_, g) => g.convenio.nome },
    { title: 'Carteirinha', dataIndex: 'numeroCarteirinha', width: 130, render: (v?: string | null) => v ?? '—' },
    { title: 'Especialidade', render: (_, g) => g.especialidade.nome },
    { title: 'Profissional', render: (_, g) => g.profissional.nome },
    { title: 'Valor', width: 110, align: 'right', render: (_, g) => REAIS.format(g.valor) },
    { title: 'Consulta', width: 120, render: (_, g) => <Tag color={CORES_STATUS[g.statusConsulta]}>{NOMES_STATUS[g.statusConsulta]}</Tag> },
    {
      title: 'Guia',
      width: 150,
      render: (_, g) => (
        <>
          <Tag color={NOMES_GUIA[g.guiaStatus].cor}>{NOMES_GUIA[g.guiaStatus].label}</Tag>
          {g.guiaNumero && <div style={{ fontSize: 12 }}>nº {g.guiaNumero}</div>}
          {g.liberacaoAdministrador && (
            <div style={{ fontSize: 12 }} title={g.liberacaoAdministrador}>
              <Tag color="volcano">Liberada pelo administrador</Tag>
            </div>
          )}
        </>
      )
    },
    {
      title: 'Ações',
      width: 80,
      render: (_, g) =>
        g.guiaStatus !== 'CANCELADA' &&
        (g.guiaStatus === 'AUTORIZADA' ? podeCancelar : podeEditar || podeCancelar) && (
          <Button
            size="small"
            danger={g.guiaStatus === 'AUTORIZADA'}
            icon={g.guiaStatus === 'AUTORIZADA' ? <StopOutlined /> : <EditOutlined />}
            title={g.guiaStatus === 'AUTORIZADA' ? 'Cancelar guia' : 'Registrar guia'}
            onClick={() => setEmEdicao(g)}
          />
        )
    }
  ];

  return (
    <>
      <Space style={{ marginBottom: 16 }} wrap>
        <Select<StatusGuia>
          allowClear
          placeholder="Situação da guia"
          style={{ width: 160 }}
          value={status}
          onChange={filtrar(setStatus)}
          options={[
            { value: 'PENDENTE', label: 'Pendentes' },
            { value: 'AUTORIZADA', label: 'Autorizadas' },
            { value: 'NEGADA', label: 'Negadas' },
            { value: 'CANCELADA', label: 'Canceladas' }
          ]}
        />
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder="Convênio"
          style={{ width: 220 }}
          value={convenioId}
          onChange={filtrar(setConvenioId)}
          options={convenios.map((c) => ({ value: c.id, label: c.nome }))}
        />
        <Input type="date" style={{ width: 150 }} value={de} onChange={(e) => filtrar(setDe)(e.target.value)} title="Consultas a partir de" />
        <Input type="date" style={{ width: 150 }} value={ate} onChange={(e) => filtrar(setAte)(e.target.value)} title="Consultas até" />
        <Input.Search
          placeholder="Paciente, carteirinha ou nº da guia"
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
      </Space>

      <Table<Guia>
        rowKey="consultaId"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Nenhuma guia encontrada.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} consulta(s)`,
          onChange: setPagina
        }}
      />

      <FormularioPaciente
        aberto={!!pacienteAberto}
        paciente={pacienteAberto}
        aoFechar={() => setPacienteAberto(null)}
        aoSalvar={() => {
          setPacienteAberto(null);
          void carregar();
        }}
      />

      <GuiaModal
        guia={emEdicao}
        podeEditar={podeEditar}
        podeCancelar={podeCancelar}
        aoFechar={() => setEmEdicao(null)}
        aoSalvar={() => {
          setEmEdicao(null);
          void carregar();
        }}
      />
    </>
  );
}
