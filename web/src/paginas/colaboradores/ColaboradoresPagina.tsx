import { useCallback, useEffect, useState } from 'react';
import { App, Button, Col, Divider, Form, Input, Modal, Radio, Row, Select, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { EditOutlined, PlusOutlined, PoweroffOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { Pagina } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import BotaoExcluir from '../../componentes/BotaoExcluir';
import CampoNumeroEndereco from '../../componentes/CampoNumeroEndereco';
import {
  celularValido,
  cnpjValido,
  cpfValido,
  formatarCelular,
  formatarCnpj,
  formatarCpf,
  formatarTelefone,
  hojeIso,
  somenteDigitos,
  UFS
} from '../../util/formatos';
import { formatarPis, pisValido } from './pis';
import type { Colaborador, ColaboradorEntrada, TipoVinculo } from './tipos';

const OBRIGATORIO = 'Campo Não Preenchido';
const TAMANHO = 10;

type FiltroSituacao = 'todos' | 'ativos' | 'inativos';
type FiltroVinculo = 'todos' | TipoVinculo;

interface ValoresFormulario {
  tipoVinculo: TipoVinculo;
  nome: string;
  cpf?: string;
  razaoSocial?: string;
  nomeFantasia?: string;
  cnpj?: string;
  telefone: string;
  email: string;
  endereco: string;
  numero: string;
  complemento?: string;
  cidade: string;
  uf: string;
  cargo: string;
  ctpsNumero?: string;
  ctpsSerie?: string;
  ctpsUf?: string;
  ctpsDataEmissao?: string;
  pisPasep?: string;
  dataAdmissao?: string;
}

const textoOuNulo = (v?: string) => (v && v.trim() !== '' ? v.trim() : null);

interface FormularioProps {
  aberto: boolean;
  colaborador: Colaborador | null;
  aoFechar: () => void;
  aoSalvar: () => void;
}

function FormularioColaborador({ aberto, colaborador, aoFechar, aoSalvar }: FormularioProps) {
  const { message } = App.useApp();
  const [form] = Form.useForm<ValoresFormulario>();
  const [salvando, setSalvando] = useState(false);
  const vinculo = Form.useWatch('tipoVinculo', form) ?? colaborador?.tipoVinculo ?? 'CLT';
  const mei = vinculo === 'MEI';

  async function enviar() {
    let v: ValoresFormulario;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    const corpo: ColaboradorEntrada = {
      tipoVinculo: v.tipoVinculo,
      nome: v.nome.trim(),
      cpf: v.cpf && somenteDigitos(v.cpf) !== '' ? somenteDigitos(v.cpf) : null,
      razaoSocial: v.tipoVinculo === 'MEI' ? textoOuNulo(v.razaoSocial) : null,
      nomeFantasia: v.tipoVinculo === 'MEI' ? textoOuNulo(v.nomeFantasia) : null,
      cnpj: v.tipoVinculo === 'MEI' && v.cnpj ? somenteDigitos(v.cnpj) : null,
      telefone: somenteDigitos(v.telefone),
      email: v.email.trim(),
      endereco: v.endereco.trim(),
      numero: v.numero.trim(),
      complemento: textoOuNulo(v.complemento),
      cidade: v.cidade.trim(),
      uf: v.uf,
      cargo: v.cargo.trim(),
      ctpsNumero: v.tipoVinculo === 'CLT' ? textoOuNulo(v.ctpsNumero) : null,
      ctpsSerie: v.tipoVinculo === 'CLT' ? textoOuNulo(v.ctpsSerie) : null,
      ctpsUf: v.tipoVinculo === 'CLT' ? (v.ctpsUf ?? null) : null,
      ctpsDataEmissao: v.tipoVinculo === 'CLT' ? textoOuNulo(v.ctpsDataEmissao) : null,
      pisPasep: v.tipoVinculo === 'CLT' && v.pisPasep && somenteDigitos(v.pisPasep) !== '' ? somenteDigitos(v.pisPasep) : null,
      dataAdmissao: v.tipoVinculo === 'CLT' ? textoOuNulo(v.dataAdmissao) : null
    };
    setSalvando(true);
    try {
      if (colaborador) await api.put<Colaborador>(`/colaboradores/${colaborador.id}`, corpo);
      else await api.post<Colaborador>('/colaboradores', corpo);
      message.success(colaborador ? 'Cadastro atualizado.' : 'Cadastro realizado.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(e.erros.map((x) => ({ name: x.campo as keyof ValoresFormulario, errors: [x.mensagem] })));
      } else if (e instanceof ApiErro && e.codigo === 'CPF_DUPLICADO') {
        form.setFields([{ name: 'cpf', errors: [e.message] }]);
      } else if (e instanceof ApiErro && e.codigo === 'CNPJ_DUPLICADO') {
        form.setFields([{ name: 'cnpj', errors: [e.message] }]);
      } else if (e instanceof ApiErro && e.codigo === 'PIS_DUPLICADO') {
        form.setFields([{ name: 'pisPasep', errors: [e.message] }]);
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
      title={colaborador ? 'Editar colaborador / prestador' : 'Novo colaborador / prestador'}
      okText="Salvar"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
      width={760}
    >
      <Form<ValoresFormulario>
        form={form}
        layout="vertical"
        requiredMark
        preserve={false}
        initialValues={
          colaborador
            ? {
                ...colaborador,
                cpf: formatarCpf(colaborador.cpf),
                cnpj: formatarCnpj(colaborador.cnpj),
                telefone: formatarCelular(colaborador.telefone),
                ctpsNumero: colaborador.ctpsNumero ?? undefined,
                ctpsSerie: colaborador.ctpsSerie ?? undefined,
                ctpsUf: colaborador.ctpsUf ?? undefined,
                ctpsDataEmissao: colaborador.ctpsDataEmissao ? colaborador.ctpsDataEmissao.slice(0, 10) : undefined,
                pisPasep: formatarPis(colaborador.pisPasep),
                dataAdmissao: colaborador.dataAdmissao ? colaborador.dataAdmissao.slice(0, 10) : undefined
              }
            : { tipoVinculo: 'CLT' }
        }
      >
        <Row gutter={16}>
          <Col xs={24} md={6}>
            <Form.Item label="Matrícula" extra="Gerada automaticamente.">
              <Input value={colaborador ? String(colaborador.id) : ''} placeholder="Automática" disabled />
            </Form.Item>
          </Col>
          <Col xs={24} md={18}>
            <Form.Item name="tipoVinculo" label="Vínculo" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Radio.Group
                optionType="button"
                buttonStyle="solid"
                options={[
                  { value: 'CLT', label: 'Colaborador (CLT)' },
                  { value: 'MEI', label: 'Prestador (MEI)' }
                ]}
              />
            </Form.Item>
          </Col>
        </Row>

        <Row gutter={16}>
          <Col xs={24} md={16}>
            <Form.Item name="nome" label="Nome" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={150} />
            </Form.Item>
          </Col>
          <Col xs={24} md={8}>
            <Form.Item name="cargo" label="Cargo" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={80} />
            </Form.Item>
          </Col>
        </Row>

        <Row gutter={16}>
          <Col xs={24} md={8}>
            <Form.Item
              name="cpf"
              label="CPF"
              normalize={(v: string) => formatarCpf(v)}
              rules={[
                { required: !mei, message: OBRIGATORIO },
                { validator: (_, v: string) => (!v || cpfValido(v) ? Promise.resolve() : Promise.reject(new Error('CPF inválido.'))) }
              ]}
            >
              <Input placeholder="000.000.000-00" inputMode="numeric" />
            </Form.Item>
          </Col>
          {mei && (
            <Col xs={24} md={16}>
              <Form.Item name="razaoSocial" label="Razão social" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
                <Input maxLength={150} />
              </Form.Item>
            </Col>
          )}
        </Row>
        {mei && (
          <Row gutter={16}>
            <Col xs={24} md={16}>
              <Form.Item name="nomeFantasia" label="Nome fantasia">
                <Input maxLength={150} />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
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
          </Row>
        )}

        <Row gutter={16}>
          <Col xs={24} md={10}>
            <Form.Item
              name="telefone"
              label="Celular"
              normalize={(v: string) => formatarCelular(v)}
              rules={[
                { required: true, message: OBRIGATORIO },
                {
                  validator: (_, v: string) =>
                    !v || celularValido(v) ? Promise.resolve() : Promise.reject(new Error('Informe o celular com DDD: (00) 00000-0000.'))
                }
              ]}
            >
              <Input placeholder="(00) 00000-0000" inputMode="tel" maxLength={15} />
            </Form.Item>
          </Col>
          <Col xs={24} md={14}>
            <Form.Item
              name="email"
              label="E-mail"
              rules={[
                { required: true, message: OBRIGATORIO },
                { type: 'email', message: 'E-mail inválido.' }
              ]}
            >
              <Input maxLength={150} />
            </Form.Item>
          </Col>
        </Row>

        <Row gutter={16}>
          <Col xs={24} md={12}>
            <Form.Item name="endereco" label="Endereço" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={250} />
            </Form.Item>
          </Col>
          <Col xs={24} md={6}>
            <Form.Item name="numero" label="Número" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <CampoNumeroEndereco />
            </Form.Item>
          </Col>
          <Col xs={24} md={6}>
            <Form.Item name="complemento" label="Complemento">
              <Input maxLength={60} />
            </Form.Item>
          </Col>
        </Row>
        <Row gutter={16}>
          <Col xs={24} md={16}>
            <Form.Item name="cidade" label="Cidade" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={80} />
            </Form.Item>
          </Col>
          <Col xs={24} md={8}>
            <Form.Item name="uf" label="UF" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Select showSearch options={UFS.map((u) => ({ value: u, label: u }))} />
            </Form.Item>
          </Col>
        </Row>

        {!mei && (
          <>
            <Divider orientation="left" plain>
              Carteira de trabalho (CLT)
            </Divider>
            <Row gutter={16}>
              <Col xs={24} md={8}>
                <Form.Item name="ctpsNumero" label="Número da CTPS" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
                  <Input maxLength={20} />
                </Form.Item>
              </Col>
              <Col xs={24} md={6}>
                <Form.Item name="ctpsSerie" label="Série" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
                  <Input maxLength={10} />
                </Form.Item>
              </Col>
              <Col xs={24} md={4}>
                <Form.Item name="ctpsUf" label="UF da CTPS" rules={[{ required: true, message: OBRIGATORIO }]}>
                  <Select showSearch options={UFS.map((u) => ({ value: u, label: u }))} />
                </Form.Item>
              </Col>
              <Col xs={24} md={6}>
                <Form.Item name="ctpsDataEmissao" label="Data de emissão" rules={[{ required: true, message: OBRIGATORIO }]}>
                  <Input type="date" max={hojeIso()} />
                </Form.Item>
              </Col>
            </Row>
            <Row gutter={16}>
              <Col xs={24} md={10}>
                <Form.Item
                  name="pisPasep"
                  label="PIS/PASEP"
                  normalize={(v: string) => formatarPis(v)}
                  rules={[
                    { required: true, message: OBRIGATORIO },
                    { validator: (_, v: string) => (!v || pisValido(v) ? Promise.resolve() : Promise.reject(new Error('PIS/PASEP inválido.'))) }
                  ]}
                >
                  <Input placeholder="000.00000.00-0" inputMode="numeric" />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item name="dataAdmissao" label="Data de admissão" rules={[{ required: true, message: OBRIGATORIO }]}>
                  <Input type="date" />
                </Form.Item>
              </Col>
            </Row>
          </>
        )}
      </Form>
    </Modal>
  );
}

export default function ColaboradoresPagina() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('colaborador.cadastro', 'incluir');
  const podeEditar = pode('colaborador.cadastro', 'editar');

  const [dados, setDados] = useState<Pagina<Colaborador>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [vinculo, setVinculo] = useState<FiltroVinculo>('todos');
  const [situacao, setSituacao] = useState<FiltroSituacao>('todos');
  const [formAberto, setFormAberto] = useState(false);
  const [emEdicao, setEmEdicao] = useState<Colaborador | null>(null);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<Colaborador>>('/colaboradores', {
          busca: buscaAplicada,
          tipoVinculo: vinculo === 'todos' ? undefined : vinculo,
          ativo: situacao === 'todos' ? undefined : situacao === 'ativos',
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os cadastros.');
    } finally {
      setCarregando(false);
    }
  }, [buscaAplicada, vinculo, situacao, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  function alterarStatus(c: Colaborador) {
    const inativar = c.ativo;
    modal.confirm({
      title: inativar ? 'Inativar cadastro' : 'Ativar cadastro',
      content: `${inativar ? 'Inativar' : 'Ativar'} "${c.nome}"?`,
      okText: inativar ? 'Inativar' : 'Ativar',
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.patch(`/colaboradores/${c.id}/status`, { ativo: !c.ativo });
          message.success(inativar ? 'Cadastro inativado.' : 'Cadastro ativado.');
          await carregar();
        } catch (e) {
          message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
        }
      }
    });
  }

  const colunas: TableColumnsType<Colaborador> = [
    { title: 'Matrícula', dataIndex: 'id', width: 100 },
    {
      title: 'Nome',
      render: (_, c) => (
        <>
          {c.nome}
          {c.tipoVinculo === 'MEI' && c.razaoSocial ? (
            <div style={{ fontSize: 12, color: '#8c8c8c' }}>{c.nomeFantasia || c.razaoSocial}</div>
          ) : null}
        </>
      )
    },
    {
      title: 'Vínculo',
      dataIndex: 'tipoVinculo',
      width: 120,
      render: (v: TipoVinculo) => (v === 'CLT' ? <Tag color="blue">CLT</Tag> : <Tag color="purple">MEI</Tag>)
    },
    { title: 'Cargo', dataIndex: 'cargo' },
    {
      title: 'CPF / CNPJ',
      width: 170,
      render: (_, c) => (c.tipoVinculo === 'MEI' && c.cnpj ? formatarCnpj(c.cnpj) : formatarCpf(c.cpf))
    },
    { title: 'Celular', dataIndex: 'telefone', width: 150, render: (v: string) => formatarTelefone(v) },
    {
      title: 'Situação',
      dataIndex: 'ativo',
      width: 110,
      render: (ativo: boolean) => (ativo ? <Tag color="green">Ativo</Tag> : <Tag>Inativo</Tag>)
    },
    {
      title: 'Ações',
      width: 150,
      render: (_, c) => (
        <Space>
          {podeEditar && (
            <Button
              size="small"
              icon={<EditOutlined />}
              title="Editar"
              onClick={() => {
                setEmEdicao(c);
                setFormAberto(true);
              }}
            />
          )}
          {podeEditar && (
            <Button
              size="small"
              danger={c.ativo}
              icon={<PoweroffOutlined />}
              title={c.ativo ? 'Inativar' : 'Ativar'}
              onClick={() => alterarStatus(c)}
            />
          )}
          <BotaoExcluir artigoTipo="o colaborador" nome={c.nome} caminho={`/colaboradores/${c.id}`} aoExcluir={carregar} />
        </Space>
      )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Colaboradores e prestadores
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
            Novo cadastro
          </Button>
        )}
      </Space>

      <Space style={{ marginBottom: 16 }} wrap>
        <Input.Search
          placeholder="Buscar por nome, razão social ou matrícula"
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
        <Select<FiltroVinculo>
          value={vinculo}
          style={{ width: 190 }}
          onChange={(v) => {
            setVinculo(v);
            setPagina(1);
          }}
          options={[
            { value: 'todos', label: 'Todos os vínculos' },
            { value: 'CLT', label: 'Colaboradores (CLT)' },
            { value: 'MEI', label: 'Prestadores (MEI)' }
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

      <Table<Colaborador>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Nenhum cadastro encontrado.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} cadastro(s)`,
          onChange: setPagina
        }}
      />

      <FormularioColaborador
        aberto={formAberto}
        colaborador={emEdicao}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />
    </>
  );
}
