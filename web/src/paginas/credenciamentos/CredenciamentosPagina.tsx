import { useCallback, useEffect, useState } from 'react';
import { App, Alert, Button, Checkbox, Col, Form, Input, Modal, Row, Select, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { EditOutlined, PlusOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { Convenio, Credenciamento, ItemApoio, Pagina, SituacaoCredenciamento } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import { formatarDataIso } from '../../util/formatos';

const OBRIGATORIO = 'Campo Não Preenchido';
const TAMANHO = 10;

const SITUACOES: { value: SituacaoCredenciamento; label: string; cor?: string }[] = [
  { value: 'APROVADO', label: 'Aprovado', cor: 'green' },
  { value: 'EM_ANALISE', label: 'Em análise', cor: 'gold' },
  { value: 'NAO_CREDENCIADO', label: 'Não credenciado', cor: 'red' }
];

interface Valores {
  convenioId: number;
  unidadeId: number;
  especialidadeIds: number[];
  situacao: SituacaoCredenciamento;
  dataInicio?: string;
  dataFim?: string;
  protocolo?: string;
  observacao?: string;
}

interface FormularioProps {
  aberto: boolean;
  credenciamento: Credenciamento | null;
  convenios: ItemApoio[];
  aoFechar: () => void;
  aoSalvar: () => void;
}

function FormularioCredenciamento({ aberto, credenciamento, convenios, aoFechar, aoSalvar }: FormularioProps) {
  const { message } = App.useApp();
  const [form] = Form.useForm<Valores>();
  const [salvando, setSalvando] = useState(false);
  const [erroGeral, setErroGeral] = useState('');
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [especialidades, setEspecialidades] = useState<ItemApoio[]>([]);
  const situacao = Form.useWatch('situacao', form);
  const convenioSel = Form.useWatch('convenioId', form);
  const [idsDoConvenio, setIdsDoConvenio] = useState<number[] | null>(null);

  // Só as especialidades cadastradas no convênio podem ser credenciadas.
  useEffect(() => {
    setIdsDoConvenio(null);
    if (!convenioSel) return;
    form.setFieldValue('especialidadeIds', []);
    api
      .get<Convenio>(`/convenios/${convenioSel}`)
      .then((c) => setIdsDoConvenio(c.especialidades.map((x) => x.id)))
      .catch(() => setIdsDoConvenio(null));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [convenioSel]);

  useEffect(() => {
    if (!aberto) return;
    setErroGeral('');
    api.get<ItemApoio[]>('/apoio/unidades').then(setUnidades).catch(() => message.error('Não foi possível carregar as unidades.'));
    api.get<ItemApoio[]>('/apoio/especialidades').then(setEspecialidades).catch(() => message.error('Não foi possível carregar as especialidades.'));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto]);

  async function enviar() {
    let v: Valores;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    setErroGeral('');
    const opcional = (t?: string) => (t && t.trim() !== '' ? t.trim() : null);
    const comum = {
      situacao: v.situacao,
      dataInicio: opcional(v.dataInicio),
      dataFim: opcional(v.dataFim),
      protocolo: opcional(v.protocolo),
      observacao: opcional(v.observacao)
    };
    setSalvando(true);
    try {
      if (credenciamento) {
        await api.put(`/convenios/${credenciamento.convenio.id}/credenciamentos/${credenciamento.id}`, comum);
        message.success('Credenciamento atualizado.');
      } else {
        await api.post(`/convenios/${v.convenioId}/credenciamentos/lote`, {
          unidadeId: v.unidadeId,
          especialidadeIds: v.especialidadeIds,
          ...comum
        });
        message.success(v.especialidadeIds.length > 1 ? 'Credenciamentos incluídos.' : 'Credenciamento incluído.');
      }
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(e.erros.map((x) => ({ name: x.campo as keyof Valores, errors: [x.mensagem] })));
      } else {
        setErroGeral(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      }
    } finally {
      setSalvando(false);
    }
  }

  const cr = credenciamento;
  return (
    <Modal
      open={aberto}
      title={cr ? 'Editar credenciamento' : 'Incluir credenciamento'}
      okText="Salvar"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
      width={680}
    >
      <Form<Valores>
        form={form}
        layout="vertical"
        requiredMark
        preserve={false}
        initialValues={
          cr
            ? {
                situacao: cr.situacao,
                dataInicio: cr.dataInicio?.slice(0, 10) ?? '',
                dataFim: cr.dataFim?.slice(0, 10) ?? '',
                protocolo: cr.protocolo ?? '',
                observacao: cr.observacao ?? ''
              }
            : { situacao: 'APROVADO', especialidadeIds: [] }
        }
      >
        {erroGeral && <Alert type="error" showIcon message={erroGeral} style={{ marginBottom: 16 }} />}

        {cr ? (
          <>
            <Form.Item label="Convênio">
              <Input readOnly value={cr.convenio.nome} />
            </Form.Item>
            <Row gutter={16}>
              <Col xs={24} sm={12}>
                <Form.Item label="Unidade">
                  <Input readOnly value={`${cr.unidade.nome} (${cr.unidade.cidade}/${cr.unidade.uf})`} />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12}>
                <Form.Item label="Especialidade">
                  <Input readOnly value={cr.especialidade.nome} />
                </Form.Item>
              </Col>
            </Row>
          </>
        ) : (
          <>
            <Form.Item name="convenioId" label="Convênio" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Select
                showSearch
                optionFilterProp="label"
                placeholder="Selecione"
                options={convenios.filter((c) => c.ativo).map((c) => ({ value: c.id, label: c.nome }))}
              />
            </Form.Item>
            <Form.Item name="unidadeId" label="Unidade (define a cidade)" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Select placeholder="Selecione" options={unidades.map((u) => ({ value: u.id, label: u.nome }))} />
            </Form.Item>
            <Form.Item
              name="especialidadeIds"
              label="Especialidades"
              extra="Você pode escolher várias: todas recebem a mesma situação e vigência."
              rules={[{ required: true, type: 'array', min: 1, message: OBRIGATORIO }]}
            >
              <Select
                mode="multiple"
                optionFilterProp="label"
                placeholder="Selecione"
                options={especialidades.filter((x) => idsDoConvenio === null || idsDoConvenio.includes(x.id)).map((x) => ({ value: x.id, label: x.nome }))}
              />
            </Form.Item>
          </>
        )}

        <Form.Item name="situacao" label="Situação" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select options={SITUACOES.map((s) => ({ value: s.value, label: s.label }))} style={{ width: 240 }} />
        </Form.Item>
        <Row gutter={16}>
          <Col xs={24} sm={12}>
            <Form.Item name="dataInicio" label="Início da vigência" rules={[{ required: situacao === 'APROVADO', message: OBRIGATORIO }]}>
              <Input type="date" />
            </Form.Item>
          </Col>
          <Col xs={24} sm={12}>
            <Form.Item name="dataFim" label="Fim da vigência" extra="Vazio: sem prazo.">
              <Input type="date" />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item name="protocolo" label="Protocolo / nº do credenciamento">
          <Input maxLength={50} />
        </Form.Item>
        <Form.Item name="observacao" label="Observação">
          <Input.TextArea rows={3} maxLength={500} showCount />
        </Form.Item>
      </Form>
    </Modal>
  );
}

export default function CredenciamentosPagina() {
  const { message } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('convenio.credenciamento', 'incluir');
  const podeEditar = pode('convenio.credenciamento', 'editar');

  const [dados, setDados] = useState<Pagina<Credenciamento>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [convenios, setConvenios] = useState<ItemApoio[]>([]);
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [especialidades, setEspecialidades] = useState<ItemApoio[]>([]);
  const [convenioId, setConvenioId] = useState<number | undefined>();
  const [unidadeId, setUnidadeId] = useState<number | undefined>();
  const [especialidadeId, setEspecialidadeId] = useState<number | undefined>();
  const [situacao, setSituacao] = useState<SituacaoCredenciamento | undefined>();
  const [vigente, setVigente] = useState(false);
  const [formAberto, setFormAberto] = useState(false);
  const [emEdicao, setEmEdicao] = useState<Credenciamento | null>(null);

  useEffect(() => {
    api.get<ItemApoio[]>('/apoio/convenios', { incluirInativos: true }).then(setConvenios).catch(() => message.error('Não foi possível carregar os convênios.'));
    api.get<ItemApoio[]>('/apoio/unidades', { incluirInativos: true }).then(setUnidades).catch(() => undefined);
    api.get<ItemApoio[]>('/apoio/especialidades', { incluirInativos: true }).then(setEspecialidades).catch(() => undefined);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<Credenciamento>>('/credenciamentos', {
          convenioId,
          unidadeId,
          especialidadeId,
          situacao,
          vigente: vigente ? true : undefined,
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os credenciamentos.');
    } finally {
      setCarregando(false);
    }
  }, [convenioId, unidadeId, especialidadeId, situacao, vigente, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  const filtrar = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    setPagina(1);
  };

  const colunas: TableColumnsType<Credenciamento> = [
    { title: 'Convênio', render: (_, c) => c.convenio.nome },
    { title: 'Unidade', render: (_, c) => `${c.unidade.nome} (${c.unidade.cidade}/${c.unidade.uf})` },
    { title: 'Especialidade', render: (_, c) => c.especialidade.nome },
    {
      title: 'Situação',
      width: 150,
      render: (_, c) => {
        const s = SITUACOES.find((x) => x.value === c.situacao);
        return <Tag color={s?.cor}>{s?.label ?? c.situacao}</Tag>;
      }
    },
    {
      title: 'Vigência',
      width: 210,
      render: (_, c) =>
        c.dataInicio ? `${formatarDataIso(c.dataInicio)} até ${c.dataFim ? formatarDataIso(c.dataFim) : 'sem prazo'}` : '—'
    },
    {
      title: 'Vale hoje',
      width: 100,
      render: (_, c) => (c.vigenteHoje ? <Tag color="green">Sim</Tag> : <Tag color="red">Não</Tag>)
    },
    { title: 'Protocolo', dataIndex: 'protocolo', width: 140, render: (v?: string | null) => v ?? '—' },
    {
      title: 'Ações',
      width: 80,
      render: (_, c) =>
        podeEditar && (
          <Button
            size="small"
            icon={<EditOutlined />}
            title="Editar"
            onClick={() => {
              setEmEdicao(c);
              setFormAberto(true);
            }}
          />
        )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Credenciamento de convênios
        </Typography.Title>
        {podeIncluir && (
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => {
              setEmEdicao(null);
              setFormAberto(true);
            }}
          >
            Incluir credenciamento
          </Button>
        )}
      </Space>

      <Space style={{ marginBottom: 16 }} wrap>
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
        <Select
          allowClear
          placeholder="Unidade"
          style={{ width: 200 }}
          value={unidadeId}
          onChange={filtrar(setUnidadeId)}
          options={unidades.map((u) => ({ value: u.id, label: u.nome }))}
        />
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder="Especialidade"
          style={{ width: 200 }}
          value={especialidadeId}
          onChange={filtrar(setEspecialidadeId)}
          options={especialidades.map((x) => ({ value: x.id, label: x.nome }))}
        />
        <Select<SituacaoCredenciamento>
          allowClear
          placeholder="Situação"
          style={{ width: 180 }}
          value={situacao}
          onChange={filtrar(setSituacao)}
          options={SITUACOES.map((s) => ({ value: s.value, label: s.label }))}
        />
        <Checkbox
          checked={vigente}
          onChange={(e) => {
            setVigente(e.target.checked);
            setPagina(1);
          }}
        >
          Somente os que valem hoje
        </Checkbox>
      </Space>

      <Table<Credenciamento>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Nenhum credenciamento encontrado.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} credenciamento(s)`,
          onChange: setPagina
        }}
      />

      <FormularioCredenciamento
        aberto={formAberto}
        credenciamento={emEdicao}
        convenios={convenios}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />
    </>
  );
}
