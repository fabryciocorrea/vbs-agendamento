import { useCallback, useEffect, useState } from 'react';
import { App, Button, Empty, Input, Modal, Popconfirm, Select, Space, Table, Tag, Typography, Upload } from 'antd';
import type { TableColumnsType, UploadFile } from 'antd';
import { DeleteOutlined, DownloadOutlined, EyeOutlined, UploadOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { DocumentoPaciente, TipoDocumentoPaciente } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import { formatarDataHora } from '../../util/formatos';

const MAX_DOC = 5 * 1024 * 1024;

const TIPOS: { value: TipoDocumentoPaciente; label: string }[] = [
  { value: 'IDENTIDADE', label: 'Documento de identidade' },
  { value: 'ENCAMINHAMENTO', label: 'Encaminhamento' },
  { value: 'AUTORIZACAO', label: 'Autorização' },
  { value: 'CARTEIRINHA', label: 'Carteirinha do convênio' },
  { value: 'EXAME', label: 'Exame ou laudo' },
  { value: 'OUTRO', label: 'Outro' }
];

function formatarTamanho(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} MB`;
}

/** Documentos anexados ao cadastro do paciente: lista, envio, abertura, download e exclusão, conforme a permissão do perfil. */
export default function DocumentosPacienteModal({ paciente, aoFechar }: { paciente: { id: string; nome: string } | null; aoFechar: () => void }) {
  const { message } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('paciente.documento', 'incluir');
  const podeExcluir = pode('paciente.documento', 'excluir');

  const [docs, setDocs] = useState<DocumentoPaciente[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [tipo, setTipo] = useState<TipoDocumentoPaciente | undefined>();
  const [descricao, setDescricao] = useState('');
  const [arquivos, setArquivos] = useState<UploadFile[]>([]);
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  const pacienteId = paciente?.id;

  const carregar = useCallback(async () => {
    if (!pacienteId) return;
    setCarregando(true);
    try {
      setDocs(await api.get<DocumentoPaciente[]>(`/pacientes/${pacienteId}/documentos`));
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os documentos.');
    } finally {
      setCarregando(false);
    }
  }, [pacienteId, message]);

  useEffect(() => {
    setDocs([]);
    setTipo(undefined);
    setDescricao('');
    setArquivos([]);
    setErro(null);
    void carregar();
  }, [carregar]);

  async function enviar() {
    if (!pacienteId) return;
    if (!tipo) return setErro('Escolha o tipo do documento.');
    if (tipo === 'OUTRO' && descricao.trim() === '') return setErro('Descreva o documento.');
    if (arquivos.length === 0) return setErro('Anexe o arquivo (PDF, JPG ou PNG, até 5 MB).');
    setErro(null);
    setEnviando(true);
    try {
      await api.upload<DocumentoPaciente>(`/pacientes/${pacienteId}/documentos`, arquivos[0].originFileObj as File, {
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

  async function abrir(d: DocumentoPaciente) {
    try {
      await api.abrir(`/pacientes/${pacienteId}/documentos/${d.id}`);
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível abrir o documento.');
    }
  }

  async function baixar(d: DocumentoPaciente) {
    try {
      await api.baixar(`/pacientes/${pacienteId}/documentos/${d.id}`, d.nomeArquivo);
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível baixar o documento.');
    }
  }

  async function excluir(d: DocumentoPaciente) {
    try {
      await api.delete(`/pacientes/${pacienteId}/documentos/${d.id}`);
      message.success('Documento excluído.');
      await carregar();
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível excluir o documento.');
    }
  }

  const colunas: TableColumnsType<DocumentoPaciente> = [
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
          <Button size="small" icon={<EyeOutlined />} title="Abrir em outra aba" onClick={() => void abrir(d)} />
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
    <Modal open={!!paciente} title={paciente ? `Documentos — ${paciente.nome}` : ''} onCancel={aoFechar} footer={null} width={900} destroyOnClose>
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
              accept=".pdf,.jpg,.jpeg,.png"
              fileList={arquivos}
              onChange={({ fileList }) => setArquivos(fileList)}
              beforeUpload={(f) => {
                if (f.size > MAX_DOC) {
                  setErro('O documento deve ter no máximo 5 MB.');
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
            PDF, JPG ou PNG, até 5 MB. No máximo 50 documentos por paciente.
          </Typography.Text>
          {erro && (
            <Typography.Text type="danger" style={{ display: 'block', marginTop: 4 }}>
              {erro}
            </Typography.Text>
          )}
        </div>
      )}

      <Table<DocumentoPaciente>
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
