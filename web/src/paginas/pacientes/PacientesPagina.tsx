import { useCallback, useEffect, useState } from 'react';
import { App, Button, Form, Input, Modal, Select, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { EditOutlined, PlusOutlined, PoweroffOutlined, SearchOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio, Pagina, Paciente, PacienteResumo } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import { cpfValido, formatarCpf, formatarDataIso, formatarTelefone, somenteDigitos } from '../../util/formatos';
import FormularioPaciente from './FormularioPaciente';

const TAMANHO = 10;

type FiltroSituacao = 'todos' | 'ativos' | 'inativos';
type FiltroIdade = 'todos' | 'menores' | 'adultos';

/** E1: a busca por CPF mostra só o CPF mascarado e leva ao cadastro ou ao novo cadastro com o CPF preenchido. */
function BuscaCpfModal({
  aberto,
  podeIncluir,
  aoFechar,
  aoAbrir,
  aoCadastrar
}: {
  aberto: boolean;
  podeIncluir: boolean;
  aoFechar: () => void;
  aoAbrir: (id: string) => void;
  aoCadastrar: (cpf: string) => void;
}) {
  const [form] = Form.useForm<{ cpf: string }>();
  const [buscando, setBuscando] = useState(false);
  const [encontrado, setEncontrado] = useState<PacienteResumo | null>(null);
  const [naoCadastrado, setNaoCadastrado] = useState<string | null>(null);

  async function buscar() {
    let v: { cpf: string };
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    const cpf = somenteDigitos(v.cpf);
    setBuscando(true);
    setEncontrado(null);
    setNaoCadastrado(null);
    try {
      setEncontrado(await api.post<PacienteResumo>('/pacientes/busca-por-cpf', { cpf }));
    } catch (e) {
      if (e instanceof ApiErro && e.codigo === 'PACIENTE_NAO_CADASTRADO') setNaoCadastrado(cpf);
      else if (e instanceof ApiErro && e.erros.length > 0) form.setFields([{ name: 'cpf', errors: [e.erros[0].mensagem] }]);
      else form.setFields([{ name: 'cpf', errors: [e instanceof ApiErro ? e.message : 'Erro inesperado.'] }]);
    } finally {
      setBuscando(false);
    }
  }

  return (
    <Modal open={aberto} title="Buscar paciente por CPF" footer={null} onCancel={aoFechar} destroyOnClose>
      <Form form={form} layout="vertical" preserve={false}>
        <Form.Item
          name="cpf"
          label="CPF"
          normalize={(v: string) => formatarCpf(v)}
          rules={[
            { required: true, message: 'Campo Não Preenchido' },
            { validator: (_, v: string) => (!v || cpfValido(v) ? Promise.resolve() : Promise.reject(new Error('CPF inválido.'))) }
          ]}
        >
          <Input placeholder="000.000.000-00" inputMode="numeric" autoFocus onPressEnter={() => void buscar()} />
        </Form.Item>
        <Button type="primary" icon={<SearchOutlined />} loading={buscando} onClick={() => void buscar()}>
          Buscar
        </Button>
      </Form>

      {encontrado && (
        <div style={{ marginTop: 16 }}>
          <Typography.Paragraph>
            <strong>{encontrado.nome}</strong>
            <br />
            CPF {encontrado.cpfMascarado} · nascimento {formatarDataIso(encontrado.dataNascimento)}
            <br />
            Responsável: {encontrado.profissionalResponsavel.nome}
          </Typography.Paragraph>
          <Button onClick={() => aoAbrir(encontrado.id)}>Abrir cadastro</Button>
        </div>
      )}
      {naoCadastrado && (
        <div style={{ marginTop: 16 }}>
          <Typography.Paragraph>Não há paciente cadastrado com este CPF.</Typography.Paragraph>
          {podeIncluir && (
            <Button type="primary" onClick={() => aoCadastrar(naoCadastrado)}>
              Cadastrar novo paciente
            </Button>
          )}
        </div>
      )}
    </Modal>
  );
}

export default function PacientesPagina() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('paciente.cadastro', 'incluir');
  const podeEditar = pode('paciente.cadastro', 'editar');

  const [dados, setDados] = useState<Pagina<PacienteResumo>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [situacao, setSituacao] = useState<FiltroSituacao>('todos');
  const [idade, setIdade] = useState<FiltroIdade>('todos');
  const [profissionalId, setProfissionalId] = useState<number | undefined>();
  const [convenioId, setConvenioId] = useState<number | undefined>();
  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);
  const [convenios, setConvenios] = useState<ItemApoio[]>([]);

  const [formAberto, setFormAberto] = useState(false);
  const [emEdicao, setEmEdicao] = useState<Paciente | null>(null);
  const [cpfInicial, setCpfInicial] = useState<string | undefined>();
  const [buscaCpfAberta, setBuscaCpfAberta] = useState(false);

  // Listas dos filtros (incluem inativos para conseguir filtrar cadastros antigos).
  useEffect(() => {
    Promise.all([
      api.get<ItemApoio[]>('/apoio/profissionais', { incluirInativos: true }),
      api.get<ItemApoio[]>('/apoio/convenios', { incluirInativos: true })
    ])
      .then(([p, c]) => {
        setProfissionais(p);
        setConvenios(c);
      })
      .catch(() => undefined); // sem a liberação de listas de apoio, os filtros ficam vazios
  }, []);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<PacienteResumo>>('/pacientes', {
          busca: buscaAplicada,
          profissionalId,
          convenioId,
          ativo: situacao === 'todos' ? undefined : situacao === 'ativos',
          menorDeIdade: idade === 'todos' ? undefined : idade === 'menores',
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os pacientes.');
    } finally {
      setCarregando(false);
    }
  }, [buscaAplicada, profissionalId, convenioId, situacao, idade, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  // A lista traz só o resumo; para editar, busca o cadastro completo (a consulta fica registrada no log).
  async function abrirEdicao(id: string) {
    try {
      setEmEdicao(await api.get<Paciente>(`/pacientes/${id}`));
      setCpfInicial(undefined);
      setFormAberto(true);
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível abrir o cadastro.');
    }
  }

  function novoPaciente(cpf?: string) {
    setEmEdicao(null);
    setCpfInicial(cpf);
    setFormAberto(true);
  }

  function alterarStatus(r: PacienteResumo) {
    const inativar = r.ativo;
    modal.confirm({
      title: inativar ? 'Inativar paciente' : 'Ativar paciente',
      content: `${inativar ? 'Inativar' : 'Ativar'} o paciente "${r.nome}"?`,
      okText: inativar ? 'Inativar' : 'Ativar',
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.patch(`/pacientes/${r.id}/status`, { ativo: !r.ativo });
          message.success(inativar ? 'Paciente inativado.' : 'Paciente ativado.');
          await carregar();
        } catch (e) {
          if (e instanceof ApiErro && e.codigo === 'PACIENTE_COM_CONSULTAS') message.warning(e.message);
          else message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
        }
      }
    });
  }

  const colunas: TableColumnsType<PacienteResumo> = [
    {
      title: 'Nome',
      dataIndex: 'nome',
      render: (nome: string, r) => (
        <>
          {nome} {r.menorDeIdade && <Tag color="gold">Menor</Tag>}
        </>
      )
    },
    { title: 'CPF', dataIndex: 'cpfMascarado', width: 150, render: (c?: string | null) => c ?? '—' },
    { title: 'Nascimento', dataIndex: 'dataNascimento', width: 120, render: (d: string) => formatarDataIso(d) },
    { title: 'Celular', dataIndex: 'telefone', width: 140, render: (t?: string | null) => formatarTelefone(t) || '—' },
    { title: 'Convênio', width: 150, render: (_, r) => r.convenio?.nomeFantasia ?? 'Particular' },
    { title: 'Profissional responsável', width: 200, render: (_, r) => r.profissionalResponsavel.nome },
    {
      title: 'Situação',
      dataIndex: 'ativo',
      width: 100,
      render: (ativo: boolean) => (ativo ? <Tag color="green">Ativo</Tag> : <Tag>Inativo</Tag>)
    },
    {
      title: 'Ações',
      width: 100,
      render: (_, r) =>
        podeEditar && (
          <Space>
            <Button size="small" icon={<EditOutlined />} title="Editar" onClick={() => void abrirEdicao(r.id)} />
            <Button
              size="small"
              danger={r.ativo}
              icon={<PoweroffOutlined />}
              title={r.ativo ? 'Inativar' : 'Ativar'}
              onClick={() => alterarStatus(r)}
            />
          </Space>
        )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Pacientes
        </Typography.Title>
        <Space>
          <Button icon={<SearchOutlined />} onClick={() => setBuscaCpfAberta(true)}>
            Buscar por CPF
          </Button>
          {podeIncluir && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => novoPaciente()}>
              Novo paciente
            </Button>
          )}
        </Space>
      </Space>

      <Space style={{ marginBottom: 16 }} wrap>
        <Input.Search
          placeholder="Buscar por nome ou celular"
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
        <Select<number>
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder="Profissional"
          style={{ width: 220 }}
          value={profissionalId}
          options={profissionais.map((p) => ({ value: p.id, label: p.nome }))}
          onChange={(v) => {
            setProfissionalId(v);
            setPagina(1);
          }}
        />
        <Select<number>
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder="Convênio"
          style={{ width: 200 }}
          value={convenioId}
          options={convenios.map((c) => ({ value: c.id, label: c.nome }))}
          onChange={(v) => {
            setConvenioId(v);
            setPagina(1);
          }}
        />
        <Select<FiltroIdade>
          value={idade}
          style={{ width: 150 }}
          onChange={(v) => {
            setIdade(v);
            setPagina(1);
          }}
          options={[
            { value: 'todos', label: 'Todas as idades' },
            { value: 'menores', label: 'Menores de idade' },
            { value: 'adultos', label: 'Adultos' }
          ]}
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

      <Table<PacienteResumo>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: 1000 }}
        locale={{ emptyText: 'Nenhum paciente encontrado.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} paciente(s)`,
          onChange: setPagina
        }}
      />

      <BuscaCpfModal
        aberto={buscaCpfAberta}
        podeIncluir={podeIncluir}
        aoFechar={() => setBuscaCpfAberta(false)}
        aoAbrir={(id) => {
          setBuscaCpfAberta(false);
          void abrirEdicao(id);
        }}
        aoCadastrar={(cpf) => {
          setBuscaCpfAberta(false);
          novoPaciente(cpf);
        }}
      />

      <FormularioPaciente
        aberto={formAberto}
        paciente={emEdicao}
        cpfInicial={cpfInicial}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />
    </>
  );
}
