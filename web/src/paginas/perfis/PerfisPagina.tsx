import { useCallback, useEffect, useMemo, useState } from 'react';
import { App, Button, Select, Space, Table, Tag } from 'antd';
import type { TableColumnsType } from 'antd';
import { DeleteOutlined, EditOutlined, EyeOutlined, PlusOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { PerfilResumo } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import PerfilFormulario from './PerfilFormulario';
import { SafetyOutlined as IconeTela } from '@ant-design/icons';
import CabecalhoPagina from '../../layout/CabecalhoPagina';
import CampoBusca from '../../componentes/CampoBusca';

export default function PerfisPagina() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('perfil.cadastro', 'incluir');
  const podeEditar = pode('perfil.cadastro', 'editar');
  const podeExcluir = pode('perfil.cadastro', 'excluir');

  const [perfis, setPerfis] = useState<PerfilResumo[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [filtroNome, setFiltroNome] = useState<string | undefined>();
  const [filtroDescricao, setFiltroDescricao] = useState('');
  const [formAberto, setFormAberto] = useState(false);
  const [emEdicao, setEmEdicao] = useState<PerfilResumo | null>(null);
  const [somenteLeitura, setSomenteLeitura] = useState(false);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setPerfis(await api.get<PerfilResumo[]>('/perfis'));
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os perfis.');
    } finally {
      setCarregando(false);
    }
  }, [message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  function excluir(p: PerfilResumo) {
    modal.confirm({
      title: 'Excluir perfil',
      content: `Excluir o perfil "${p.nome}"? Esta ação não pode ser desfeita.`,
      okText: 'Excluir',
      okButtonProps: { danger: true },
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.delete(`/perfis/${p.id}`);
          message.success('Perfil excluído.');
          await carregar();
        } catch (erro) {
          if (erro instanceof ApiErro && erro.codigo === 'PERFIL_COM_USUARIOS') message.warning(erro.message);
          else message.error(erro instanceof ApiErro ? erro.message : 'Erro inesperado.');
        }
      }
    });
  }

  const opcoesNomes = useMemo(() => perfis.map((p) => ({ value: p.nome, label: p.nome })), [perfis]);
  const perfisFiltrados = useMemo(() => {
    const termo = filtroDescricao.trim().toLowerCase();
    return perfis.filter(
      (p) => (!filtroNome || p.nome === filtroNome) && (termo === '' || (p.descricao ?? '').toLowerCase().includes(termo))
    );
  }, [perfis, filtroNome, filtroDescricao]);

  const colunas: TableColumnsType<PerfilResumo> = [
    {
      title: 'Nome',
      dataIndex: 'nome',
      render: (nome: string, p) => (
        <>
          {nome} {p.protegido && <Tag color="gold">Protegido</Tag>}
        </>
      )
    },
    { title: 'Descrição', dataIndex: 'descricao' },
    { title: 'Usuários', dataIndex: 'quantidadeUsuarios', width: 100, align: 'center' },
    {
      title: 'Ações',
      width: 130,
      render: (_, p) => (
        <Space>
          <Button
            size="small"
            icon={<EyeOutlined />}
            title="Visualizar"
            onClick={() => {
              setEmEdicao(p);
              setSomenteLeitura(true);
              setFormAberto(true);
            }}
          />
          {podeEditar && !p.protegido && (
            <Button
              size="small"
              icon={<EditOutlined />}
              title="Editar"
              onClick={() => {
                setEmEdicao(p);
                setSomenteLeitura(false);
                setFormAberto(true);
              }}
            />
          )}
          {podeExcluir && !p.protegido && (
            <Button size="small" danger icon={<DeleteOutlined />} title="Excluir" onClick={() => excluir(p)} />
          )}
        </Space>
      )
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <CabecalhoPagina icone={<IconeTela />} titulo="Perfis de acesso" subtitulo="Permissões de cada perfil, tela por tela" />
        {podeIncluir && (
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => {
              setEmEdicao(null);
              setSomenteLeitura(false);
              setFormAberto(true);
            }}
          >
            Novo perfil
          </Button>
        )}
      </Space>

      <Space style={{ marginBottom: 16 }} wrap>
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder="Nome"
          style={{ width: 240 }}
          options={opcoesNomes}
          value={filtroNome}
          onChange={(v) => setFiltroNome(v)}
        />
        <CampoBusca
          placeholder="Descrição"
          allowClear
          style={{ width: 280 }}
          value={filtroDescricao}
          onChange={(e) => setFiltroDescricao(e.target.value)}
          onSearch={(v) => setFiltroDescricao(v)}
        />
      </Space>

      <Table<PerfilResumo>
        rowKey="id"
        columns={colunas}
        dataSource={perfisFiltrados}
        loading={carregando}
        pagination={false}
        locale={{ emptyText: perfis.length === 0 ? 'Nenhum perfil cadastrado.' : 'Nenhum perfil encontrado com os filtros.' }}
      />

      <PerfilFormulario
        aberto={formAberto}
        somenteLeitura={somenteLeitura}
        perfil={emEdicao}
        aoFechar={() => setFormAberto(false)}
        aoSalvar={() => {
          setFormAberto(false);
          void carregar();
        }}
      />
    </>
  );
}
