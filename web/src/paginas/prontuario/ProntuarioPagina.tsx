import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import type { FormInstance } from 'antd';
import { Alert, App, Button, DatePicker, Empty, Form, Input, Modal, Pagination, Select, Space, Spin, Tag, Timeline, Typography, Upload } from 'antd';
import type { UploadFile } from 'antd';
import { ArrowLeftOutlined, DownloadOutlined, EditOutlined, HistoryOutlined, PaperClipOutlined, PlusOutlined, SwapOutlined, UploadOutlined } from '@ant-design/icons';
import type { Dayjs } from 'dayjs';
import { api, ApiErro } from '../../api/cliente';
import type { CorrecaoProntuario, DocumentoAutorizacao, ItemApoio, Pagina, Paciente, RegistroProntuario } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import { formatarDataHora } from '../../util/formatos';
import TransferenciaProntuarioModal from './TransferenciaProntuarioModal';
import { FileTextOutlined as IconeTela } from '@ant-design/icons';
import CabecalhoPagina from '../../layout/CabecalhoPagina';

const OBRIGATORIO = 'Campo Não Preenchido';
const TAMANHO = 10;
const MAX_DOC = 5 * 1024 * 1024;

interface ValoresRegistro {
  observacao: string;
  profissionalId?: number;
  solicitadoPorProfissionalId?: number;
  autorizadoPor?: string;
  observacaoAutorizacao?: string;
  documento?: UploadFile[];
}

/** Leva os erros de campo da API para o formulário (autorizacao.xxx vira xxx). */
function aplicarErros(e: ApiErro, form: FormInstance<ValoresRegistro>) {
  const mapa: Record<string, keyof ValoresRegistro> = {
    observacao: 'observacao',
    profissionalId: 'profissionalId',
    'autorizacao.solicitadoPorProfissionalId': 'solicitadoPorProfissionalId',
    'autorizacao.autorizadoPor': 'autorizadoPor',
    'autorizacao.documentoId': 'documento',
    arquivo: 'documento',
    'autorizacao.observacao': 'observacaoAutorizacao'
  };
  const campos = e.erros.filter((x) => mapa[x.campo]).map((x) => ({ name: mapa[x.campo], errors: [x.mensagem] }));
  if (campos.length > 0) form.setFields(campos);
  return campos.length > 0;
}

/** Campos da autorização do administrador: todos obrigatórios (solicitante, responsável legal, observação e documento). */
function CamposAutorizacao({ profissionais }: { profissionais: ItemApoio[] }) {
  return (
    <>
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        message="Autorização obrigatória"
        description="O administrador só inclui ou altera um registro com a solicitação do profissional e o OK do responsável legal. Estes dados e o documento ficam gravados no registro e no log. Não escreva texto clínico nestes campos."
      />
      <Form.Item name="solicitadoPorProfissionalId" label="Profissional que solicitou" rules={[{ required: true, message: OBRIGATORIO }]}>
        <Select<number>
          showSearch
          optionFilterProp="label"
          placeholder="Selecione o profissional"
          options={profissionais.filter((p) => p.ativo).map((p) => ({ value: p.id, label: p.nome }))}
        />
      </Form.Item>
      <Form.Item name="autorizadoPor" label="Responsável legal que deu o OK" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }, { min: 3, message: 'Mínimo de 3 caracteres' }]}>
        <Input maxLength={150} placeholder="Nome completo, como no documento" />
      </Form.Item>
      <Form.Item
        name="documento"
        required
        label="Documento de autorização (PDF, JPG ou PNG, até 5 MB)"
        valuePropName="fileList"
        getValueFromEvent={(e: { fileList?: UploadFile[] }) => e?.fileList ?? []}
        rules={[{ validator: (_, v: UploadFile[] | undefined) => (v && v.length > 0 ? Promise.resolve() : Promise.reject(new Error(OBRIGATORIO))) }]}
      >
        <Upload
          maxCount={1}
          accept=".pdf,.jpg,.jpeg,.png"
          beforeUpload={(f) => {
            if (f.size > MAX_DOC) {
              Modal.warning({ title: 'Documento muito grande', content: 'O documento deve ter no máximo 5 MB.' });
              return Upload.LIST_IGNORE;
            }
            return false; // o envio é feito ao salvar
          }}
        >
          <Button icon={<UploadOutlined />}>Anexar documento</Button>
        </Upload>
      </Form.Item>
      <Form.Item
        name="observacaoAutorizacao"
        label="Observação da autorização (como e quando foi pedido e autorizado)"
        rules={[{ required: true, whitespace: true, message: OBRIGATORIO }, { min: 10, message: 'Mínimo de 10 caracteres' }]}
      >
        <Input.TextArea rows={3} maxLength={1000} showCount />
      </Form.Item>
    </>
  );
}

