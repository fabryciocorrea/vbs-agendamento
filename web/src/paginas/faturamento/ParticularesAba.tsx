import { useCallback, useEffect, useState } from 'react';
import { App, Input, Select, Space, Table, Tag } from 'antd';
import type { TableColumnsType } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio, Pagina } from '../../api/tipos';
import LinkPaciente from '../../componentes/LinkPaciente';
import { formatarDataHora } from '../../util/formatos';
import { CORES_STATUS, NOMES_STATUS } from '../agenda/tipos';
import type { StatusConsulta } from '../agenda/tipos';

const TAMANHO = 10;
const REAIS = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

interface Particular {
  consultaId: string;
  inicio: string;
  statusConsulta: StatusConsulta;
  pacienteId: string;
  paciente: string;
  profissional: string;
  especialidade: string;
  unidade: string;
  valor: number;
  formaPagamento: string;
  pagamentoConfirmado: boolean;
  pagamentoConfirmadoEm?: string | null;
  pagamentoConfirmadoPor?: string | null;
}

/** Acompanhamento dos pagamentos das consultas particulares (a confirmação é feita pela recepção, depois do check-in). */
export default function ParticularesAba() {
  const { message } = App.useApp();
  const [dados, setDados] = useState<Pagina<Particular>>({ itens: [], pagina: 1, tamanho: TAMANHO, total: 0 });
  const [carregando, setCarregando] = useState(false);
  const [pagina, setPagina] = useState(1);
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [pagamento, setPagamento] = useState<'PENDENTE' | 'CONFIRMADO' | undefined>();
  const [unidadeId, setUnidadeId] = useState<number | undefined>();
  const [de, setDe] = useState('');
  const [ate, setAte] = useState('');
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');

  useEffect(() => {
    api.get<ItemApoio[]>('/apoio/unidades').then(setUnidades).catch(() => undefined);
  }, []);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(
        await api.get<Pagina<Particular>>('/faturamento/particulares', {
          pagamento,
          unidadeId,
          dataInicial: de || undefined,
          dataFinal: ate || undefined,
          busca: buscaAplicada || undefined,
          pagina,
          tamanho: TAMANHO
        })
      );
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar as consultas particulares.');
    } finally {
      setCarregando(false);
    }
  }, [pagamento, unidadeId, de, ate, buscaAplicada, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  const filtrar = <T,>(set: (v: T) => void) => (v: T) => {
    set(v);
    setPagina(1);
  };

  const colunas: TableColumnsType<Particular> = [
    { title: 'Data e hora', width: 150, render: (_, c) => formatarDataHora(c.inicio) },
    { title: 'Paciente', render: (_, c) => <LinkPaciente id={c.pacienteId} nome={c.paciente} aoSalvar={() => void carregar()} /> },
    { title: 'Profissional', dataIndex: 'profissional' },
    { title: 'Especialidade', dataIndex: 'especialidade' },
    { title: 'Unidade', dataIndex: 'unidade' },
    { title: 'Valor', width: 110, align: 'right', render: (_, c) => REAIS.format(c.valor) },
    { title: 'Forma de pagamento', dataIndex: 'formaPagamento' },
    { title: 'Consulta', width: 120, render: (_, c) => <Tag color={CORES_STATUS[c.statusConsulta]}>{NOMES_STATUS[c.statusConsulta]}</Tag> },
    {
      title: 'Pagamento',
      width: 190,
      render: (_, c) =>
        c.pagamentoConfirmado ? (
          <>
            <Tag color="green">Confirmado</Tag>
            <div style={{ fontSize: 12 }}>
              {formatarDataHora(c.pagamentoConfirmadoEm)}
              {c.pagamentoConfirmadoPor ? ` - ${c.pagamentoConfirmadoPor}` : ''}
            </div>
          </>
        ) : (
          <Tag color="gold">Pendente</Tag>
        )
    }
  ];

  return (
    <>
      <Space style={{ marginBottom: 16 }} wrap>
        <Select
          allowClear
          placeholder="Pagamento"
          style={{ width: 170 }}
          value={pagamento}
          onChange={filtrar(setPagamento)}
          options={[
            { value: 'PENDENTE', label: 'Pendentes' },
            { value: 'CONFIRMADO', label: 'Confirmados' }
          ]}
        />
        <Select
          allowClear
          placeholder="Todas as unidades"
          style={{ width: 220 }}
          value={unidadeId}
          onChange={filtrar(setUnidadeId)}
          options={unidades.map((u) => ({ value: u.id, label: u.nome }))}
        />
        <Input type="date" style={{ width: 150 }} value={de} onChange={(e) => filtrar(setDe)(e.target.value)} title="Consultas a partir de" />
        <Input type="date" style={{ width: 150 }} value={ate} onChange={(e) => filtrar(setAte)(e.target.value)} title="Consultas até" />
        <Input.Search
          placeholder="Paciente"
          allowClear
          style={{ width: 240 }}
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
      </Space>

      <Table<Particular>
        rowKey="consultaId"
        columns={colunas}
        dataSource={dados.itens}
        loading={carregando}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Nenhuma consulta particular encontrada.' }}
        pagination={{
          current: pagina,
          pageSize: TAMANHO,
          total: dados.total,
          showSizeChanger: false,
          showTotal: (t) => `${t} consulta(s)`,
          onChange: setPagina
        }}
      />
    </>
  );
}
