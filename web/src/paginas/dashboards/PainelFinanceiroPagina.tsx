import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, App, Button, Card, Col, Empty, Input, Row, Segmented, Select, Space, Statistic, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio } from '../../api/tipos';
import { adicionarDias, hojeBrasilia, horaBrasilia, segundaDaSemana } from '../agenda/datas';

interface ValorQtd {
  quantidade: number;
  valor: number;
}

interface Linha {
  tipo?: 'PARTICULAR' | 'CONVENIO' | null;
  nome: string;
  quantidade: number;
  valor: number;
  percentual: number;
  autorizado?: number | null;
  pendente?: number | null;
  negado?: number | null;
}

interface PainelFinanceiro {
  dataInicial: string;
  dataFinal: string;
  atualizadoEm: string;
  agrupamento: 'DIA' | 'MES';
  totais: {
    faturado: ValorQtd;
    ticketMedio: number;
    particularRecebido: ValorQtd;
    particularAConfirmar: ValorQtd;
    convenioAutorizado: ValorQtd;
    convenioPendente: ValorQtd;
    convenioNegado: ValorQtd;
    previsto: ValorQtd;
  };
  evolucao: { periodo: string; quantidade: number; valor: number }[];
  porConvenio: Linha[];
  porEspecialidade: Linha[];
  porProfissional: Linha[];
}

type Periodo = 'DIA' | 'SEMANA' | 'MES';

const ATUALIZA_A_CADA_MS = 60_000;
const MOEDA = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
const PERCENTUAL = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 0, maximumFractionDigits: 1 });
const COR_PARTICULAR = '#e0a800';
const COR_CONVENIO = '#1f6f4a';

function primeiroDoMes(dia: string) {
  return `${dia.slice(0, 7)}-01`;
}

function ultimoDoMes(dia: string) {
  const ano = Number(dia.slice(0, 4));
  const mes = Number(dia.slice(5, 7));
  const ultimo = new Date(Date.UTC(ano, mes, 0)).getUTCDate();
  return `${dia.slice(0, 7)}-${String(ultimo).padStart(2, '0')}`;
}

function rotuloPeriodo(periodo: string, agrupamento: 'DIA' | 'MES') {
  if (agrupamento === 'MES') return `${periodo.slice(5, 7)}/${periodo.slice(2, 4)}`;
  return `${periodo.slice(8, 10)}/${periodo.slice(5, 7)}`;
}

/** Colunas verticais: valor faturado por dia ou por mês. */
function GraficoEvolucao({ itens, agrupamento }: { itens: PainelFinanceiro['evolucao']; agrupamento: 'DIA' | 'MES' }) {
  const maximo = Math.max(1, ...itens.map((i) => i.valor));
  const passo = Math.max(1, Math.ceil(itens.length / 12));
  return (
    <div style={{ display: 'flex', alignItems: 'flex-end', gap: itens.length > 40 ? 2 : 4, height: 180, paddingTop: 8 }}>
      {itens.map((i, n) => (
        <div
          key={i.periodo}
          title={`${rotuloPeriodo(i.periodo, agrupamento)}: ${MOEDA.format(i.valor)} (${i.quantidade} consulta(s))`}
          style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', justifyContent: 'flex-end', alignItems: 'center', height: '100%' }}
        >
          <div
            style={{
              width: '100%',
              height: `${(i.valor / maximo) * 100}%`,
              minHeight: i.valor > 0 ? 3 : 1,
              background: i.valor > 0 ? COR_CONVENIO : '#e8e8e8',
              borderRadius: '3px 3px 0 0'
            }}
          />
          <span style={{ fontSize: 10, color: '#8c8c8c', height: 14, whiteSpace: 'nowrap' }}>{n % passo === 0 ? rotuloPeriodo(i.periodo, agrupamento) : ''}</span>
        </div>
      ))}
    </div>
  );
}

function colunasLinha(titulo: string, comGuia = false): TableColumnsType<Linha> {
  const colunas: TableColumnsType<Linha> = [
    {
      title: titulo,
      render: (_, l) => (
        <>
          {l.nome}{' '}
          {l.tipo === 'PARTICULAR' && (
            <Tag color="gold" style={{ marginLeft: 4 }}>
              Particular
            </Tag>
          )}
        </>
      )
    },
    { title: 'Consultas', dataIndex: 'quantidade', align: 'right', width: 90 },
    { title: 'Valor', align: 'right', width: 130, render: (_, l) => <strong>{MOEDA.format(l.valor)}</strong> },
    { title: '%', align: 'right', width: 70, render: (_, l) => `${PERCENTUAL.format(l.percentual)}%` }
  ];
  if (comGuia) {
    colunas.push(
      { title: 'Autorizado', align: 'right', width: 120, render: (_, l) => (l.autorizado == null ? '—' : MOEDA.format(l.autorizado)) },
      { title: 'Pendente', align: 'right', width: 120, render: (_, l) => (l.pendente == null ? '—' : MOEDA.format(l.pendente)) },
      { title: 'Negado', align: 'right', width: 120, render: (_, l) => (l.negado == null ? '—' : MOEDA.format(l.negado)) }
    );
  }
  return colunas;
}

