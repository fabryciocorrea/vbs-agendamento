import { useEffect, useMemo, useState } from 'react';
import { Alert, App, Button, Checkbox, Form, Input, Modal, Space, Spin, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import type { Perfil, PerfilComAvisos, PerfilResumo, TelaSistema } from '../../api/tipos';

const OBRIGATORIO = 'Campo Não Preenchido';

interface ValoresFormulario {
  nome: string;
  descricao?: string;
}

/** v = visualizar, i = incluir, e = editar, c = cancelar, x = excluir, p = exportar. */
interface MarcasTela {
  v: boolean;
  i: boolean;
  e: boolean;
  c: boolean;
  x: boolean;
  p: boolean;
}

interface MarcasCampo {
  v: boolean;
  e: boolean;
}

type AcaoTela = keyof MarcasTela;

const ACOES: { chave: AcaoTela; titulo: string }[] = [
  { chave: 'v', titulo: 'Visualizar' },
  { chave: 'i', titulo: 'Incluir' },
  { chave: 'e', titulo: 'Editar' },
  { chave: 'c', titulo: 'Cancelar' },
  { chave: 'x', titulo: 'Excluir' },
  { chave: 'p', titulo: 'Exportar' }
];

const SEM_MARCAS: MarcasTela = { v: false, i: false, e: false, c: false, x: false, p: false };

interface Props {
  aberto: boolean;
  perfil: PerfilResumo | null;
  aoFechar: () => void;
  aoSalvar: () => void;
}

export default function PerfilFormulario({ aberto, perfil, aoFechar, aoSalvar }: Props) {
  const { message, modal } = App.useApp();
  const [form] = Form.useForm<ValoresFormulario>();
  const [carregando, setCarregando] = useState(false);
  const [salvando, setSalvando] = useState(false);
  const [telas, setTelas] = useState<TelaSistema[]>([]);
  const [marcasTelas, setMarcasTelas] = useState<Record<number, MarcasTela>>({});
  const [marcasCampos, setMarcasCampos] = useState<Record<number, MarcasCampo>>({});
  const [protegido, setProtegido] = useState(false);

  // Ao abrir, carrega o catálogo de telas e, na edição, as liberações atuais do perfil.
  useEffect(() => {
    if (!aberto) return;
    let cancelado = false;
    setCarregando(true);
    Promise.all([api.get<TelaSistema[]>('/telas'), perfil ? api.get<Perfil>(`/perfis/${perfil.id}`) : Promise.resolve(null)])
      .then(([catalogo, atual]) => {
        if (cancelado) return;
        setTelas(catalogo);
        const t: Record<number, MarcasTela> = {};
        const c: Record<number, MarcasCampo> = {};
        for (const x of atual?.permissoes.telas ?? []) {
          t[x.telaId] = { v: x.podeVisualizar, i: x.podeIncluir, e: x.podeEditar, c: x.podeCancelar ?? false, x: x.podeExcluir, p: x.podeExportar };
        }
        for (const x of atual?.permissoes.campos ?? []) c[x.campoId] = { v: x.podeVisualizar, e: x.podeEditar };
        setMarcasTelas(t);
        setMarcasCampos(c);
        setProtegido(atual?.protegido ?? false);
        form.setFieldsValue({ nome: atual?.nome ?? '', descricao: atual?.descricao ?? '' });
      })
      .catch((e) => {
        if (cancelado) return;
        message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar as telas do sistema.');
        aoFechar();
      })
      .finally(() => {
        if (!cancelado) setCarregando(false);
      });
    return () => {
      cancelado = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aberto, perfil]);

  function marcasDe(telaId: number): MarcasTela {
    return marcasTelas[telaId] ?? SEM_MARCAS;
  }

  /** Visualizar é a base: marcar qualquer ação marca visualizar; desmarcar visualizar limpa as demais. */
  function alterarTela(telaId: number, acao: AcaoTela, marcado: boolean) {
    setMarcasTelas((antes) => {
      const atual = antes[telaId] ?? SEM_MARCAS;
      let novo: MarcasTela = { ...atual, [acao]: marcado };
      if (marcado && acao !== 'v') novo.v = true;
      if (!marcado && acao === 'v') novo = { ...SEM_MARCAS };
      return { ...antes, [telaId]: novo };
    });
  }

  function alterarCampo(campoId: number, acao: keyof MarcasCampo, marcado: boolean) {
    setMarcasCampos((antes) => {
      const atual = antes[campoId] ?? { v: false, e: false };
      let novo: MarcasCampo = { ...atual, [acao]: marcado };
      if (marcado && acao === 'e') novo.v = true;
      if (!marcado && acao === 'v') novo = { v: false, e: false };
      return { ...antes, [campoId]: novo };
    });
  }

  function marcarTudo(marcado: boolean) {
    const t: Record<number, MarcasTela> = {};
    for (const tela of telas) t[tela.id] = marcado ? { v: true, i: true, e: true, c: true, x: true, p: true } : { ...SEM_MARCAS };
    setMarcasTelas(t);
    const c: Record<number, MarcasCampo> = {};
    for (const tela of telas) for (const campo of tela.campos) c[campo.id] = marcado ? { v: true, e: true } : { v: false, e: false };
    setMarcasCampos(c);
  }

  const corpo = useMemo(
    () => ({
      telas: telas.map((t) => {
        const m = marcasDe(t.id);
        return { telaId: t.id, podeVisualizar: m.v, podeIncluir: m.i, podeEditar: m.e, podeCancelar: m.c, podeExcluir: m.x, podeExportar: m.p };
      }),
      campos: telas.flatMap((t) =>
        t.campos.map((c) => {
          const m = marcasCampos[c.id] ?? { v: false, e: false };
          return { campoId: c.id, podeVisualizar: m.v, podeEditar: m.e };
        })
      )
    }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [telas, marcasTelas, marcasCampos]
  );

  async function enviar() {
    let v: ValoresFormulario;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    const entrada = {
      nome: v.nome.trim(),
      descricao: v.descricao && v.descricao.trim() !== '' ? v.descricao.trim() : null,
      permissoes: corpo
    };
    setSalvando(true);
    try {
      const salvo = perfil
        ? await api.put<PerfilComAvisos>(`/perfis/${perfil.id}`, entrada)
        : await api.post<PerfilComAvisos>('/perfis', entrada);
      message.success(perfil ? 'Perfil atualizado.' : 'Perfil cadastrado.');
      if (salvo.avisos.length > 0) {
        modal.warning({
          title: 'Perfil salvo com avisos',
          width: 640,
          content: (
            <ul style={{ paddingLeft: 18 }}>
              {salvo.avisos.map((a) => (
                <li key={`${a.telaId}-${a.campoId}`}>{a.mensagem}</li>
              ))}
            </ul>
          )
        });
      }
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.codigo === 'PERFIL_DUPLICADO') {
        form.setFields([{ name: 'nome', errors: [e.message] }]);
      } else if (e instanceof ApiErro && e.erros.length > 0) {
        const paraCampos = e.erros.filter((x) => x.campo === 'nome' || x.campo === 'descricao');
        const outros = e.erros.filter((x) => x.campo !== 'nome' && x.campo !== 'descricao');
        if (paraCampos.length > 0) form.setFields(paraCampos.map((x) => ({ name: x.campo as keyof ValoresFormulario, errors: [x.mensagem] })));
        if (outros.length > 0) message.error(outros.map((x) => x.mensagem).join(' '));
      } else {
        message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      }
    } finally {
      setSalvando(false);
    }
  }

  const colunas: TableColumnsType<TelaSistema> = [
    {
      title: 'Tela',
      render: (_, t) => (
        <span>{t.nome}</span>
      )
    },
    { title: 'Módulo', dataIndex: 'modulo', width: 130, render: (m: string) => <Tag>{m}</Tag> },
    ...ACOES.map((a) => ({
      title: (
        <span>{a.titulo}</span>
      ),
      width: 90,
      align: 'center' as const,
      render: (_: unknown, t: TelaSistema) => (
        <Checkbox
          checked={protegido ? true : marcasDe(t.id)[a.chave]}
          disabled={protegido}
          onChange={(e) => alterarTela(t.id, a.chave, e.target.checked)}
        />
      )
    }))
  ];

  const titulo = protegido ? 'Perfil Administrador (somente consulta)' : perfil ? 'Editar perfil de acesso' : 'Novo perfil de acesso';

  return (
    <Modal
      open={aberto}
      title={titulo}
      width={1000}
      okText="Salvar"
      cancelText={protegido ? 'Fechar' : 'Cancelar'}
      okButtonProps={{ style: protegido ? { display: 'none' } : undefined }}
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
    >
      <Spin spinning={carregando}>
        {protegido && (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 16 }}
            message="O perfil Administrador é protegido: tem acesso a todas as telas e não pode ser alterado nem excluído."
          />
        )}
        <Form<ValoresFormulario> form={form} layout="vertical" requiredMark preserve={false} disabled={protegido}>
          <Form.Item name="nome" label="Nome do perfil" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
            <Input maxLength={60} />
          </Form.Item>
          <Form.Item name="descricao" label="Descrição">
            <Input.TextArea maxLength={250} rows={2} showCount />
          </Form.Item>
        </Form>

        <Space style={{ marginBottom: 8, width: '100%', justifyContent: 'space-between' }} wrap>
          <Typography.Text strong>Liberações por tela</Typography.Text>
          {!protegido && (
            <Space>
              <Button size="small" onClick={() => marcarTudo(true)}>
                Marcar tudo
              </Button>
              <Button size="small" onClick={() => marcarTudo(false)}>
                Limpar tudo
              </Button>
            </Space>
          )}
        </Space>
        <Typography.Paragraph type="secondary" style={{ fontSize: 12 }}>
          Marcar incluir, editar, cancelar, excluir ou exportar libera também visualizar. Editar altera dados; cancelar libera o cancelamento. Telas sem nenhuma marca ficam sem acesso para este perfil.
        </Typography.Paragraph>

        <Table<TelaSistema>
          rowKey="id"
          size="small"
          columns={colunas}
          dataSource={telas}
          pagination={false}
          scroll={{ y: 360 }}
          expandable={{
            // Só as telas que têm campos cadastrados no catálogo mostram a liberação campo a campo.
            rowExpandable: (t) => t.campos.length > 0,
            expandedRowRender: (t) => (
              <Table
                size="small"
                rowKey="id"
                pagination={false}
                dataSource={t.campos}
                columns={[
                  {
                    title: 'Campo',
                    render: (_, c) => (
                      <>
                        {c.nome} {c.obrigatorio && <Tag color="gold">obrigatório</Tag>}
                      </>
                    )
                  },
                  {
                    title: 'Visualizar',
                    width: 100,
                    align: 'center',
                    render: (_, c) => (
                      <Checkbox
                        checked={protegido ? true : (marcasCampos[c.id]?.v ?? false)}
                        disabled={protegido}
                        onChange={(e) => alterarCampo(c.id, 'v', e.target.checked)}
                      />
                    )
                  },
                  {
                    title: 'Editar',
                    width: 100,
                    align: 'center',
                    render: (_, c) => (
                      <Checkbox
                        checked={protegido ? true : (marcasCampos[c.id]?.e ?? false)}
                        disabled={protegido}
                        onChange={(e) => alterarCampo(c.id, 'e', e.target.checked)}
                      />
                    )
                  }
                ]}
              />
            )
          }}
        />
      </Spin>
    </Modal>
  );
}
