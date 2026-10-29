import { useCallback, useEffect, useState } from 'react';
import { Alert, App, Button, Form, Input, Modal, Radio, Select, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { EditOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio, Pagina } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import { formatarDataHora } from '../../util/formatos';
import { CORES_STATUS, NOMES_STATUS } from '../agenda/tipos';
import type { StatusConsulta } from '../agenda/tipos';

const OBRIGATORIO = 'Campo Não Preenchido';
const TAMANHO = 10;
const REAIS = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

type StatusGuia = 'PENDENTE' | 'AUTORIZADA' | 'NEGADA';

interface Guia {
  consultaId: string;
  inicio: string;
  statusConsulta: StatusConsulta;
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
  NEGADA: { label: 'Negada', cor: 'red' }
};

interface Valores {
  status: StatusGuia;
  numero?: string;
  observacao?: string;
}

function GuiaModal({ guia, aoFechar, aoSalvar }: { guia: Guia | null; aoFechar: () => void; aoSalvar: () => void }) {
  const { message } = App.useApp();
  const [form] = Form.useForm<Valores>();
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState('');
  const status = Form.useWatch('status', form);

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
      title="Guia do convênio"
      okText="Salvar"
      cancelText="Cancelar"
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
          initialValues={{ status: guia.guiaStatus, numero: guia.guiaNumero ?? '', observacao: guia.guiaObservacao ?? '' }}
        >
          {erro && <Alert type="error" showIcon message={erro} style={{ marginBottom: 16 }} />}
          <p style={{ marginTop: 0 }}>
            <strong>{guia.paciente}</strong> - {guia.convenio.nome}
            {guia.numeroCarteirinha ? ` (carteirinha ${guia.numeroCarteirinha})` : ''}
            <br />
            {guia.especialidade.nome} com {guia.profissional.nome}, em {formatarDataHora(guia.inicio)}
          </p>
          <Form.Item name="status" label="Situação da guia" rules={[{ required: true, message: OBRIGATORIO }]}>
            <Radio.Group
              optionType="button"
              options={[
                { value: 'AUTORIZADA', label: 'Autorizada' },
                { value: 'NEGADA', label: 'Negada' },
                { value: 'PENDENTE', label: 'Pendente' }
              ]}
            />
          </Form.Item>
          <Form.Item name="numero" label="Número da guia" rules={[{ required: status === 'AUTORIZADA', whitespace: true, message: OBRIGATORIO }]}>
            <Input maxLength={30} />
          </Form.Item>
          <Form.Item
            name="observacao"
            label={status === 'NEGADA' ? 'Motivo da negativa' : 'Observação'}
            rules={[{ required: status === 'NEGADA', whitespace: true, message: OBRIGATORIO }]}
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

  const [dados, setDados] = useState<Pagina<Guia>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [convenios, setConvenios] = useState<ItemApoio[]>([]);
  const [status, setStatus] = useState<StatusGuia | undefined>('PENDENTE');
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
    { title: 'Paciente', dataIndex: 'paciente' },
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
      render: (_, g) => podeEditar && <Button size="small" icon={<EditOutlined />} title="Registrar guia" onClick={() => setEmEdicao(g)} />
    }
  ];

  return (
    <>
      <Space style={{ marginBottom: 16 }} wrap>
        <Select<StatusGuia>
          allowClear
          placeholder="Guia"
          style={{ width: 160 }}
          value={status}
          onChange={filtrar(setStatus)}
          options={[
            { value: 'PENDENTE', label: 'Pendentes' },
            { value: 'AUTORIZADA', label: 'Autorizadas' },
            { value: 'NEGADA', label: 'Negadas' }
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

      <GuiaModal
        guia={emEdicao}
        aoFechar={() => setEmEdicao(null)}
        aoSalvar={() => {
          setEmEdicao(null);
          void carregar();
        }}
      />
    </>
  );
}
