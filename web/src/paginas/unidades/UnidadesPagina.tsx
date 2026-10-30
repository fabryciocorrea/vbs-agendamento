import { useCallback, useEffect, useState } from 'react';
import { App, Button, Col, Form, Input, Modal, Row, Select, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { EditOutlined, PlusOutlined, PoweroffOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { Pagina, Unidade, UnidadeEntrada } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import BotaoExcluir from '../../componentes/BotaoExcluir';
import CampoNumeroEndereco from '../../componentes/CampoNumeroEndereco';
import { cnpjValido, formatarCep, formatarCnpj, somenteDigitos, UFS } from '../../util/formatos';

const OBRIGATORIO = 'Campo Não Preenchido';
const TAMANHO = 10;

type FiltroSituacao = 'todos' | 'ativos' | 'inativos';

interface ValoresFormulario {
  razaoSocial: string;
  nomeFantasia: string;
  cnpj: string;
  cep: string;
  endereco: string;
  numero: string;
  complemento?: string;
  cidade: string;
  uf: string;
}

/** Aplica no formulário os erros devolvidos pelo servidor. Devolve true se tratou. */
function aplicarErrosDoServidor(e: unknown, form: ReturnType<typeof Form.useForm<ValoresFormulario>>[0]): boolean {
  if (!(e instanceof ApiErro)) return false;
  if (e.erros.length > 0) {
    form.setFields(e.erros.map((x) => ({ name: x.campo as keyof ValoresFormulario, errors: [x.mensagem] })));
    return true;
  }
  if (e.codigo === 'CNPJ_DUPLICADO') {
    form.setFields([{ name: 'cnpj', errors: [e.message] }]);
    return true;
  }
  return false;
}

interface FormularioProps {
  aberto: boolean;
  unidade: Unidade | null;
  aoFechar: () => void;
  aoSalvar: () => void;
}

function FormularioUnidade({ aberto, unidade, aoFechar, aoSalvar }: FormularioProps) {
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
    const corpo: UnidadeEntrada = {
      razaoSocial: valores.razaoSocial.trim(),
      nomeFantasia: valores.nomeFantasia.trim(),
      cnpj: somenteDigitos(valores.cnpj),
      cep: somenteDigitos(valores.cep),
      endereco: valores.endereco.trim(),
      numero: valores.numero.trim(),
      complemento: valores.complemento && valores.complemento.trim() !== '' ? valores.complemento.trim() : null,
      cidade: valores.cidade.trim(),
      uf: valores.uf
    };
    setSalvando(true);
    try {
      if (unidade) await api.put<Unidade>(`/unidades/${unidade.id}`, corpo);
      else await api.post<Unidade>('/unidades', corpo);
      message.success(unidade ? 'Unidade atualizada.' : 'Unidade cadastrada.');
      aoSalvar();
    } catch (e) {
      if (!aplicarErrosDoServidor(e, form)) message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Modal
      open={aberto}
      title={unidade ? 'Editar unidade' : 'Nova unidade'}
      okText="Salvar"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
      width={640}
    >
      <Form<ValoresFormulario>
        form={form}
        layout="vertical"
        requiredMark
        preserve={false}
        initialValues={
          unidade
            ? { ...unidade, cnpj: formatarCnpj(unidade.cnpj), cep: formatarCep(unidade.cep) }
            : undefined
        }
      >
        <Form.Item name="razaoSocial" label="Razão social" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
          <Input maxLength={150} />
        </Form.Item>
        <Form.Item name="nomeFantasia" label="Nome fantasia" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
          <Input maxLength={150} />
        </Form.Item>
        <Row gutter={16}>
          <Col xs={24} sm={12}>
            <Form.Item
              name="cnpj"
              label="CNPJ"
              normalize={(v: string) => formatarCnpj(v)}
              rules={[
                { required: true, message: OBRIGATORIO },
                { validator: (_, v: string) => (!v || cnpjValido(v) ? Promise.resolve() : Promise.reject(new Error('CNPJ inválido.'))) }
              ]}
            >
              <Input placeholder="00.000.000/0000-00" inputMode="numeric" />
            </Form.Item>
          </Col>
          <Col xs={24} sm={12}>
            <Form.Item
              name="cep"
              label="CEP"
              normalize={(v: string) => formatarCep(v)}
              rules={[
                { required: true, message: OBRIGATORIO },
                { validator: (_, v: string) => (!v || somenteDigitos(v).length === 8 ? Promise.resolve() : Promise.reject(new Error('CEP inválido.'))) }
              ]}
            >
              <Input placeholder="00000-000" inputMode="numeric" />
            </Form.Item>
          </Col>
        </Row>
        <Row gutter={16}>
          <Col xs={24} sm={12}>
            <Form.Item name="endereco" label="Endereço" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={250} />
            </Form.Item>
          </Col>
          <Col xs={24} sm={6}>
            <Form.Item name="numero" label="Número" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <CampoNumeroEndereco />
            </Form.Item>
          </Col>
          <Col xs={24} sm={6}>
            <Form.Item name="complemento" label="Complemento">
              <Input maxLength={60} />
            </Form.Item>
          </Col>
        </Row>
        <Row gutter={16}>
          <Col xs={24} sm={16}>
            <Form.Item name="cidade" label="Cidade" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={100} />
            </Form.Item>
          </Col>
          <Col xs={24} sm={8}>
            <Form.Item name="uf" label="UF" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Select showSearch options={UFS.map((u) => ({ value: u, label: u }))} />
            </Form.Item>
          </Col>
        </Row>
      </Form>
    </Modal>
  );
}

export default function UnidadesPagina() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('unidade.cadastro', 'incluir');
  const podeEditar = pode('unidade.cadastro', 'editar');

  const [dados, setDados] = useState<Pagina<Unidade>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [situacao, setSituacao] = useState<FiltroSituacao>('todos');
  const [formAberto, setFormAberto] = useState(false);
  const [emEdicao, setEmEdicao] = useState<Unidade | null>(null);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<Unidade>>('/unidades', {
          busca: buscaAplicada,
          ativo: situacao === 'todos' ? undefined : situacao === 'ativos',
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar as unidades.');
    } finally {
      setCarregando(false);
    }
  }, [buscaAplicada, situacao, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  function alterarStatus(u: Unidade) {
    const inativar = u.ativo;
    modal.confirm({
      title: inativar ? 'Inativar unidade' : 'Ativar unidade',
      content: `${inativar ? 'Inativar' : 'Ativar'} a unidade "${u.nomeFantasia}"?`,
      okText: inativar ? 'Inativar' : 'Ativar',
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.patch(`/unidades/${u.id}/status`, { ativo: !u.ativo });
          message.success(inativar ? 'Unidade inativada.' : 'Unidade ativada.');
          await carregar();
        } catch (e) {
          if (e instanceof ApiErro && e.codigo === 'UNIDADE_COM_CONSULTAS') {
            const qtd = e.extras['quantidadeConsultas'];
            message.warning(`${e.message}${typeof qtd === 'number' ? ` (consultas futuras: ${qtd})` : ''}`);
          } else {
            message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
          }
        }
      }
    });
  }

  const colunas: TableColumnsType<Unidade> = [
    { title: 'Nome fantasia', dataIndex: 'nomeFantasia' },
    { title: 'Razão social', dataIndex: 'razaoSocial' },
    { title: 'CNPJ', dataIndex: 'cnpj', render: (v: string) => formatarCnpj(v), width: 170 },
    { title: 'Cidade/UF', render: (_, u) => `${u.cidade}/${u.uf}` },
    {
      title: 'Situação',
      dataIndex: 'ativo',
      width: 110,
      render: (ativo: boolean) => (ativo ? <Tag color="green">Ativa</Tag> : <Tag>Inativa</Tag>)
    },
    {
      title: 'Ações',
      width: 170,
      render: (_, u) => (
        <Space>
          {podeEditar && (
            <Button
              size="small"
              icon={<EditOutlined />}
              title="Editar"
              onClick={() => {
                setEmEdicao(u);
                setFormAberto(true);
              }}
            />
          )}
          {podeEditar && (
            <Button
              size="small"
              danger={u.ativo}
              icon={<PoweroffOutlined />}
              title={u.ativo ? 'Inativar' : 'Ativar'}
              onClick={() => alterarStatus(u)}
            />
          )}
          <BotaoExcluir artigoTipo="a unidade" nome={u.nomeFantasia} caminho={`/unidades/${u.id}`} aviso="As salas, os credenciamentos e os vínculos de profissionais com a unidade saem junto." aoExcluir={carregar} />
        </Space>
      )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Unidades
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
            Nova unidade
          </Button>
        )}
      </Space>

      <Space style={{ marginBottom: 16 }} wrap>
        <Input.Search
          placeholder="Buscar por nome fantasia ou razão social"
          allowClear
          style={{ width: 340 }}
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

      <Table<Unidade>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Nenhuma unidade encontrada.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} unidade(s)`,
          onChange: setPagina
        }}
      />

      <FormularioUnidade
        aberto={formAberto}
        unidade={emEdicao}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />
    </>
  );
}
