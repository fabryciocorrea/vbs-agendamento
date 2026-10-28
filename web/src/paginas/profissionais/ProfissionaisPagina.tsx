import { useCallback, useEffect, useState } from "react";
import {
  App,
  Button,
  Input,
  Select,
  Space,
  Table,
  Tag,
  Typography,
} from "antd";
import type { TableColumnsType } from "antd";
import {
  ClockCircleOutlined,
  EditOutlined,
  PlusOutlined,
  PoweroffOutlined,
} from "@ant-design/icons";
import { api, ApiErro } from "../../api/cliente";
import type {
  ItemApoio,
  Pagina,
  Profissional,
  ProfissionalResumo,
} from "../../api/tipos";
import { useAuth } from "../../auth/AuthContext";
import FormularioProfissional from "./FormularioProfissional";
import ExpedienteModal from "./ExpedienteModal";

const TAMANHO = 10;

type FiltroSituacao = "todos" | "ativos" | "inativos";

export default function ProfissionaisPagina() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode("profissional.cadastro", "incluir");
  const podeEditar = pode("profissional.cadastro", "editar");

  const [dados, setDados] = useState<Pagina<ProfissionalResumo>>({
    itens: [],
    pagina: 1,
    tamanho: TAMANHO,
    total: 0,
  });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [busca, setBusca] = useState("");
  const [buscaAplicada, setBuscaAplicada] = useState("");
  const [situacao, setSituacao] = useState<FiltroSituacao>("todos");
  const [especialidadeId, setEspecialidadeId] = useState<number | undefined>();
  const [unidadeId, setUnidadeId] = useState<number | undefined>();
  const [especialidades, setEspecialidades] = useState<ItemApoio[]>([]);
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);

  const [formAberto, setFormAberto] = useState(false);
  const [emEdicao, setEmEdicao] = useState<Profissional | null>(null);
  const [expediente, setExpediente] = useState<ProfissionalResumo | null>(null);

  // Listas dos filtros (incluem inativos para conseguir filtrar cadastros antigos).
  useEffect(() => {
    Promise.all([
      api.get<ItemApoio[]>("/apoio/especialidades", { incluirInativos: true }),
      api.get<ItemApoio[]>("/apoio/unidades", { incluirInativos: true }),
    ])
      .then(([e, u]) => {
        setEspecialidades(e);
        setUnidades(u);
      })
      .catch(() => undefined); // sem a liberação de listas de apoio, os filtros ficam vazios
  }, []);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<ProfissionalResumo>>("/profissionais", {
          busca: buscaAplicada,
          especialidadeId,
          unidadeId,
          ativo: situacao === "todos" ? undefined : situacao === "ativos",
          pagina,
          tamanho: TAMANHO,
        }),
      );
    } catch (e) {
      message.error(
        e instanceof ApiErro
          ? e.message
          : "Não foi possível carregar os profissionais.",
      );
    } finally {
      setCarregando(false);
    }
  }, [buscaAplicada, especialidadeId, unidadeId, situacao, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  // A lista traz só o resumo; para editar, busca o cadastro completo.
  async function abrirEdicao(r: ProfissionalResumo) {
    try {
      setEmEdicao(await api.get<Profissional>(`/profissionais/${r.id}`));
      setFormAberto(true);
    } catch (e) {
      message.error(
        e instanceof ApiErro ? e.message : "Não foi possível abrir o cadastro.",
      );
    }
  }

  function alterarStatus(r: ProfissionalResumo) {
    const inativar = r.ativo;
    modal.confirm({
      title: inativar ? "Inativar profissional" : "Ativar profissional",
      content: `${inativar ? "Inativar" : "Ativar"} o profissional "${r.nome}"?`,
      okText: inativar ? "Inativar" : "Ativar",
      cancelText: "Cancelar",
      onOk: async () => {
        try {
          const p = await api.patch<Profissional>(
            `/profissionais/${r.id}/status`,
            { ativo: !r.ativo },
          );
          message.success(
            inativar ? "Profissional inativado." : "Profissional ativado.",
          );
          if (inativar && p.quantidadePacientes > 0)
            modal.info({
              title: "Pacientes sem profissional ativo",
              content: `${p.quantidadePacientes} paciente(s) continuam com este profissional como responsável. A recepção deve trocá-lo.`,
            });
          await carregar();
        } catch (e) {
          if (e instanceof ApiErro && e.codigo === "PROFISSIONAL_COM_CONSULTAS")
            message.warning(e.message);
          else
            message.error(
              e instanceof ApiErro ? e.message : "Erro inesperado.",
            );
        }
      },
    });
  }

  const colunas: TableColumnsType<ProfissionalResumo> = [
    { title: "Nome", dataIndex: "nome" },
    { title: "Tipo", dataIndex: "registroProfissional", width: 80 },
    { title: "Número do Conselho", dataIndex: "numeroConselho", width: 160 },
    { title: "UF do Conselho", dataIndex: "ufConselho", width: 130 },
    {
      title: "Especialidades",
      render: (_, r) =>
        r.especialidades.map((e) => <Tag key={e.id}>{e.nome}</Tag>),
    },
    {
      title: "Unidades",
      render: (_, r) => r.unidades.map((u) => <Tag key={u.id}>{u.nome}</Tag>),
    },
    {
      title: "Situação",
      dataIndex: "ativo",
      width: 110,
      render: (ativo: boolean) =>
        ativo ? <Tag color="green">Ativo</Tag> : <Tag>Inativo</Tag>,
    },
    {
      title: "Ações",
      width: 150,
      render: (_, r) =>
        podeEditar && (
          <Space>
            <Button
              size="small"
              icon={<EditOutlined />}
              title="Editar"
              onClick={() => void abrirEdicao(r)}
            />
            <Button
              size="small"
              icon={<ClockCircleOutlined />}
              title="Expediente"
              onClick={() => setExpediente(r)}
            />
            <Button
              size="small"
              danger={r.ativo}
              icon={<PoweroffOutlined />}
              title={r.ativo ? "Inativar" : "Ativar"}
              onClick={() => alterarStatus(r)}
            />
          </Space>
        ),
    },
  ];

  return (
    <>
      <Space
        style={{
          width: "100%",
          justifyContent: "space-between",
          marginBottom: 16,
        }}
        wrap
      >
        <Typography.Title level={3} style={{ margin: 0 }}>
          Profissionais
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
            Novo profissional
          </Button>
        )}
      </Space>

      <Space style={{ marginBottom: 16 }} wrap>
        <Input.Search
          placeholder="Buscar por nome ou número do conselho"
          allowClear
          style={{ width: 330 }}
          value={busca}
          onChange={(e) => {
            setBusca(e.target.value);
            if (e.target.value === "") {
              setBuscaAplicada("");
              setPagina(1);
            }
          }}
          onSearch={(v) => {
            setBuscaAplicada(v.trim());
            setPagina(1);
          }}
        />
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder="Especialidade"
          style={{ width: 200 }}
          value={especialidadeId}
          options={especialidades.map((e) => ({ value: e.id, label: e.nome }))}
          onChange={(v) => {
            setEspecialidadeId(v);
            setPagina(1);
          }}
        />
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder="Unidade"
          style={{ width: 200 }}
          value={unidadeId}
          options={unidades.map((u) => ({ value: u.id, label: u.nome }))}
          onChange={(v) => {
            setUnidadeId(v);
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
            { value: "todos", label: "Todos" },
            { value: "ativos", label: "Somente ativos" },
            { value: "inativos", label: "Somente inativos" },
          ]}
        />
      </Space>

      <Table<ProfissionalResumo>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: "max-content" }}
        locale={{ emptyText: "Nenhum profissional encontrado." }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} profissional(is)`,
          onChange: setPagina,
        }}
      />

      <FormularioProfissional
        aberto={formAberto}
        profissional={emEdicao}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />
      <ExpedienteModal
        profissional={expediente}
        aoFechar={() => setExpediente(null)}
      />
    </>
  );
}
