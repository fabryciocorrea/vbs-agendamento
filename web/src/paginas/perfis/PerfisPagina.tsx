import { useCallback, useEffect, useState } from 'react';
import { App, Button, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { DeleteOutlined, EditOutlined, EyeOutlined, PlusOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { PerfilResumo } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import PerfilFormulario from './PerfilFormulario';

export default function PerfisPagina() {
  const { message, modal } = App.useApp();
  const { pode } = useAuth();
  const podeIncluir = pode('perfil.cadastro', 'incluir');
  const podeEditar = pode('perfil.cadastro', 'editar');
  const podeExcluir = pode('perfil.cadastro', 'excluir');

  const [perfis, setPerfis] = useState<PerfilResumo[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [formAberto, setFormAberto] = useState(false);
  const [emEdicao, setEmEdicao] = useState<PerfilResumo | null>(null);

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
          {p.protegido || !podeEditar ? (
            <Button
              size="small"
              icon={<EyeOutlined />}
              title="Consultar"
              onClick={() => {
                setEmEdicao(p);
                setFormAberto(true);
              }}
            />
          ) : (
            <Button
              size="small"
              icon={<EditOutlined />}
              title="Editar"
              onClick={() => {
                setEmEdicao(p);
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
        <Typography.Title level={3} style={{ margin: 0 }}>
          Perfis de acesso
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
            Novo perfil
          </Button>
        )}
      </Space>

      <Table<PerfilResumo>
        rowKey="id"
        columns={colunas}
        dataSource={perfis}
        loading={carregando}
        pagination={false}
        locale={{ emptyText: 'Nenhum perfil cadastrado.' }}
      />

      <PerfilFormulario
        aberto={formAberto}
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
