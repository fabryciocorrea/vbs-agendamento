import { useCallback, useEffect, useState } from 'react';
import { App, Button, Form, Input, Modal, Select, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { EditOutlined, PlusOutlined, PoweroffOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio, Pagina, Sala, SalaEntrada } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import BotaoExcluir from '../../componentes/BotaoExcluir';

const OBRIGATORIO = 'Campo Não Preenchido';
const TAMANHO = 10;

type FiltroSituacao = 'todos' | 'ativos' | 'inativos';

interface ValoresFormulario {
  unidadeId: number;
  numero: string;
  andar: string;
}

interface FormularioProps {
  aberto: boolean;
  sala: Sala | null;
  unidades: ItemApoio[];
  aoFechar: () => void;
  aoSalvar: () => void;
}

function FormularioSala({ aberto, sala, unidades, aoFechar, aoSalvar }: FormularioProps) {
  const { message } = App.useApp();
  const [form] = Form.useForm<ValoresFormulario>();
  const [salvando, setSalvando] = useState(false);

  // Novas salas só em unidades ativas; na edição a unidade da sala aparece mesmo se estiver inativa.
  const opcoes = unidades
    .filter((u) => u.ativo || (sala && u.id === sala.unidade.id))
    .map((u) => ({ value: u.id, label: u.nome }));
  if (sala && !opcoes.some((o) => o.value === sala.unidade.id)) {
    opcoes.push({ value: sala.unidade.id, label: sala.unidade.nome });
  }

  async function enviar() {
    let valores: ValoresFormulario;
    try {
      valores = await form.validateFields();
    } catch {
      return;
    }
    const corpo: SalaEntrada = {
      unidadeId: sala ? sala.unidade.id : valores.unidadeId,
      numero: valores.numero.trim(),
      andar: valores.andar.trim()
    };
    setSalvando(true);
    try {
      if (sala) await api.put<Sala>(`/salas/${sala.id}`, corpo);
      else await api.post<Sala>('/salas', corpo);
      message.success(sala ? 'Sala atualizada.' : 'Sala cadastrada.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(e.erros.map((x) => ({ name: x.campo as keyof ValoresFormulario, errors: [x.mensagem] })));
      } else if (e instanceof ApiErro && e.codigo === 'NUMERO_SALA_DUPLICADO') {
        form.setFields([{ name: 'numero', errors: [e.message] }]);
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
      title={sala ? 'Editar sala' : 'Nova sala'}
      okText="Salvar"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
      width={480}
    >
      <Form<ValoresFormulario>
        form={form}
        layout="vertical"
        requiredMark
        preserve={false}
        initialValues={sala ? { unidadeId: sala.unidade.id, numero: sala.numero, andar: sala.andar } : undefined}
      >
        <Form.Item
          name="unidadeId"
          label="Unidade"
          rules={[{ required: true, message: OBRIGATORIO }]}
          extra={sala ? 'A unidade de uma sala não pode ser alterada. Para outra unidade, cadastre uma nova sala.' : undefined}
        >
          <Select showSearch optionFilterProp="label" options={opcoes} disabled={!!sala} placeholder="Selecione a unidade" />
        </Form.Item>
        <Form.Item name="numero" label="Identificação da sala" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
          <Input maxLength={40} placeholder="Ex.: Sala 312 ou Consultório 01" />
        </Form.Item>
        <Form.Item name="andar" label="Andar" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
          <Input maxLength={10} />
        </Form.Item>
      </Form>
    </Modal>
  );
}

export default function SalasPagina() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('sala.cadastro', 'incluir');
  const podeEditar = pode('sala.cadastro', 'editar');

  const [dados, setDados] = useState<Pagina<Sala>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [unidadeFiltro, setUnidadeFiltro] = useState<number | undefined>(undefined);
  const [situacao, setSituacao] = useState<FiltroSituacao>('todos');
  const [formAberto, setFormAberto] = useState(false);
  const [emEdicao, setEmEdicao] = useState<Sala | null>(null);

  useEffect(() => {
    api
      .get<ItemApoio[]>('/apoio/unidades')
      .then(setUnidades)
      .catch(() => setUnidades([]));
  }, []);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<Sala>>('/salas', {
          busca: buscaAplicada,
          unidadeId: unidadeFiltro,
          ativo: situacao === 'todos' ? undefined : situacao === 'ativos',
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar as salas.');
    } finally {
      setCarregando(false);
    }
  }, [buscaAplicada, unidadeFiltro, situacao, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  function alterarStatus(s: Sala) {
    const inativar = s.ativo;
    modal.confirm({
      title: inativar ? 'Inativar sala' : 'Ativar sala',
      content: `${inativar ? 'Inativar' : 'Ativar'} a sala "${s.numero}" da unidade "${s.unidade.nome}"?`,
      okText: inativar ? 'Inativar' : 'Ativar',
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.patch(`/salas/${s.id}/status`, { ativo: !s.ativo });
          message.success(inativar ? 'Sala inativada.' : 'Sala ativada.');
          await carregar();
        } catch (e) {
          if (e instanceof ApiErro && e.codigo === 'SALA_COM_CONSULTAS') {
            const qtd = e.extras['quantidadeConsultas'];
            message.warning(`${e.message}${typeof qtd === 'number' ? ` (consultas futuras: ${qtd})` : ''}`);
          } else {
            message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
          }
        }
      }
    });
  }

  const colunas: TableColumnsType<Sala> = [
    { title: 'Unidade', render: (_, s) => s.unidade.nome },
    { title: 'Andar', dataIndex: 'andar', width: 110 },
    { title: 'Identificação', dataIndex: 'numero' },
    {
      title: 'Situação',
      dataIndex: 'ativo',
      width: 110,
      render: (ativo: boolean) => (ativo ? <Tag color="green">Ativa</Tag> : <Tag>Inativa</Tag>)
    },
    {
      title: 'Ações',
      width: 170,
      render: (_, s) => (
        <Space>
          {podeEditar && (
            <Button
              size="small"
              icon={<EditOutlined />}
              title="Editar"
              onClick={() => {
                setEmEdicao(s);
                setFormAberto(true);
              }}
            />
          )}
          {podeEditar && (
            <Button
              size="small"
              danger={s.ativo}
              icon={<PoweroffOutlined />}
              title={s.ativo ? 'Inativar' : 'Ativar'}
              onClick={() => alterarStatus(s)}
            />
          )}
          <BotaoExcluir artigoTipo="a sala" nome={`Sala ${s.numero} (${s.unidade.nome})`} caminho={`/salas/${s.id}`} aoExcluir={carregar} />
        </Space>
      )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Salas
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
            Nova sala
          </Button>
        )}
      </Space>

      <Space style={{ marginBottom: 16 }} wrap>
        <Input.Search
          placeholder="Buscar por identificação, andar ou unidade"
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
        <Select<number>
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder="Todas as unidades"
          style={{ width: 240 }}
          value={unidadeFiltro}
          onChange={(v) => {
            setUnidadeFiltro(v);
            setPagina(1);
          }}
          options={unidades.map((u) => ({ value: u.id, label: u.nome }))}
        />
        <Select<FiltroSituacao>
          value={situacao}
          style={{ width: 160 }}
          onChange={(v) => {
            setSituacao(v);
            setPagina(1);
          }}
          options={[
            { value: 'todos', label: 'Todas' },
            { value: 'ativos', label: 'Somente ativas' },
            { value: 'inativos', label: 'Somente inativas' }
          ]}
        />
      </Space>

      <Table<Sala>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Nenhuma sala encontrada.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} sala(s)`,
          onChange: setPagina
        }}
      />

      <FormularioSala
        aberto={formAberto}
        sala={emEdicao}
        unidades={unidades}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />
    </>
  );
}