/** Envia o documento (uma vez por arquivo) e devolve o código para a gravação. */
async function enviarDocumento(pacienteId: string, lista: UploadFile[], ultimo: { current: { uid: string; id: string } | null }): Promise<string> {
  const item = lista[0];
  if (ultimo.current && ultimo.current.uid === item.uid) return ultimo.current.id;
  const arquivo = item.originFileObj as File;
  const doc = await api.upload<DocumentoAutorizacao>(`/pacientes/${pacienteId}/prontuario/autorizacoes`, arquivo);
  ultimo.current = { uid: item.uid, id: doc.id };
  return doc.id;
}

function chaveRascunho(pacienteId: string) {
  return `vbs.prontuario.rascunho.${pacienteId}`;
}

/** Baixa o documento de autorização (só quem tem a liberação de correções do prontuário). */
function BotaoDocumento({ pacienteId, documento }: { pacienteId: string; documento: DocumentoAutorizacao }) {
  const { message } = App.useApp();
  const [baixando, setBaixando] = useState(false);
  async function baixar() {
    setBaixando(true);
    try {
      await api.baixar(`/pacientes/${pacienteId}/prontuario/autorizacoes/${documento.id}`, documento.nomeArquivo);
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível baixar o documento.');
    } finally {
      setBaixando(false);
    }
  }
  return (
    <Button size="small" icon={<DownloadOutlined />} loading={baixando} onClick={() => void baixar()} title={documento.nomeArquivo}>
      <PaperClipOutlined /> Documento
    </Button>
  );
}

