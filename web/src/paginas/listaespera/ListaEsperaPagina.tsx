import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, App, Button, Form, Input, Modal, Select, Space, Spin, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { CalendarOutlined, CheckOutlined, CloseOutlined, PlusOutlined, SearchOutlined, UndoOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio, ListaEspera, Pagina, PacienteResumo, StatusListaEspera } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import { formatarDataHora } from '../../util/formatos';
import NovaConsultaModal from '../agenda/NovaConsultaModal';

const OBRIGATORIO = 'Campo Não Preenchido';
const TAMANHO = 10;

const STATUS: Record<StatusListaEspera, { label: string; cor?: string }> = {
  AGUARDANDO: { label: 'Aguardando', cor: 'blue' },
  NOTIFICADO: { label: 'Horário liberado', cor: 'orange' },
  ATENDIDO: { label: 'Atendido', cor: 'green' },
  CANCELADO: { label: 'Cancelado' }
};

interface Valores {
  pacienteId: string;
  profissionalId?: number;
  especialidadeId?: number;
  unidadeId?: number;
  observacao?: string;
}

interface FormularioProps {
  aberto: boolean;
  profissionais: ItemApoio[];
  unidades: ItemApoio[];
  aoFechar: () => void;
  aoSalvar: () => void;
}

function FormularioEspera({ aberto, profissionais, unidades, aoFechar, aoSalvar }: FormularioProps) {
  const { message } = App.useApp();
  const [form] = Form.useForm<Valores>();
  const [salvando, setSalvando] = useState(false);
  const [erroGeral, setErroGeral] = useState('');
  const [pacientes, setPacientes] = useState<PacienteResumo[]>([]);
  const [buscando, setBuscando] = useState(false);
  const [especialidades, setEspecialidades] = useState<ItemApoio[]>([]);
  const temporizador = useRef<ReturnType<typeof setTimeout> | null>(null);
  const profissionalSel = Form.useWatch('profissionalId', form);

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

  useEffect(() => {
    if (!aberto) return;
    setErroGeral('');
    buscarPacientes('');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto]);

  // Com profissional escolhido, só as especialidades dele; sem, todas.
  useEffect(() => {
    if (!aberto) return;
    api
      .get<ItemApoio[]>('/apoio/especialidades', profissionalSel ? { profissionalId: profissionalSel } : undefined)
      .then((lista) => {
        setEspecialidades(lista);
        const atual = form.getFieldValue('especialidadeId');
        if (atual && !lista.some((x) => x.id === atual)) form.setFieldValue('especialidadeId', undefined);
      })
      .catch(() => message.error('Não foi possível carregar as especialidades.'));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto, profissionalSel]);

  async function enviar() {
    let v: Valores;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    if (!v.profissionalId && !v.especialidadeId) {
      setErroGeral('Informe o profissional, a especialidade ou ambos.');
      return;
    }
    setErroGeral('');
    setSalvando(true);
    try {
      await api.post('/lista-espera', {
        pacienteId: v.pacienteId,
        profissionalId: v.profissionalId ?? null,
        especialidadeId: v.especialidadeId ?? null,
        unidadeId: v.unidadeId ?? null,
        observacao: v.observacao && v.observacao.trim() !== '' ? v.observacao.trim() : null
      });
      message.success('Paciente incluído na lista de espera.');
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

  return (
    <Modal
      open={aberto}
      title="Incluir na lista de espera"
      okText="Incluir"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
      width={600}
    >
      <Form<Valores> form={form} layout="vertical" requiredMark preserve={false}>
        {erroGeral && <Alert type="error" showIcon message={erroGeral} style={{ marginBottom: 16 }} />}
        <Form.Item name="pacienteId" label="Paciente" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select
            showSearch
            filterOption={false}
            onSearch={buscarPacientes}
            notFoundContent={buscando ? <Spin size="small" /> : 'Nenhum paciente encontrado'}
            placeholder="Digite o nome ou o CPF do paciente"
            options={pacientes.map((p) => ({ value: p.id, label: p.nome }))}
          />
        </Form.Item>
        <Form.Item name="profissionalId" label="Profissional" extra="Informe o profissional, a especialidade ou ambos.">
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Qualquer profissional"
            options={profissionais.map((p) => ({ value: p.id, label: p.nome }))}
          />
        </Form.Item>
        <Form.Item name="especialidadeId" label="Especialidade">
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder="Qualquer especialidade"
            options={especialidades.map((x) => ({ value: x.id, label: x.nome }))}
          />
        </Form.Item>
        <Form.Item name="unidadeId" label="Unidade">
          <Select allowClear placeholder="Qualquer unidade" options={unidades.map((u) => ({ value: u.id, label: u.nome }))} />
        </Form.Item>
        <Form.Item name="observacao" label="Observação">
          <Input.TextArea rows={3} maxLength={250} showCount placeholder="Ex.: prefere o período da tarde" />
        </Form.Item>
      </Form>
    </Modal>
  );
}

export default function ListaEsperaPagina() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('listaespera.cadastro', 'incluir');
  const podeEditar = pode('listaespera.cadastro', 'editar');
  const podeCancelar = pode('listaespera.cadastro', 'cancelar');
  const podeAgendar = pode('consulta.agendamento', 'incluir');

  const [dados, setDados] = useState<Pagina<ListaEspera>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [avisados, setAvisados] = useState(0);
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [status, setStatus] = useState<StatusListaEspera | undefined>();
  const [profissionalId, setProfissionalId] = useState<number | undefined>();
  const [especialidadeId, setEspecialidadeId] = useState<number | undefined>();
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);
  const [especialidades, setEspecialidades] = useState<ItemApoio[]>([]);
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [formAberto, setFormAberto] = useState(false);
  const [paraAgendar, setParaAgendar] = useState<ListaEspera | null>(null);

  useEffect(() => {
    api.get<ItemApoio[]>('/apoio/profissionais').then(setProfissionais).catch(() => message.error('Não foi possível carregar os profissionais.'));
    api.get<ItemApoio[]>('/apoio/especialidades').then(setEspecialidades).catch(() => undefined);
    api.get<ItemApoio[]>('/apoio/unidades').then(setUnidades).catch(() => undefined);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<ListaEspera>>('/lista-espera', {
          status,
          profissionalId,
          especialidadeId,
          busca: buscaAplicada || undefined,
          pagina,
          tamanho: TAMANHO
        })
      );
      const av = await api.get<Pagina<ListaEspera>>('/lista-espera', { status: 'NOTIFICADO', pagina: 1, tamanho: 1 });
      setAvisados(av.total);
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar a lista de espera.');
    } finally {
      setCarregando(false);
    }
  }, [status, profissionalId, especialidadeId, buscaAplicada, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  const filtrar = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    setPagina(1);
  };

  async function mudarStatus(item: ListaEspera, novo: 'ATENDIDO' | 'CANCELADO' | 'AGUARDANDO') {
    try {
      await api.patch(`/lista-espera/${item.id}/status`, { status: novo });
      message.success(novo === 'ATENDIDO' ? 'Marcado como atendido.' : novo === 'CANCELADO' ? 'Item cancelado.' : 'Voltou para a fila.');
      await carregar();
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      await carregar();
    }
  }

  function confirmarCancelamento(item: ListaEspera) {
    modal.confirm({
      title: 'Cancelar item da lista de espera',
      content: `Retirar ${item.paciente.nome} da lista de espera?`,
      okText: 'Cancelar item',
      okButtonProps: { danger: true },
      cancelText: 'Voltar',
      onOk: () => mudarStatus(item, 'CANCELADO')
    });
  }

  const colunas: TableColumnsType<ListaEspera> = [
    { title: 'Entrada', width: 140, render: (_, i) => formatarDataHora(i.criadoEm) },
    { title: 'Paciente', render: (_, i) => i.paciente.nome },
    { title: 'Profissional', render: (_, i) => i.profissional?.nome ?? 'Qualquer' },
    { title: 'Especialidade', render: (_, i) => i.especialidade?.nome ?? 'Qualquer' },
    { title: 'Unidade', render: (_, i) => i.unidade?.nome ?? 'Qualquer' },
    { title: 'Observação', dataIndex: 'observacao', render: (v?: string | null) => v ?? '—' },
    {
      title: 'Situação',
      width: 150,
      render: (_, i) => (
        <span>
          <Tag color={STATUS[i.status].cor}>{STATUS[i.status].label}</Tag>
          {i.status === 'NOTIFICADO' && i.notificadoEm && <div style={{ fontSize: 12 }}>{formatarDataHora(i.notificadoEm)}</div>}
        </span>
      )
    },
    {
      title: 'Ações',
      width: 170,
      render: (_, i) => {
        if (i.status === 'ATENDIDO' || i.status === 'CANCELADO' || (!podeEditar && !podeCancelar)) return null;
        return (
          <Space>
            {podeEditar && podeAgendar && <Button size="small" type="primary" icon={<CalendarOutlined />} title="Agendar consulta" onClick={() => setParaAgendar(i)} />}
            {podeEditar && i.status === 'NOTIFICADO' && (
              <Button size="small" icon={<UndoOutlined />} title="Voltar para a fila" onClick={() => void mudarStatus(i, 'AGUARDANDO')} />
            )}
            {podeEditar && <Button size="small" icon={<CheckOutlined />} title="Marcar como atendido" onClick={() => void mudarStatus(i, 'ATENDIDO')} />}
            {podeCancelar && <Button size="small" danger icon={<CloseOutlined />} title="Cancelar" onClick={() => confirmarCancelamento(i)} />}
          </Space>
        );
      }
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Lista de espera
        </Typography.Title>
        {podeIncluir && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setFormAberto(true)}>
            Incluir na lista de espera
          </Button>
        )}
      </Space>

      {avisados > 0 && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          message={`${avisados} paciente(s) com horário liberado. Agende pelo botão de calendário ou devolva à fila.`}
        />
      )}

      <Space style={{ marginBottom: 16 }} wrap>
        <Input.Search
          allowClear
          placeholder="Buscar por nome do paciente"
          style={{ width: 260 }}
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
          enterButton={<SearchOutlined />}
        />
        <Select<StatusListaEspera>
          allowClear
          placeholder="Situação"
          style={{ width: 190 }}
          value={status}
          onChange={filtrar(setStatus)}
          options={(Object.keys(STATUS) as StatusListaEspera[]).map((s) => ({ value: s, label: STATUS[s].label }))}
        />
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder="Profissional"
          style={{ width: 220 }}
          value={profissionalId}
          onChange={filtrar(setProfissionalId)}
          options={profissionais.map((p) => ({ value: p.id, label: p.nome }))}
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
      </Space>

      <Table<ListaEspera>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Nenhum paciente na lista de espera.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} item(ns)`,
          onChange: setPagina
        }}
      />

      <FormularioEspera
        aberto={formAberto}
        profissionais={profissionais}
        unidades={unidades}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />

      <NovaConsultaModal
        aberto={paraAgendar !== null}
        profissionalId={paraAgendar?.profissional?.id}
        unidadeId={paraAgendar?.unidade?.id}
        especialidadeId={paraAgendar?.especialidade?.id}
        pacienteInicial={paraAgendar?.paciente}
        aoFechar={() => setParaAgendar(null)}
        aoSalvar={() => {
          const item = paraAgendar;
          setParaAgendar(null);
          // Paciente agendado: sai da fila.
          if (item) void mudarStatus(item, 'ATENDIDO');
          else void carregar();
        }}
      />
    </>
  );
}
