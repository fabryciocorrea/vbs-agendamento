import { useCallback, useEffect, useState } from 'react';
import { App, Button, Form, Input, Modal, Select, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { EditOutlined, PlusOutlined, PoweroffOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { CondicaoFormaPagamento, Convenio, FormaPagamento, Pagina, TipoFormaPagamento } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import BotaoExcluir from '../../componentes/BotaoExcluir';

const OBRIGATORIO = 'Campo Não Preenchido';
const TAMANHO = 10;

const TIPOS: { value: TipoFormaPagamento; label: string }[] = [
  { value: 'DINHEIRO', label: 'Dinheiro' },
  { value: 'PIX', label: 'PIX' },
  { value: 'CARTAO_CREDITO', label: 'Cartão de crédito' },
  { value: 'CARTAO_DEBITO', label: 'Cartão de débito' },
  { value: 'CONVENIO', label: 'Convênio' }
];
const CONDICOES: { value: CondicaoFormaPagamento; label: string }[] = [
  { value: 'A_VISTA', label: 'À vista' },
  { value: 'A_PRAZO', label: 'A prazo' }
];
const rotulo = <T extends string>(lista: { value: T; label: string }[], v: T) => lista.find((i) => i.value === v)?.label ?? v;

type FiltroSituacao = 'todos' | 'ativos' | 'inativos';

interface ValoresFormulario {
  descricao: string;
  tipo: TipoFormaPagamento;
  condicao: CondicaoFormaPagamento;
  convenioId?: number;
}

interface FormularioProps {
  aberto: boolean;
  forma: FormaPagamento | null;
  aoFechar: () => void;
  aoSalvar: () => void;
}

function FormularioFormaPagamento({ aberto, forma, aoFechar, aoSalvar }: FormularioProps) {
  const { message } = App.useApp();
  const [form] = Form.useForm<ValoresFormulario>();
  const [salvando, setSalvando] = useState(false);
  const [convenios, setConvenios] = useState<{ value: number; label: string }[]>([]);
  const tipo = Form.useWatch('tipo', form);

  // A forma ligada a um convênio não troca de tipo nem de convênio.
  const travada = forma?.tipo === 'CONVENIO';

  useEffect(() => {
    if (!aberto) return;
    api
      .get<Pagina<Convenio>>('/convenios', { ativo: true, pagina: 1, tamanho: 100 })
      .then((r) => {
        const lista = r.itens.map((c) => ({ value: c.id, label: c.nomeFantasia || c.razaoSocial }));
        // Se o convênio da forma estiver inativo, ele não vem na lista; mantém para exibir.
        if (forma?.convenio && !lista.some((i) => i.value === forma.convenio!.id))
          lista.push({ value: forma.convenio.id, label: forma.convenio.nome });
        setConvenios(lista);
      })
      .catch(() => message.error('Não foi possível carregar os convênios.'));
  }, [aberto, forma, message]);

  async function enviar() {
    let v: ValoresFormulario;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    const corpo = {
      descricao: v.descricao.trim(),
      tipo: v.tipo,
      condicao: v.condicao,
      convenioId: v.tipo === 'CONVENIO' ? v.convenioId : null
    };
    setSalvando(true);
    try {
      if (forma) await api.put<FormaPagamento>(`/formas-pagamento/${forma.id}`, corpo);
      else await api.post<FormaPagamento>('/formas-pagamento', corpo);
      message.success(forma ? 'Forma de pagamento atualizada.' : 'Forma de pagamento cadastrada.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(e.erros.map((x) => ({ name: x.campo as keyof ValoresFormulario, errors: [x.mensagem] })));
      } else if (e instanceof ApiErro && e.codigo === 'FORMA_PAGAMENTO_DUPLICADA') {
        form.setFields([{ name: 'descricao', errors: [e.message] }]);
      } else if (e instanceof ApiErro && e.codigo === 'CONVENIO_JA_TEM_FORMA_PAGAMENTO') {
        form.setFields([{ name: 'convenioId', errors: [e.message] }]);
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
      title={forma ? 'Editar forma de pagamento' : 'Nova forma de pagamento'}
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
        initialValues={
          forma
            ? { descricao: forma.descricao, tipo: forma.tipo, condicao: forma.condicao, convenioId: forma.convenio?.id }
            : { condicao: 'A_VISTA' }
        }
      >
        <Form.Item name="descricao" label="Descrição" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
          <Input maxLength={80} />
        </Form.Item>
        <Form.Item name="tipo" label="Tipo" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select options={TIPOS} disabled={travada} />
        </Form.Item>
        <Form.Item name="condicao" label="Condição" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select options={CONDICOES} />
        </Form.Item>
        {tipo === 'CONVENIO' && (
          <Form.Item name="convenioId" label="Convênio" rules={[{ required: true, message: OBRIGATORIO }]}>
            <Select showSearch optionFilterProp="label" options={convenios} disabled={travada} placeholder="Selecione" />
          </Form.Item>
        )}
      </Form>
    </Modal>
  );
}

export default function FormasPagamentoPagina() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('formapagamento.cadastro', 'incluir');
  const podeEditar = pode('formapagamento.cadastro', 'editar');

  const [dados, setDados] = useState<Pagina<FormaPagamento>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [tipoFiltro, setTipoFiltro] = useState<TipoFormaPagamento | 'todos'>('todos');
  const [situacao, setSituacao] = useState<FiltroSituacao>('todos');
  const [formAberto, setFormAberto] = useState(false);
  const [emEdicao, setEmEdicao] = useState<FormaPagamento | null>(null);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<FormaPagamento>>('/formas-pagamento', {
          busca: buscaAplicada,
          tipo: tipoFiltro === 'todos' ? undefined : tipoFiltro,
          ativo: situacao === 'todos' ? undefined : situacao === 'ativos',
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar as formas de pagamento.');
    } finally {
      setCarregando(false);
    }
  }, [buscaAplicada, tipoFiltro, situacao, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  function alterarStatus(f: FormaPagamento) {
    const inativar = f.ativo;
    modal.confirm({
      title: inativar ? 'Inativar forma de pagamento' : 'Ativar forma de pagamento',
      content: `${inativar ? 'Inativar' : 'Ativar'} a forma de pagamento "${f.descricao}"?`,
      okText: inativar ? 'Inativar' : 'Ativar',
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.patch(`/formas-pagamento/${f.id}/status`, { ativo: !f.ativo });
          message.success(inativar ? 'Forma de pagamento inativada.' : 'Forma de pagamento ativada.');
          await carregar();
        } catch (e) {
          message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
        }
      }
    });
  }

  const colunas: TableColumnsType<FormaPagamento> = [
    { title: 'Descrição', dataIndex: 'descricao' },
    { title: 'Tipo', dataIndex: 'tipo', width: 160, render: (t: TipoFormaPagamento) => rotulo(TIPOS, t) },
    { title: 'Condição', dataIndex: 'condicao', width: 110, render: (c: CondicaoFormaPagamento) => rotulo(CONDICOES, c) },
    { title: 'Convênio', render: (_, f) => f.convenio?.nome ?? '—' },
    {
      title: 'Situação',
      dataIndex: 'ativo',
      width: 110,
      render: (ativo: boolean) => (ativo ? <Tag color="green">Ativa</Tag> : <Tag>Inativa</Tag>)
    },
    {
      title: 'Ações',
      width: 150,
      render: (_, f) =>
        podeEditar && (
          <Space>
            <Button
              size="small"
              icon={<EditOutlined />}
              title="Editar"
              onClick={() => {
                setEmEdicao(f);
                setFormAberto(true);
              }}
            />
            <Button
              size="small"
              danger={f.ativo}
              icon={<PoweroffOutlined />}
              title={f.ativo ? 'Inativar' : 'Ativar'}
              onClick={() => alterarStatus(f)}
            />
            <BotaoExcluir artigoTipo="a forma de pagamento" nome={f.descricao} caminho={`/formas-pagamento/${f.id}`} aoExcluir={carregar} />
          </Space>
        )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Formas de pagamento
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
            Nova forma de pagamento
          </Button>
        )}
      </Space>

      <Space style={{ marginBottom: 16 }} wrap>
        <Input.Search
          placeholder="Buscar por descrição"
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
        <Select<TipoFormaPagamento | 'todos'>
          value={tipoFiltro}
          style={{ width: 180 }}
          onChange={(v) => {
            setTipoFiltro(v);
            setPagina(1);
          }}
          options={[{ value: 'todos', label: 'Todos os tipos' }, ...TIPOS]}
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

      <Table<FormaPagamento>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Nenhuma forma de pagamento encontrada.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} forma(s) de pagamento`,
          onChange: setPagina
        }}
      />

      <FormularioFormaPagamento
        aberto={formAberto}
        forma={emEdicao}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />
    </>
  );
}