export default function PainelFinanceiroPagina() {
  const { message } = App.useApp();
  const hoje = hojeBrasilia();
  const [periodo, setPeriodo] = useState<Periodo | undefined>('MES');
  const [dataInicial, setDataInicial] = useState(primeiroDoMes(hoje));
  const [dataFinal, setDataFinal] = useState(ultimoDoMes(hoje));
  const [unidadeId, setUnidadeId] = useState<number | undefined>();
  const [profissionalId, setProfissionalId] = useState<number | undefined>();
  const [tipo, setTipo] = useState<'PARTICULAR' | 'CONVENIO' | undefined>();
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);
  const [painel, setPainel] = useState<PainelFinanceiro | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [erro, setErro] = useState('');
  const sequencia = useRef(0);

  useEffect(() => {
    api.get<ItemApoio[]>('/apoio/unidades').then(setUnidades).catch(() => setUnidades([]));
    api.get<ItemApoio[]>('/apoio/profissionais').then(setProfissionais).catch(() => setProfissionais([]));
  }, []);

  const periodoValido = dataInicial.length === 10 && dataFinal.length === 10 && dataFinal >= dataInicial;

  const carregar = useCallback(
    async (silencioso = false) => {
      if (!periodoValido) return;
      const minha = ++sequencia.current;
      if (!silencioso) setCarregando(true);
      try {
        const resposta = await api.get<PainelFinanceiro>('/dashboards/financeiro', { dataInicial, dataFinal, unidadeId, profissionalId, tipo });
        if (minha !== sequencia.current) return;
        setPainel(resposta);
        setErro('');
      } catch (e) {
        if (minha !== sequencia.current) return;
        const texto = e instanceof ApiErro ? e.message : 'Não foi possível carregar o painel financeiro.';
        if (silencioso) setErro(texto);
        else message.error(texto);
      } finally {
        if (minha === sequencia.current) setCarregando(false);
      }
    },
    [periodoValido, dataInicial, dataFinal, unidadeId, profissionalId, tipo, message]
  );

  useEffect(() => {
    void carregar();
    const t = window.setInterval(() => void carregar(true), ATUALIZA_A_CADA_MS);
    return () => window.clearInterval(t);
  }, [carregar]);

  const escolherPeriodo = (v: Periodo) => {
    setPeriodo(v);
    if (v === 'DIA') {
      setDataInicial(hoje);
      setDataFinal(hoje);
    } else if (v === 'SEMANA') {
      const segunda = segundaDaSemana(hoje);
      setDataInicial(segunda);
      setDataFinal(adicionarDias(segunda, 6));
    } else {
      setDataInicial(primeiroDoMes(hoje));
      setDataFinal(ultimoDoMes(hoje));
    }
  };
  const mudarInicial = (v: string) => {
    setPeriodo(undefined);
    setDataInicial(v);
    if (v && dataFinal && dataFinal < v) setDataFinal(v);
  };
  const mudarFinal = (v: string) => {
    setPeriodo(undefined);
    setDataFinal(v);
    if (v && dataInicial && v < dataInicial) setDataInicial(v);
  };

  const t = painel?.totais;
  const cartoes: { titulo: string; valor?: number; detalhe?: string; cor?: string }[] = [
    { titulo: 'Faturado', valor: t?.faturado.valor, detalhe: `${t?.faturado.quantidade ?? 0} consulta(s) concluída(s)` },
    { titulo: 'Particular não pago', valor: t?.particularAConfirmar.valor, detalhe: `${t?.particularAConfirmar.quantidade ?? 0} consulta(s) sem pagamento`, cor: '#f5222d' },
    { titulo: 'Particular recebido', valor: t?.particularRecebido.valor, detalhe: `${t?.particularRecebido.quantidade ?? 0} consulta(s)`, cor: COR_PARTICULAR },
    { titulo: 'Particular a confirmar', valor: t?.particularAConfirmar.valor, detalhe: `${t?.particularAConfirmar.quantidade ?? 0} sem pagamento confirmado`, cor: '#fa8c16' },
    { titulo: 'Convênio autorizado', valor: t?.convenioAutorizado.valor, detalhe: `${t?.convenioAutorizado.quantidade ?? 0} consulta(s)`, cor: COR_CONVENIO },
    { titulo: 'Convênio pendente', valor: t?.convenioPendente.valor, detalhe: `${t?.convenioPendente.quantidade ?? 0} guia(s) pendente(s)`, cor: '#fa8c16' },
    { titulo: 'Convênio negado', valor: t?.convenioNegado.valor, detalhe: `${t?.convenioNegado.quantidade ?? 0} guia(s) negada(s)`, cor: '#f5222d' },
    { titulo: 'Previsto', valor: t?.previsto.valor, detalhe: `${t?.previsto.quantidade ?? 0} ainda não concluída(s)`, cor: '#1677ff' }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Painel financeiro
        </Typography.Title>
        <Space wrap>
          <Segmented<Periodo>
            value={periodo as Periodo}
            onChange={escolherPeriodo}
            options={[
              { value: 'DIA', label: 'Dia' },
              { value: 'SEMANA', label: 'Semana' },
              { value: 'MES', label: 'Mês' }
            ]}
          />
          <Input type="date" aria-label="Data inicial" style={{ width: 150 }} value={dataInicial} onChange={(e) => mudarInicial(e.target.value)} />
          <Input type="date" aria-label="Data final" style={{ width: 150 }} value={dataFinal} min={dataInicial} status={periodoValido ? undefined : 'error'} onChange={(e) => mudarFinal(e.target.value)} />
          <Select<number>
            allowClear
            showSearch
            optionFilterProp="label"
            style={{ width: 200 }}
            placeholder="Todas as unidades"
            value={unidadeId}
            onChange={setUnidadeId}
            options={unidades.map((u) => ({ value: u.id, label: u.nome }))}
          />
          <Select<number>
            allowClear
            showSearch
            optionFilterProp="label"
            style={{ width: 220 }}
            placeholder="Todos os profissionais"
            value={profissionalId}
            onChange={setProfissionalId}
            options={profissionais.map((x) => ({ value: x.id, label: x.nome }))}
          />
          <Select<'PARTICULAR' | 'CONVENIO'>
            allowClear
            style={{ width: 150 }}
            placeholder="Todos os tipos"
            value={tipo}
            onChange={setTipo}
            options={[
              { value: 'CONVENIO', label: 'Convênio' },
              { value: 'PARTICULAR', label: 'Particular' }
            ]}
          />
          <Button icon={<ReloadOutlined />} onClick={() => void carregar()} loading={carregando} title="Atualizar agora" />
        </Space>
      </Space>

      {erro && <Alert type="warning" showIcon message={erro} style={{ marginBottom: 16 }} />}

      <Row gutter={[12, 12]}>
        {cartoes.map((c) => (
          <Col key={c.titulo} xs={24} sm={12} lg={6}>
            <Card size="small" loading={carregando && !painel}>
              <Statistic title={c.titulo} value={c.valor ?? 0} formatter={(v) => MOEDA.format(Number(v))} valueStyle={c.cor ? { color: c.cor } : undefined} />
              {c.detalhe && <Typography.Text type="secondary">{c.detalhe}</Typography.Text>}
            </Card>
          </Col>
        ))}
      </Row>
      <Typography.Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
        O faturado conta só as consultas concluídas. Canceladas, remarcadas e faltas ficam de fora; as ainda não concluídas aparecem em “Previsto”.
      </Typography.Text>

      <Card
        size="small"
        style={{ marginTop: 16 }}
        title={painel?.agrupamento === 'MES' ? 'Faturado por mês' : 'Faturado por dia'}
        extra={painel ? <Typography.Text type="secondary">Atualizado às {horaBrasilia(painel.atualizadoEm)}</Typography.Text> : null}
      >
        {!painel || painel.totais.faturado.quantidade === 0 ? (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhuma consulta concluída no período." />
        ) : (
          <GraficoEvolucao itens={painel.evolucao} agrupamento={painel.agrupamento} />
        )}
      </Card>

      <Card size="small" style={{ marginTop: 16 }} title="Por convênio e particular">
        <Table<Linha>
          rowKey={(l) => `${l.tipo}-${l.nome}`}
          size="small"
          columns={colunasLinha('Convênio', true)}
          dataSource={painel?.porConvenio ?? []}
          loading={carregando && !painel}
          pagination={false}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Sem faturamento no período." /> }}
        />
      </Card>

      <Row gutter={[12, 12]} style={{ marginTop: 16 }}>
        <Col xs={24} lg={12}>
          <Card size="small" title="Por especialidade">
            <Table<Linha>
              rowKey="nome"
              size="small"
              columns={colunasLinha('Especialidade')}
              dataSource={painel?.porEspecialidade ?? []}
              loading={carregando && !painel}
              pagination={false}
              scroll={{ x: 'max-content' }}
              locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Sem faturamento no período." /> }}
            />
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card size="small" title="Por profissional">
            <Table<Linha>
              rowKey="nome"
              size="small"
              columns={colunasLinha('Profissional')}
              dataSource={painel?.porProfissional ?? []}
              loading={carregando && !painel}
              pagination={false}
              scroll={{ x: 'max-content' }}
              locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Sem faturamento no período." /> }}
            />
          </Card>
        </Col>
      </Row>
    </>
  );
}
