import { useCallback, useEffect, useState } from 'react';
import { Alert, App, Button, Empty, Select, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio, Pagina } from '../../api/tipos';
import { formatarDataHora } from '../../util/formatos';
import type { ConsultaResumo } from '../agenda/tipos';

const TAMANHO = 20;

/** Consultas futuras por convênio cujo credenciamento deixou de valer (J4). Nada é cancelado sozinho: a recepção entra em contato. */
export default function ConsultasSemCredenciamentoPagina() {
  const { message } = App.useApp();
  const [dados, setDados] = useState<Pagina<ConsultaResumo> | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [erro, setErro] = useState('');
  const [pagina, setPagina] = useState(1);
  const [unidadeId, setUnidadeId] = useState<number>();
  const [convenioId, setConvenioId] = useState<number>();
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [convenios, setConvenios] = useState<ItemApoio[]>([]);

  useEffect(() => {
    api.get<ItemApoio[]>('/apoio/unidades').then(setUnidades).catch(() => setUnidades([]));
    api.get<ItemApoio[]>('/apoio/convenios').then(setConvenios).catch(() => setConvenios([]));
  }, []);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(await api.get<Pagina<ConsultaResumo>>('/consultas/sem-credenciamento', { unidadeId, convenioId, pagina, tamanho: TAMANHO }));
      setErro('');
    } catch (e) {
      const texto = e instanceof ApiErro ? e.message : 'Não foi possível carregar as consultas.';
      setErro(texto);
      message.error(texto);
    } finally {
      setCarregando(false);
    }
  }, [unidadeId, convenioId, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  const colunas: TableColumnsType<ConsultaResumo> = [
    { title: 'Data e hora', width: 150, render: (_, c) => formatarDataHora(c.inicio) },
    { title: 'Paciente', render: (_, c) => c.paciente.nome },
    { title: 'Convênio', render: (_, c) => c.convenio?.nome ?? '—' },
    { title: 'Unidade', render: (_, c) => c.unidade.nome },
    { title: 'Especialidade', render: (_, c) => c.especialidade.nome },
    { title: 'Profissional', render: (_, c) => c.profissional.nome },
    { title: 'Situação', width: 120, render: (_, c) => <Tag color={c.status === 'CONFIRMADA' ? 'green' : 'blue'}>{c.status === 'CONFIRMADA' ? 'Confirmada' : 'Agendada'}</Tag> }
  ];

  return (
    <>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        Consultas sem credenciamento
      </Typography.Title>
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        message="Nada foi cancelado."
        description="São consultas futuras, agendadas ou confirmadas, de convênios que deixaram de ter credenciamento aprovado e vigente na unidade e especialidade. Entre em contato com o paciente e remarque, troque para particular ou peça a liberação ao administrador."
      />
      <Space wrap style={{ marginBottom: 16 }}>
        <Select<number>
          allowClear
          showSearch
          optionFilterProp="label"
          style={{ width: 220 }}
          placeholder="Todas as unidades"
          value={unidadeId}
          onChange={(v) => {
            setPagina(1);
            setUnidadeId(v);
          }}
          options={unidades.map((x) => ({ value: x.id, label: x.nome }))}
        />
        <Select<number>
          allowClear
          showSearch
          optionFilterProp="label"
          style={{ width: 220 }}
          placeholder="Todos os convênios"
          value={convenioId}
          onChange={(v) => {
            setPagina(1);
            setConvenioId(v);
          }}
          options={convenios.map((x) => ({ value: x.id, label: x.nome }))}
        />
        <Button icon={<ReloadOutlined />} onClick={() => void carregar()} loading={carregando} title="Atualizar" />
      </Space>
      {erro && <Alert type="warning" showIcon message={erro} style={{ marginBottom: 16 }} />}
      <Table
        rowKey="id"
        size="small"
        loading={carregando}
        columns={colunas}
        dataSource={dados?.itens ?? []}
        scroll={{ x: 'max-content' }}
        pagination={{ current: pagina, pageSize: TAMANHO, total: dados?.total ?? 0, showSizeChanger: false, onChange: setPagina }}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhuma consulta futura sem credenciamento." /> }}
      />
    </>
  );
}
