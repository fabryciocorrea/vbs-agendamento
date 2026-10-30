import { useCallback, useEffect, useState } from 'react';
import { App, Button, Input, Select, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { EditOutlined, KeyOutlined, PlusOutlined, PoweroffOutlined, UnlockOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { Pagina, PerfilResumo, Usuario } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import BotaoExcluir from '../../componentes/BotaoExcluir';
import UsuarioFormulario from './UsuarioFormulario';
import SenhaModal from './SenhaModal';

const TAMANHO = 10;

type FiltroSituacao = 'todos' | 'ativos' | 'inativos';

function formatarDataHora(iso?: string | null): string {
  if (!iso) return 'Nunca';
  return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

export default function UsuariosPagina() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('usuario.cadastro', 'incluir');
  const podeEditar = pode('usuario.cadastro', 'editar');

  const [dados, setDados] = useState<Pagina<Usuario>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [perfilId, setPerfilId] = useState<number | undefined>(undefined);
  const [situacao, setSituacao] = useState<FiltroSituacao>('todos');
  const [perfis, setPerfis] = useState<PerfilResumo[]>([]);
  const [formAberto, setFormAberto] = useState(false);
  const [emEdicao, setEmEdicao] = useState<Usuario | null>(null);
  const [paraSenha, setParaSenha] = useState<Usuario | null>(null);

  useEffect(() => {
    api
      .get<PerfilResumo[]>('/perfis')
      .then(setPerfis)
      .catch(() => undefined); // o filtro por perfil fica vazio se a lista não puder ser carregada
  }, []);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<Usuario>>('/usuarios', {
          busca: buscaAplicada,
          perfilId,
          ativo: situacao === 'todos' ? undefined : situacao === 'ativos',
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os usuários.');
    } finally {
      setCarregando(false);
    }
  }, [buscaAplicada, perfilId, situacao, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  function alterarStatus(u: Usuario) {
    const inativar = u.ativo;
    modal.confirm({
      title: inativar ? 'Inativar usuário' : 'Ativar usuário',
      content: inativar
        ? `Inativar o usuário "${u.nomeCompleto}"? As sessões abertas dele serão encerradas.`
        : `Ativar o usuário "${u.nomeCompleto}"?`,
      okText: inativar ? 'Inativar' : 'Ativar',
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.patch(`/usuarios/${u.id}/status`, { ativo: !u.ativo });
          message.success(inativar ? 'Usuário inativado.' : 'Usuário ativado.');
          await carregar();
        } catch (erro) {
          if (erro instanceof ApiErro && (erro.codigo === 'USUARIO_PROPRIO' || erro.codigo === 'ULTIMO_ADMINISTRADOR')) message.warning(erro.message);
          else message.error(erro instanceof ApiErro ? erro.message : 'Erro inesperado.');
        }
      }
    });
  }

  function desbloquear(u: Usuario) {
    modal.confirm({
      title: 'Desbloquear usuário',
      content: `Desbloquear o usuário "${u.nomeCompleto}"? Ele poderá entrar de novo com a senha atual.`,
      okText: 'Desbloquear',
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.post(`/usuarios/${u.id}/desbloquear`);
          message.success('Usuário desbloqueado.');
          await carregar();
        } catch (erro) {
          message.error(erro instanceof ApiErro ? erro.message : 'Erro inesperado.');
        }
      }
    });
  }

  const colunas: TableColumnsType<Usuario> = [
    { title: 'Nome', dataIndex: 'nomeCompleto' },
    { title: 'Usuário', dataIndex: 'nomeUsuario', width: 170 },
    { title: 'E-mail', dataIndex: 'email' },
    { title: 'Perfil', width: 150, render: (_, u) => u.perfil.nome },
    {
      title: 'Situação',
      width: 130,
      render: (_, u) => (
        <Space size={4} wrap>
          {u.ativo ? <Tag color="green">Ativo</Tag> : <Tag>Inativo</Tag>}
          {u.bloqueado && <Tag color="red">Bloqueado</Tag>}
        </Space>
      )
    },
    { title: 'Último acesso', width: 140, render: (_, u) => formatarDataHora(u.ultimoLoginEm) },
    {
      title: 'Ações',
      width: 210,
      render: (_, u) =>
        podeEditar && (
          <Space>
            <Button
              size="small"
              icon={<EditOutlined />}
              title="Editar"
              onClick={() => {
                setEmEdicao(u);
                setFormAberto(true);
              }}
            />
            <Button size="small" icon={<KeyOutlined />} title="Definir nova senha" onClick={() => setParaSenha(u)} />
            {u.bloqueado && <Button size="small" icon={<UnlockOutlined />} title="Desbloquear" onClick={() => desbloquear(u)} />}
            <Button
              size="small"
              danger={u.ativo}
              icon={<PoweroffOutlined />}
              title={u.ativo ? 'Inativar' : 'Ativar'}
              onClick={() => alterarStatus(u)}
            />
            <BotaoExcluir artigoTipo="o usuário" nome={u.nomeCompleto} caminho={`/usuarios/${u.id}`} aoExcluir={carregar} />
          </Space>
        )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Usuários
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
            Novo usuário
          </Button>
        )}
      </Space>

      <Space style={{ marginBottom: 16 }} wrap>
        <Input.Search
          placeholder="Buscar por nome, usuário ou e-mail"
          allowClear
          style={{ width: 320 }}
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
        <Select<number>
          allowClear
          placeholder="Perfil"
          style={{ width: 200 }}
          value={perfilId}
          options={perfis.map((p) => ({ value: p.id, label: p.nome }))}
          onChange={(v) => {
            setPerfilId(v);
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

      <Table<Usuario>
        rowKey="id"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        locale={{ emptyText: 'Nenhum usuário encontrado.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} usuário(s)`,
          onChange: setPagina
        }}
      />

      <UsuarioFormulario
        aberto={formAberto}
        usuario={emEdicao}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />
      <SenhaModal usuario={paraSenha} aoFechar={() => setParaSenha(null)} />
    </>
  );
}