/** Novo registro. Profissional responsável: só o texto. Administrador: também profissional e autorização. */
function NovoRegistroModal({
  aberto, pacienteId, administrador, profissionais, consultaId, profissionalInicial, aoFechar, aoSalvar
}: {
  aberto: boolean;
  pacienteId: string;
  consultaId?: string;
  profissionalInicial?: number;
  administrador: boolean;
  profissionais: ItemApoio[];
  aoFechar: () => void;
  aoSalvar: () => void;
}) {
  const { message, modal } = App.useApp();
  const [form] = Form.useForm<ValoresRegistro>();
  const [salvando, setSalvando] = useState(false);
  const [erroGeral, setErroGeral] = useState('');
  const docEnviado = useRef<{ uid: string; id: string } | null>(null);

  useEffect(() => {
    if (!aberto) return;
    setErroGeral('');
    form.resetFields();
    docEnviado.current = null;
    if (profissionalInicial) form.setFieldsValue({ profissionalId: profissionalInicial, solicitadoPorProfissionalId: profissionalInicial });
    // O rascunho fica só neste navegador; o servidor não guarda rascunho.
    try {
      const r = localStorage.getItem(chaveRascunho(pacienteId));
      if (r) form.setFieldsValue({ observacao: r });
    } catch {
      /* sem armazenamento: segue sem rascunho */
    }
  }, [aberto, pacienteId, profissionalInicial, form]);

  function guardarRascunho(texto: string) {
    try {
      if (texto) localStorage.setItem(chaveRascunho(pacienteId), texto);
      else localStorage.removeItem(chaveRascunho(pacienteId));
    } catch {
      /* ignora */
    }
  }

  async function enviar(v: ValoresRegistro) {
    setSalvando(true);
    setErroGeral('');
    try {
      const documentoId = administrador ? await enviarDocumento(pacienteId, v.documento ?? [], docEnviado) : undefined;
      await api.post(`/pacientes/${pacienteId}/prontuario`, {
        observacao: v.observacao,
        consultaId: consultaId || undefined,
        ...(administrador
          ? {
              profissionalId: v.profissionalId,
              autorizacao: {
                solicitadoPorProfissionalId: v.solicitadoPorProfissionalId,
                autorizadoPor: v.autorizadoPor,
                observacao: v.observacaoAutorizacao,
                documentoId
              }
            }
          : {})
      });
      guardarRascunho('');
      message.success('Registro salvo no prontuário.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro) {
        if (!aplicarErros(e, form)) setErroGeral(e.message);
      } else setErroGeral('Não foi possível salvar o registro.');
    } finally {
      setSalvando(false);
    }
  }

  async function salvar() {
    let v: ValoresRegistro;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    modal.confirm({
      title: 'Salvar o registro?',
      content: administrador
        ? 'O registro ficará em nome do profissional informado, com a autorização gravada. Depois de salvo, só o administrador pode alterá-lo, e o texto anterior fica no histórico.'
        : 'Depois de salvo, o registro não pode ser editado nem excluído por você. Só o administrador altera, com a sua solicitação e o OK do responsável legal.',
      okText: 'Salvar registro',
      cancelText: 'Voltar e revisar',
      onOk: () => enviar(v)
    });
  }

  return (
    <Modal
      title="Novo registro no prontuário"
      open={aberto}
      width={720}
      onCancel={aoFechar}
      onOk={() => void salvar()}
      okText="Salvar registro"
      cancelText="Cancelar"
      confirmLoading={salvando}
      maskClosable={false}
      destroyOnClose
    >
      <Form form={form} layout="vertical" requiredMark>
        {erroGeral && <Alert type="error" showIcon message={erroGeral} style={{ marginBottom: 16 }} />}
        {consultaId && <Alert type="info" showIcon message="Este registro ficará vinculado à consulta aberta pela Agenda." style={{ marginBottom: 16 }} />}
        {administrador && (
          <Form.Item name="profissionalId" label="Em nome de qual profissional" rules={[{ required: true, message: OBRIGATORIO }]}>
            <Select<number>
              showSearch
              optionFilterProp="label"
              placeholder="Profissional (com CRM ou CRP)"
              options={profissionais.filter((p) => p.ativo).map((p) => ({ value: p.id, label: p.nome }))}
              onChange={(v) => {
                if (!form.getFieldValue('solicitadoPorProfissionalId')) form.setFieldsValue({ solicitadoPorProfissionalId: v });
              }}
            />
          </Form.Item>
        )}
        <Form.Item name="observacao" label="Registro do atendimento" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
          <Input.TextArea rows={10} maxLength={20000} showCount onChange={(e) => guardarRascunho(e.target.value)} />
        </Form.Item>
        {administrador && <CamposAutorizacao profissionais={profissionais} />}
      </Form>
    </Modal>
  );
}

