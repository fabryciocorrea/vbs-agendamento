import { useCallback, useEffect, useState } from 'react';
import { App, Button, Col, Form, Input, Modal, Row, Select, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { EditOutlined, PlusOutlined, PoweroffOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { Convenio, Pagina } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import { cnpjValido, formatarCnpj, formatarDataIso, hojeIso, somenteDigitos } from '../../util/formatos';

const OBRIGATORIO = 'Campo Não Preenchido';
const TAMANHO = 10;

type FiltroSituacao = 'todos' | 'ativos' | 'inativos';

interface ValoresFormulario {
  razaoSocial: string;
  nomeFantasia?: string;
  cnpj: string;
  codigoAns: string;
  dataInscricao: string;
  inscricaoMunicipal?: string;
  inscricaoEstadual?: string;
}

interface FormularioProps {
  aberto: boolean;
  convenio: Convenio | null;
  aoFechar: () => void;
  aoSalvar: () => void;
}

function FormularioConvenio({ aberto, convenio, aoFechar, aoSalvar }: FormularioProps) {
  const { message } = App.useApp();
  const [form] = Form.useForm<ValoresFormulario>();
  const [salvando, setSalvando] = useState(false);

  async function enviar() {
    let v: ValoresFormulario;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    // Campos opcionais em branco viram null para o servidor.
    const opcional = (t?: string) => (t && t.trim() !== '' ? t.trim() : null);
    const corpo = {
      razaoSocial: v.razaoSocial.trim(),
      nomeFantasia: opcional(v.nomeFantasia),
      cnpj: somenteDigitos(v.cnpj),
      codigoAns: v.codigoAns.trim(),
      dataInscricao: v.dataInscricao,
      inscricaoMunicipal: opcional(v.inscricaoMunicipal),
      inscricaoEstadual: opcional(v.inscricaoEstadual)
    };
    setSalvando(true);
    try {
      if (convenio) await api.put<Convenio>(`/convenios/${convenio.id}`, corpo);
      else await api.post<Convenio>('/convenios', corpo);
      message.success(convenio ? 'Convênio atualizado.' : 'Convênio cadastrado.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(e.erros.map((x) => ({ name: x.campo as keyof ValoresFormulario, errors: [x.mensagem] })));
      } else if (e instanceof ApiErro && e.codigo === 'CNPJ_DUPLICADO') {
        form.setFields([{ name: 'cnpj', errors: [e.message] }]);
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
      title={convenio ? 'Editar convênio' : 'Novo convênio'}
      okText="Salvar"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
      width={680}
    >
      <Form<ValoresFormulario>
        form={form}
        layout="vertical"
        requiredMark
        preserve={false}
        initialValues={
          convenio
            ? {
                razaoSocial: convenio.razaoSocial,
                nomeFantasia: convenio.nomeFantasia ?? '',
                cnpj: formatarCnpj(convenio.cnpj),
                codigoAns: convenio.codigoAns,
                dataInscricao: convenio.dataInscricao.slice(0, 10),
                inscricaoMunicipal: convenio.inscricaoMunicipal ?? '',
                inscricaoEstadual: convenio.inscricaoEstadual ?? ''
              }
            : undefined
        }
      >
        <Form.Item name="razaoSocial" label="Razão social" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
          <Input maxLength={150} />
        </Form.Item>
        <Form.Item name="nomeFantasia" label="Nome fantasia">
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
            <Form.Item name="codigoAns" label="Código ANS" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={10} />
            </Form.Item>
          </Col>
        </Row>
        <Row gutter={16}>
          <Col xs={24} sm={8}>
            <Form.Item name="dataInscricao" label="Data de inscrição" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Input type="date" max={hojeIso()} />
            </Form.Item>
          </Col>
          <Col xs={24} sm={8}>
            <Form.Item name="inscricaoMunicipal" label="Inscrição municipal">
              <Input maxLength={30} />
            </Form.Item>
          </Col>
          <Col xs={24} sm={8}>
            <Form.Item name="inscricaoEstadual" label="Inscrição estadual">
              <Input maxLength={30} />
            </Form.Item>
          </Col>
        </Row>
      </Form>
    </Modal>
  );
}

export default function ConveniosPagina() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('convenio.cadastro', 'incluir');
  const podeEditar = pode('convenio.cadastro', 'editar');

  const [dados, setDados] = useState<Pagina<Convenio>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [situacao, setSituacao] = useState<FiltroSituacao>('todos');
  const [formAberto, setFormAberto] = useState(false);
  const [emEdicao, setEmEdicao] = useState<Convenio | null>(null);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<Convenio>>('/convenios', {
          busca: buscaAplicada,
          ativo: situacao === 'todos' ? undefined : situacao === 'ativos',
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os convênios.');
    } finally {
      setCarregando(false);
    }
  }, [buscaAplicada, situacao, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  function alterarStatus(c: Convenio) {
    const inativar = c.ativo;
    modal.confirm({
      title: inativar ? 'Inativar convênio' : 'Ativar convênio',
      content: `${inativar ? 'Inativar' : 'Ativar'} o convênio "${c.nomeFantasia || c.razaoSocial}"? A forma de pagamento ligada a ele também será ${inativar ? 'inativada' : 'ativada'}.`,
      okText: inativar ? 'Inativar' : 'Ativar',
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.patch(`/convenios/${c.id}/status`, { ativo: !c.ativo });
          message.success(inativar ? 'Convênio inativado.' : 'Convênio ativado.');
          await carregar();
        } catch (e) {
          if (e instanceof ApiErro && e.codigo === 'CONVENIO_COM_CONSULTAS') message.warning(e.message);
          else message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
        }
      }
    });
  }

  const colunas: TableColumnsType<Convenio> = [
    { title: 'Nome', render: (_, c) => c.nomeFantasia || c.razaoSocial },
    { title: 'Razão social', dataIndex: 'razaoSocial' },
    { title: 'CNPJ', dataIndex: 'cnpj', width: 170, render: (v: string) => formatarCnpj(v) },
    { title: 'Código ANS', dataIndex: 'codigoAns', width: 120 },
    { title: 'Inscrição', dataIndex: 'dataInscricao', width: 110, render: (v: string) => formatarDataIso(v) },
    {
      title: 'Situação',
      dataIndex: 'ativo',
      width: 110,
      render: (ativo: boolean) => (ativo ? <Tag color="green">Ativo</Tag> : <Tag>Inativo</Tag>)
    },
    {
      title: 'Ações',
      width: 110,
      render: (_, c) =>
        podeEditar && (
          <Space>
            <Button
              size="small"
              icon={<EditOutlined />}
              title="Editar"
              onClick={() => {
                setEmEdicao(c);
                setFormAberto(true);
              }}
            />
            <Button
              size="small"
              danger={c.ativo}
              icon={<PoweroffOutlined />}
              title={c.ativo ? 'Inativar' : 'Ativar'}
              onClick={() => alterarStatus(c)}
            />
          </Space>
        )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Convênios
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
            Novo convênio
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
            { value: 'todos', label: 'Todos' },
            { value: 'ativos', label: 'Somente ativos' },
            { value: 'inativos', label: 'Somente inativos' }
          ]}
        />
      </Space>

      <Table<Convenio>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Nenhum convênio encontrado.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} convênio(s)`,
          onChange: setPagina
        }}
      />

      <FormularioConvenio
        aberto={formAberto}
        convenio={emEdicao}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />
    </>
  );
}
