import { useCallback, useEffect, useState } from 'react';
import { App, Button, Form, Input, Modal, Select, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { EditOutlined, PlusOutlined, PoweroffOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { Especialidade, Pagina } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';

const OBRIGATORIO = 'Campo Não Preenchido';
const TAMANHO = 10;

type FiltroSituacao = 'todos' | 'ativos' | 'inativos';

interface ValoresFormulario {
  nome: string;
}

interface FormularioProps {
  aberto: boolean;
  especialidade: Especialidade | null;
  aoFechar: () => void;
  aoSalvar: () => void;
}

function FormularioEspecialidade({ aberto, especialidade, aoFechar, aoSalvar }: FormularioProps) {
  const { message } = App.useApp();
  const [form] = Form.useForm<ValoresFormulario>();
  const [salvando, setSalvando] = useState(false);

  async function enviar() {
    let valores: ValoresFormulario;
    try {
      valores = await form.validateFields();
    } catch {
      return;
    }
    const corpo = { nome: valores.nome.trim() };
    setSalvando(true);
    try {
      if (especialidade) await api.put<Especialidade>(`/especialidades/${especialidade.id}`, corpo);
      else await api.post<Especialidade>('/especialidades', corpo);
      message.success(especialidade ? 'Especialidade atualizada.' : 'Especialidade cadastrada.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(e.erros.map((x) => ({ name: x.campo as keyof ValoresFormulario, errors: [x.mensagem] })));
      } else if (e instanceof ApiErro && e.codigo === 'ESPECIALIDADE_DUPLICADA') {
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
      title={especialidade ? 'Editar especialidade' : 'Nova especialidade'}
      okText="Salvar"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
    >
      <Form<ValoresFormulario>
        form={form}
        layout="vertical"
        requiredMark
        preserve={false}
        initialValues={especialidade ? { nome: especialidade.nome } : undefined}
      >
        <Form.Item name="nome" label="Nome" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
          <Input maxLength={80} autoFocus onPressEnter={() => void enviar()} />
        </Form.Item>
      </Form>
    </Modal>
  );
}

export default function EspecialidadesPagina() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('especialidade.cadastro', 'incluir');
  const podeEditar = pode('especialidade.cadastro', 'editar');

  const [dados, setDados] = useState<Pagina<Especialidade>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [situacao, setSituacao] = useState<FiltroSituacao>('todos');
  const [formAberto, setFormAberto] = useState(false);
  const [emEdicao, setEmEdicao] = useState<Especialidade | null>(null);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<Especialidade>>('/especialidades', {
          busca: buscaAplicada,
          ativo: situacao === 'todos' ? undefined : situacao === 'ativos',
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar as especialidades.');
    } finally {
      setCarregando(false);
    }
  }, [buscaAplicada, situacao, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  function alterarStatus(e: Especialidade) {
    const inativar = e.ativo;
    modal.confirm({
      title: inativar ? 'Inativar especialidade' : 'Ativar especialidade',
      content: `${inativar ? 'Inativar' : 'Ativar'} a especialidade "${e.nome}"?`,
      okText: inativar ? 'Inativar' : 'Ativar',
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.patch(`/especialidades/${e.id}/status`, { ativo: !e.ativo });
          message.success(inativar ? 'Especialidade inativada.' : 'Especialidade ativada.');
          await carregar();
        } catch (erro) {
          if (erro instanceof ApiErro && erro.codigo === 'ESPECIALIDADE_COM_CONSULTAS') message.warning(erro.message);
          else message.error(erro instanceof ApiErro ? erro.message : 'Erro inesperado.');
        }
      }
    });
  }

  const colunas: TableColumnsType<Especialidade> = [
    { title: 'Nome', dataIndex: 'nome' },
    {
      title: 'Situação',
      dataIndex: 'ativo',
      width: 110,
      render: (ativo: boolean) => (ativo ? <Tag color="green">Ativa</Tag> : <Tag>Inativa</Tag>)
    },
    {
      title: 'Ações',
      width: 130,
      render: (_, e) =>
        podeEditar && (
          <Space>
            <Button
              size="small"
              icon={<EditOutlined />}
              title="Editar"
              onClick={() => {
                setEmEdicao(e);
                setFormAberto(true);
              }}
            />
            <Button
              size="small"
              danger={e.ativo}
              icon={<PoweroffOutlined />}
              title={e.ativo ? 'Inativar' : 'Ativar'}
              onClick={() => alterarStatus(e)}
            />
          </Space>
        )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Especialidades
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
            Nova especialidade
          </Button>
        )}
      </Space>

      <Space style={{ marginBottom: 16 }} wrap>
        <Input.Search
          placeholder="Buscar por nome"
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

      <Table<Especialidade>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        locale={{ emptyText: 'Nenhuma especialidade encontrada.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} especialidade(s)`,
          onChange: setPagina
        }}
      />

      <FormularioEspecialidade
        aberto={formAberto}
        especialidade={emEdicao}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />
    </>
  );
}
