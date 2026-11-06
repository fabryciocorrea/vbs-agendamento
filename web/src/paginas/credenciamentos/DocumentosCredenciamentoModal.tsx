import { useCallback, useEffect, useState } from 'react';
import { App, Button, Empty, Input, Modal, Popconfirm, Select, Space, Table, Tag, Typography, Upload } from 'antd';
import type { TableColumnsType, UploadFile } from 'antd';
import { DeleteOutlined, DownloadOutlined, EyeOutlined, UploadOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import { useAuth } from '../../auth/AuthContext';
import { formatarDataHora } from '../../util/formatos';

const MAX_DOC = 10 * 1024 * 1024;

type TipoDocumentoCredenciamento = 'PROTOCOLO' | 'CONTRATO' | 'APROVACAO' | 'TABELA_VALORES' | 'COMUNICADO' | 'OUTRO';

interface DocumentoCredenciamento {
  id: string;
  tipo: TipoDocumentoCredenciamento;
  tipoNome: string;
  descricao?: string | null;
  nomeArquivo: string;
  tipoConteudo: string;
  tamanhoBytes: number;
  enviadoEm: string;
  enviadoPor?: { id: number; nomeCompleto: string } | null;
}

/** PDF e imagem abrem em outra aba; Word e Excel só baixam. */
function abreNoNavegador(tipoConteudo: string): boolean {
  return tipoConteudo === 'application/pdf' || tipoConteudo.startsWith('image/');
}

const TIPOS: { value: TipoDocumentoCredenciamento; label: string }[] = [
  { value: 'PROTOCOLO', label: 'Protocolo' },
  { value: 'CONTRATO', label: 'Contrato' },
  { value: 'APROVACAO', label: 'Carta ou aprovação' },
  { value: 'TABELA_VALORES', label: 'Tabela de valores' },
  { value: 'COMUNICADO', label: 'Comunicado' },
  { value: 'OUTRO', label: 'Outro' }
];

function formatarTamanho(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} MB`;
}

/** Documentos anexados ao credenciamento: lista, envio, abertura, download e exclusão, conforme a permissão do perfil. */
export default function DocumentosCredenciamentoModal({ credenciamento, aoFechar }: { credenciamento: { id: number; convenioId: number; nome: string } | null; aoFechar: () => void }) {
  const { message } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('credenciamento.documento', 'incluir');
  const podeExcluir = pode('credenciamento.documento', 'excluir');

  const [docs, setDocs] = useState<DocumentoCredenciamento[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [tipo, setTipo] = useState<TipoDocumentoCredenciamento | undefined>();
  const [descricao, setDescricao] = useState('');
  const [arquivos, setArquivos] = useState<UploadFile[]>([]);
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  const base = credenciamento ? `/convenios/${credenciamento.convenioId}/credenciamentos/${credenciamento.id}/documentos` : null;

  const carregar = useCallback(async () => {
    if (!base) return;
    setCarregando(true);
    try {
      setDocs(await api.get<DocumentoCredenciamento[]>(base));
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os documentos.');
    } finally {
      setCarregando(false);
    }
  }, [base, message]);

  useEffect(() => {
    setDocs([]);
    setTipo(undefined);
    setDescricao('');
    setArquivos([]);
    setErro(null);
    void carregar();
  }, [carregar]);

  async function enviar() {
    if (!base) return;
    if (!tipo) return setErro('Escolha o tipo do documento.');
    if (tipo === 'OUTRO' && descricao.trim() === '') return setErro('Descreva o documento.');
    if (arquivos.length === 0) return setErro('Anexe o arquivo (PDF, Word, Excel, JPG ou PNG, até 10 MB).');
    setErro(null);
    setEnviando(true);
    try {
      await api.upload<DocumentoCredenciamento>(base, arquivos[0].originFileObj as File, {
        tipo,
        descricao: descricao.trim()
      });
      message.success('Documento enviado.');
      setTipo(undefined);
      setDescricao('');
      setArquivos([]);
      await carregar();
    } catch (e) {
      setErro(e instanceof ApiErro ? e.message : 'Não foi possível enviar o documento.');
    } finally {
      setEnviando(false);
    }
  }

  async function abrir(d: DocumentoCredenciamento) {
    try {
      if (abreNoNavegador(d.tipoConteudo)) await api.abrir(`${base}/${d.id}`);
      else await api.baixar(`${base}/${d.id}`, d.nomeArquivo);
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível abrir o documento.');
    }
  }

  async function baixar(d: DocumentoCredenciamento) {
    try {
      await api.baixar(`${base}/${d.id}`, d.nomeArquivo);
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível baixar o documento.');
    }
  }

  async function excluir(d: DocumentoCredenciamento) {
    try {
      await api.delete(`${base}/${d.id}`);
      message.success('Documento excluído.');
      await carregar();
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível excluir o documento.');
    }
  }

  const colunas: TableColumnsType<DocumentoCredenciamento> = [
    { title: 'Tipo', dataIndex: 'tipoNome', width: 190, render: (t: string) => <Tag>{t}</Tag> },
    { title: 'Descrição', dataIndex: 'descricao', render: (d?: string | null) => d || '—' },
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
      width: 170,
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
    <Modal open={!!credenciamento} title={credenciamento ? `Documentos — ${credenciamento.nome}` : ''} onCancel={aoFechar} footer={null} width={900} destroyOnClose>
      {podeIncluir && (
        <div style={{ marginBottom: 16 }}>
          <Space wrap align="start" style={{ width: '100%' }}>
            <Select
              value={tipo}
              onChange={setTipo}
              options={TIPOS}
              placeholder="Tipo do documento"
              style={{ width: 230 }}
            />
            <Input
              value={descricao}
              onChange={(e) => setDescricao(e.target.value)}
              maxLength={150}
              placeholder={tipo === 'OUTRO' ? 'Descreva o documento' : 'Descrição (opcional)'}
              style={{ width: 260 }}
            />
            <Upload
              maxCount={1}
              accept=".pdf,.doc,.docx,.xls,.xlsx,.jpg,.jpeg,.png"
              fileList={arquivos}
              onChange={({ fileList }) => setArquivos(fileList)}
              beforeUpload={(f) => {
                if (f.size > MAX_DOC) {
                  setErro('O documento deve ter no máximo 10 MB.');
                  return Upload.LIST_IGNORE;
                }
                setErro(null);
                return false; // o envio é feito pelo botão Enviar
              }}
            >
              <Button icon={<UploadOutlined />}>Escolher arquivo</Button>
            </Upload>
            <Button type="primary" loading={enviando} onClick={() => void enviar()}>
              Enviar
            </Button>
          </Space>
          <Typography.Text type="secondary" style={{ display: 'block', marginTop: 6, fontSize: 12 }}>
            PDF, Word (doc, docx), Excel (xls, xlsx), JPG ou PNG, até 10 MB. No máximo 30 documentos por credenciamento.
          </Typography.Text>
          {erro && (
            <Typography.Text type="danger" style={{ display: 'block', marginTop: 4 }}>
              {erro}
            </Typography.Text>
          )}
        </div>
      )}

      <Table<DocumentoCredenciamento>
        rowKey="id"
        size="small"
        loading={carregando}
        columns={colunas}
        dataSource={docs}
        pagination={false}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhum documento anexado" /> }}
      />
    </Modal>
  );
}
