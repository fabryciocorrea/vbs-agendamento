import { useCallback, useEffect, useState } from 'react';
import { App, Button, Empty, Form, Input, Modal, Popconfirm, Select, Space, Table, Tag, Typography, Upload } from 'antd';
import type { TableColumnsType, UploadFile } from 'antd';
import { DeleteOutlined, DownloadOutlined, EyeOutlined, FolderOpenOutlined as IconeTela, PlusOutlined, UploadOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import { useAuth } from '../../auth/AuthContext';
import CabecalhoPagina from '../../layout/CabecalhoPagina';
import CampoBusca from '../../componentes/CampoBusca';
import { formatarDataHora } from '../../util/formatos';

const OBRIGATORIO = 'Campo Não Preenchido';
const MAX_DOC = 10 * 1024 * 1024;

type TipoDocumentoInterno = 'FOLHA_TIMBRADA' | 'TERMO' | 'CONTRATO' | 'POLITICA' | 'FORMULARIO' | 'MODELO' | 'OUTRO';

interface DocumentoInterno {
  id: string;
  tipo: TipoDocumentoInterno;
  tipoNome: string;
  titulo: string;
  descricao?: string | null;
  nomeArquivo: string;
  tipoConteudo: string;
  tamanhoBytes: number;
  enviadoEm: string;
  enviadoPor?: { id: number; nomeCompleto: string } | null;
}

const TIPOS: { value: TipoDocumentoInterno; label: string }[] = [
  { value: 'FOLHA_TIMBRADA', label: 'Folha timbrada' },
  { value: 'TERMO', label: 'Termo' },
  { value: 'CONTRATO', label: 'Contrato' },
  { value: 'POLITICA', label: 'Política ou norma' },
  { value: 'FORMULARIO', label: 'Formulário' },
  { value: 'MODELO', label: 'Modelo de documento' },
  { value: 'OUTRO', label: 'Outro' }
];

function formatarTamanho(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} MB`;
}

/** PDF e imagem abrem em outra aba; Word e Excel só baixam. */
function abreNoNavegador(tipoConteudo: string): boolean {
  return tipoConteudo === 'application/pdf' || tipoConteudo.startsWith('image/');
}

interface Valores {
  tipo: TipoDocumentoInterno;
  titulo: string;
  descricao?: string;
}

function EnvioModal({ aberto, aoFechar, aoEnviar }: { aberto: boolean; aoFechar: () => void; aoEnviar: () => void }) {
  const { message } = App.useApp();
  const [form] = Form.useForm<Valores>();
  const [arquivos, setArquivos] = useState<UploadFile[]>([]);
  const [erroArquivo, setErroArquivo] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    if (aberto) {
      form.resetFields();
      setArquivos([]);
      setErroArquivo(null);
    }
  }, [aberto, form]);

  async function enviar() {
    let v: Valores;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    if (arquivos.length === 0) {
      setErroArquivo('Anexe o documento.');
      return;
    }
    setEnviando(true);
    try {
      await api.upload<DocumentoInterno>('/documentos-internos', arquivos[0].originFileObj as File, {
        tipo: v.tipo,
        titulo: v.titulo.trim(),
        descricao: v.descricao?.trim()
      });
      message.success('Documento enviado.');
      aoEnviar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(
          e.erros
            .filter((x) => x.campo !== 'arquivo')
            .map((x) => ({ name: x.campo as keyof Valores, errors: [x.mensagem] }))
        );
        const doArquivo = e.erros.find((x) => x.campo === 'arquivo');
        setErroArquivo(doArquivo ? doArquivo.mensagem : null);
      } else {
        setErroArquivo(e instanceof ApiErro ? e.message : 'Não foi possível enviar o documento.');
      }
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Modal
      open={aberto}
      title="Novo documento"
      okText="Enviar"
      cancelText="Cancelar"
      confirmLoading={enviando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
      width={560}
    >
      <Form<Valores> form={form} layout="vertical" requiredMark preserve={false}>
        <Form.Item name="tipo" label="Tipo" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select options={TIPOS} placeholder="Selecione" />
        </Form.Item>
        <Form.Item
          name="titulo"
          label="Título"
          rules={[
            { required: true, whitespace: true, message: OBRIGATORIO },
            { max: 150, message: 'No máximo 150 caracteres' }
          ]}
        >
          <Input maxLength={150} placeholder="Ex.: Termo de consentimento — atendimento online" />
        </Form.Item>
        <Form.Item name="descricao" label="Descrição" rules={[{ max: 300, message: 'No máximo 300 caracteres' }]}>
          <Input.TextArea rows={3} maxLength={300} showCount />
        </Form.Item>
        <Form.Item label="Arquivo" required validateStatus={erroArquivo ? 'error' : undefined} help={erroArquivo ?? undefined}>
          <Upload
            maxCount={1}
            accept=".pdf,.doc,.docx,.xls,.xlsx,.jpg,.jpeg,.png"
            fileList={arquivos}
            onChange={({ fileList }) => setArquivos(fileList)}
            beforeUpload={(f) => {
              if (f.size > MAX_DOC) {
                setErroArquivo('O documento deve ter no máximo 10 MB.');
                return Upload.LIST_IGNORE;
              }
              setErroArquivo(null);
              return false; // o envio é feito pelo botão Enviar
            }}
          >
            <Button icon={<UploadOutlined />}>Escolher arquivo</Button>
          </Upload>
          <Typography.Text type="secondary" style={{ display: 'block', marginTop: 6, fontSize: 12 }}>
            PDF, Word (doc, docx), Excel (xls, xlsx), JPG ou PNG, até 10 MB.
          </Typography.Text>
        </Form.Item>
      </Form>
    </Modal>
  );
}

/** Documentos internos da empresa (folha timbrada, termos, contratos, políticas, formulários, modelos...). */
export default function DocumentosPagina() {
  const { message } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('documento.interno', 'incluir');
  const podeExcluir = pode('documento.interno', 'excluir');

  const [docs, setDocs] = useState<DocumentoInterno[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [tipo, setTipo] = useState<TipoDocumentoInterno | undefined>();
  const [busca, setBusca] = useState('');
  const [enviandoAberto, setEnviandoAberto] = useState(false);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDocs(await api.get<DocumentoInterno[]>('/documentos-internos', { tipo, busca: busca.trim() || undefined }));
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os documentos.');
    } finally {
      setCarregando(false);
    }
  }, [tipo, busca, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  async function abrir(d: DocumentoInterno) {
    try {
      if (abreNoNavegador(d.tipoConteudo)) await api.abrir(`/documentos-internos/${d.id}`);
      else await api.baixar(`/documentos-internos/${d.id}`, d.nomeArquivo);
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível abrir o documento.');
    }
  }

  async function baixar(d: DocumentoInterno) {
    try {
      await api.baixar(`/documentos-internos/${d.id}`, d.nomeArquivo);
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível baixar o documento.');
    }
  }

  async function excluir(d: DocumentoInterno) {
    try {
      await api.delete(`/documentos-internos/${d.id}`);
      message.success('Documento excluído.');
      await carregar();
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível excluir o documento.');
    }
  }

  const colunas: TableColumnsType<DocumentoInterno> = [
    { title: 'Tipo', dataIndex: 'tipoNome', width: 190, render: (t: string) => <Tag>{t}</Tag> },
    {
      title: 'Título',
      render: (_, d) => (
        <>
          <strong>{d.titulo}</strong>
          {d.descricao && (
            <div>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {d.descricao}
              </Typography.Text>
            </div>
          )}
        </>
      )
    },
    {
      title: 'Arquivo',
      width: 230,
      render: (_, d) => (
        <>
          <Typography.Text ellipsis style={{ maxWidth: 200, display: 'inline-block', verticalAlign: 'bottom' }} title={d.nomeArquivo}>
            {d.nomeArquivo}
          </Typography.Text>
          <br />
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {formatarTamanho(d.tamanhoBytes)}
          </Typography.Text>
        </>
      )
    },
    {
      title: 'Enviado',
      width: 180,
      render: (_, d) => (
        <>
          {formatarDataHora(d.enviadoEm)}
          <br />
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {d.enviadoPor?.nomeCompleto ?? '—'}
          </Typography.Text>
        </>
      )
    },
    {
      title: 'Ações',
      width: 130,
      render: (_, d) => (
        <Space>
          <Button size="small" icon={<EyeOutlined />} title={abreNoNavegador(d.tipoConteudo) ? 'Abrir em outra aba' : 'Baixar para abrir'} onClick={() => void abrir(d)} />
          <Button size="small" icon={<DownloadOutlined />} title="Baixar" onClick={() => void baixar(d)} />
          {podeExcluir && (
            <Popconfirm
              title="Excluir este documento?"
              description="O arquivo é apagado de vez."
              okText="Excluir"
              cancelText="Cancelar"
              okButtonProps={{ danger: true }}
              onConfirm={() => void excluir(d)}
            >
              <Button size="small" danger icon={<DeleteOutlined />} title="Excluir" />
            </Popconfirm>
          )}
        </Space>
      )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <CabecalhoPagina icone={<IconeTela />} titulo="Documentos" subtitulo="Folha timbrada, termos, contratos, políticas e outros documentos internos da empresa" />
        {podeIncluir && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setEnviandoAberto(true)}>
            Novo documento
          </Button>
        )}
      </Space>

      <Space wrap style={{ marginBottom: 16 }}>
        <CampoBusca allowClear placeholder="Buscar por título, descrição ou arquivo" style={{ width: 340 }} onSearch={(v) => setBusca(v)} />
        <Select allowClear value={tipo} onChange={setTipo} options={TIPOS} placeholder="Todos os tipos" style={{ width: 220 }} />
      </Space>

      <Table<DocumentoInterno>
        rowKey="id"
        size="middle"
        loading={carregando}
        columns={colunas}
        dataSource={docs}
        pagination={{ pageSize: 10, showSizeChanger: false, hideOnSinglePage: true }}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhum documento cadastrado" /> }}
      />

      <EnvioModal
        aberto={enviandoAberto}
        aoFechar={() => setEnviandoAberto(false)}
        aoEnviar={() => {
          setEnviandoAberto(false);
          void carregar();
        }}
      />
    </>
  );
}