/** Edição de um registro salvo: só o administrador, sempre com autorização. */
function EditarRegistroModal({
  registro, pacienteId, profissionais, aoFechar, aoSalvar
}: {
  registro: RegistroProntuario | null;
  pacienteId: string;
  profissionais: ItemApoio[];
  aoFechar: () => void;
  aoSalvar: () => void;
}) {
  const { message, modal } = App.useApp();
  const [form] = Form.useForm<ValoresRegistro>();
  const [salvando, setSalvando] = useState(false);
  const [erroGeral, setErroGeral] = useState('');
  const docEnviado = useRef<{ uid: string; id: string } | null>(null);

  useEffect(() => {
    if (!registro) return;
    setErroGeral('');
    form.resetFields();
    docEnviado.current = null;
    form.setFieldsValue({ observacao: registro.observacao, solicitadoPorProfissionalId: registro.profissional.id });
  }, [registro, form]);

  async function enviar(v: ValoresRegistro) {
    if (!registro) return;
    setSalvando(true);
    setErroGeral('');
    try {
      const documentoId = await enviarDocumento(pacienteId, v.documento ?? [], docEnviado);
      await api.put(`/pacientes/${pacienteId}/prontuario/${registro.id}`, {
        observacao: v.observacao,
        autorizacao: {
          solicitadoPorProfissionalId: v.solicitadoPorProfissionalId,
          autorizadoPor: v.autorizadoPor,
          observacao: v.observacaoAutorizacao,
          documentoId
        }
      });
      message.success('Registro alterado. O texto anterior ficou no histórico.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro) {
        if (!aplicarErros(e, form)) setErroGeral(e.message);
      } else setErroGeral('Não foi possível alterar o registro.');
    } finally {
      setSalvando(false);
    }
  }

  async function salvar() {
    let v: ValoresRegistro;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    modal.confirm({
      title: 'Alterar o registro?',
      content: 'O texto anterior fica guardado no histórico de correções, com a autorização, o seu nome e a data e hora.',
      okText: 'Alterar registro',
      cancelText: 'Voltar',
      onOk: () => enviar(v)
    });
  }

  return (
    <Modal
      title="Editar registro do prontuário"
      open={!!registro}
      width={720}
      onCancel={aoFechar}
      onOk={() => void salvar()}
      okText="Alterar registro"
      cancelText="Cancelar"
      confirmLoading={salvando}
      maskClosable={false}
      destroyOnClose
    >
      <Form form={form} layout="vertical" requiredMark>
        {erroGeral && <Alert type="error" showIcon message={erroGeral} style={{ marginBottom: 16 }} />}
        <Form.Item name="observacao" label="Texto do registro" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
          <Input.TextArea rows={10} maxLength={20000} showCount />
        </Form.Item>
        <CamposAutorizacao profissionais={profissionais} />
      </Form>
    </Modal>
  );
}

/** Histórico de alterações de um registro (restrito a quem tem prontuario.correcao:visualizar). */
function HistoricoModal({ registro, pacienteId, aoFechar }: { registro: RegistroProntuario | null; pacienteId: string; aoFechar: () => void }) {
  const [itens, setItens] = useState<CorrecaoProntuario[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [erro, setErro] = useState('');

  useEffect(() => {
    if (!registro) return;
    setCarregando(true);
    setErro('');
    api
      .get<CorrecaoProntuario[]>(`/pacientes/${pacienteId}/prontuario/${registro.id}/correcoes`)
      .then(setItens)
      .catch((e) => setErro(e instanceof ApiErro ? e.message : 'Não foi possível carregar o histórico.'))
      .finally(() => setCarregando(false));
  }, [registro, pacienteId]);

  return (
    <Modal title="Histórico de alterações" open={!!registro} width={760} onCancel={aoFechar} footer={<Button onClick={aoFechar}>Fechar</Button>} destroyOnClose>
      {erro && <Alert type="error" showIcon message={erro} />}
      <Spin spinning={carregando}>
        {!carregando && !erro && itens.length === 0 && <Empty description="Nenhuma alteração." />}
        <Timeline
          items={itens.map((c) => ({
            children: (
              <div>
                <Space wrap>
                  <strong>{formatarDataHora(c.corrigidoEm)}</strong>
                  <Tag color={c.origem === 'SISTEMA' ? 'blue' : 'purple'}>{c.origem === 'SISTEMA' ? 'No sistema' : 'No banco (TI)'}</Tag>
                  <span>por {c.executadoPor}</span>
                </Space>
                <div style={{ marginTop: 4, color: 'var(--vbs-texto-medio)' }}>
                  Solicitado por {c.solicitadoPor || '—'} · Autorizado por {c.autorizadoPor}
                </div>
                <div style={{ color: 'var(--vbs-texto-medio)' }}>Observação: {c.observacaoAutorizacao}</div>
                <div style={{ color: 'var(--vbs-texto-medio)', marginBottom: 4 }}>
                  Documento: {c.documento ? <BotaoDocumento pacienteId={pacienteId} documento={c.documento} /> : 'não anexado (correção feita direto no banco)'}
                </div>
                <Typography.Text type="secondary">Texto anterior</Typography.Text>
                <div style={{ whiteSpace: 'pre-wrap', background: 'var(--vbs-fundo-remocao)', padding: 8, borderRadius: 6, marginBottom: 6 }}>{c.observacaoAnterior}</div>
                <Typography.Text type="secondary">Texto novo</Typography.Text>
                <div style={{ whiteSpace: 'pre-wrap', background: 'var(--vbs-fundo-inclusao)', padding: 8, borderRadius: 6 }}>{c.observacaoNova}</div>
              </div>
            )
          }))}
        />
      </Spin>
    </Modal>
  );
}

export default function ProntuarioPagina() {
  const { pacienteId = '' } = useParams<{ pacienteId: string }>();
  const navegar = useNavigate();
  const [busca] = useSearchParams();
  const consultaId = busca.get('consultaId') ?? undefined;
  const profissionalInicial = busca.get('profissionalId') ? Number(busca.get('profissionalId')) : undefined;
  const { sessao, pode } = useAuth();
  const administrador = !!sessao?.administrador;
  const podeIncluir = pode('prontuario.registro', 'incluir');
  const podeEditar = administrador && pode('prontuario.registro', 'editar');
  const podeHistorico = pode('prontuario.correcao', 'visualizar');
  const podeTransferir = pode('prontuario.transferencia', 'editar');

  const [paciente, setPaciente] = useState<Paciente | null>(null);
  const [dados, setDados] = useState<Pagina<RegistroProntuario>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState('');
  const [pagina, setPagina] = useState(1);
  const [periodo, setPeriodo] = useState<[Dayjs | null, Dayjs | null] | null>(null);
  const [profissionalId, setProfissionalId] = useState<number>();
  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);
  // Vindo da Agenda (consultaId no endereço), o novo registro já abre vinculado à consulta.
  const [novoAberto, setNovoAberto] = useState(!!consultaId);
  const [editando, setEditando] = useState<RegistroProntuario | null>(null);
  const [historico, setHistorico] = useState<RegistroProntuario | null>(null);
  const [transferindo, setTransferindo] = useState(false);

  useEffect(() => {
    api.get<Paciente>(`/pacientes/${pacienteId}`).then(setPaciente).catch(() => setPaciente(null));
    api.get<ItemApoio[]>('/apoio/profissionais', { incluirInativos: true }).then(setProfissionais).catch(() => setProfissionais([]));
  }, [pacienteId]);

  const carregar = useCallback(async () => {
    setCarregando(true);
    setErro('');
    try {
      const r = await api.get<Pagina<RegistroProntuario>>(`/pacientes/${pacienteId}/prontuario`, {
        pagina,
        tamanho: TAMANHO,
        dataInicial: periodo?.[0]?.format('YYYY-MM-DD'),
        dataFinal: periodo?.[1]?.format('YYYY-MM-DD'),
        profissionalId
      });
      setDados(r);
    } catch (e) {
      setDados({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
      setErro(e instanceof ApiErro ? e.message : 'Não foi possível carregar o prontuário.');
    } finally {
      setCarregando(false);
    }
  }, [pacienteId, pagina, periodo, profissionalId]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Space>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navegar('/pacientes')}>
            Pacientes
          </Button>
          <CabecalhoPagina icone={<IconeTela />} titulo={`Prontuário${paciente ? ` — ${paciente.nome}` : ''}`} subtitulo="Registros clínicos do paciente" />
          {paciente && !paciente.ativo && <Tag>Paciente inativo</Tag>}
          {paciente && <Tag color="green">Responsável: {paciente.profissionalResponsavel.nome}</Tag>}
        </Space>
        <Space>
        {podeTransferir && !erro && paciente && (
          <Button icon={<SwapOutlined />} onClick={() => setTransferindo(true)}>
            Transferência de prontuário
          </Button>
        )}
        {podeIncluir && !erro && (
          <Button type="primary" icon={<PlusOutlined />} disabled={paciente ? !paciente.ativo : false} onClick={() => setNovoAberto(true)}>
            Novo registro
          </Button>
        )}
        </Space>
      </Space>

      {erro ? (
        <Alert type="warning" showIcon message="Acesso ao prontuário" description={erro} />
      ) : (
        <>
          <Space style={{ marginBottom: 16 }} wrap>
            <DatePicker.RangePicker
              format="DD/MM/YYYY"
              placeholder={['Data inicial', 'Data final']}
              value={periodo as never}
              onChange={(v) => {
                setPeriodo(v as [Dayjs | null, Dayjs | null] | null);
                setPagina(1);
              }}
            />
            <Select<number>
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Profissional que registrou"
              style={{ width: 240 }}
              value={profissionalId}
              options={profissionais.map((p) => ({ value: p.id, label: p.nome }))}
              onChange={(v) => {
                setProfissionalId(v);
                setPagina(1);
              }}
            />
          </Space>

          <Spin spinning={carregando}>
            {!carregando && dados.itens.length === 0 && <Empty description="Nenhum registro no prontuário." />}
            <Space direction="vertical" style={{ width: '100%' }} size={12}>
              {dados.itens.map((r) => (
                <div key={r.id} style={{ border: '1px solid var(--vbs-borda-suave)', borderRadius: 8, padding: 16, background: 'var(--vbs-superficie)' }}>
                  <Space style={{ width: '100%', justifyContent: 'space-between' }} align="start" wrap>
                    <div>
                      <strong>{formatarDataHora(r.registradoEm)}</strong>
                      <span style={{ marginLeft: 12 }}>
                        {r.profissional.nome} — {r.profissional.registroProfissional} {r.profissional.numeroConselho}/{r.profissional.ufConselho}
                      </span>
                    </div>
                    <Space>
                      {r.inclusaoAdministrativa && (
                        <Tag color="gold" title={`Solicitado por ${r.inclusaoAdministrativa.solicitadoPor}; autorizado por ${r.inclusaoAdministrativa.autorizadoPor}`}>
                          Incluído por {r.inclusaoAdministrativa.incluidoPor.nomeCompleto}
                        </Tag>
                      )}
                      {podeHistorico && r.inclusaoAdministrativa?.documento && (
                        <BotaoDocumento pacienteId={pacienteId} documento={r.inclusaoAdministrativa.documento} />
                      )}
                      {r.corrigido && <Tag color="orange">Corrigido em {formatarDataHora(r.corrigidoEm)}</Tag>}
                      {podeHistorico && r.corrigido && (
                        <Button size="small" icon={<HistoryOutlined />} onClick={() => setHistorico(r)}>
                          Histórico
                        </Button>
                      )}
                      {podeEditar && (
                        <Button size="small" icon={<EditOutlined />} onClick={() => setEditando(r)}>
                          Editar
                        </Button>
                      )}
                    </Space>
                  </Space>
                  <div style={{ whiteSpace: 'pre-wrap', marginTop: 10 }}>{r.observacao}</div>
                </div>
              ))}
            </Space>
          </Spin>

          {dados.total > TAMANHO && (
            <Pagination
              style={{ marginTop: 16, textAlign: 'right' }}
              current={pagina}
              pageSize={TAMANHO}
              total={dados.total}
              showSizeChanger={false}
              onChange={setPagina}
            />
          )}
        </>
      )}

      <NovoRegistroModal
        aberto={novoAberto}
        pacienteId={pacienteId}
        administrador={administrador}
        profissionais={profissionais}
        consultaId={consultaId}
        profissionalInicial={profissionalInicial}
        aoFechar={() => setNovoAberto(false)}
        aoSalvar={() => {
          setNovoAberto(false);
          setPagina(1);
          void carregar();
        }}
      />
      <EditarRegistroModal
        registro={editando}
        pacienteId={pacienteId}
        profissionais={profissionais}
        aoFechar={() => setEditando(null)}
        aoSalvar={() => {
          setEditando(null);
          void carregar();
        }}
      />
      {paciente && (
        <TransferenciaProntuarioModal
          aberto={transferindo}
          pacienteId={pacienteId}
          pacienteNome={paciente.nome}
          profissionalAtual={paciente.profissionalResponsavel}
          aoFechar={() => setTransferindo(false)}
          aoTransferir={() => {
            setTransferindo(false);
            // Quem não é administrador perde o acesso na hora (RN16): volta para a lista.
            if (!administrador) navegar('/pacientes');
            else {
              api.get<Paciente>(`/pacientes/${pacienteId}`).then(setPaciente).catch(() => undefined);
              void carregar();
            }
          }}
        />
      )}
      <HistoricoModal registro={historico} pacienteId={pacienteId} aoFechar={() => setHistorico(null)} />
    </>
  );
}
