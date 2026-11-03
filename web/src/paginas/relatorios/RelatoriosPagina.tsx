import { useCallback, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { Alert, App, Button, Card, Col, Descriptions, Empty, Input, Progress, Row, Segmented, Select, Space, Statistic, Switch, Table, Tabs, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import MenuExportar from '../../componentes/MenuExportar';
import { baixarCsv, baixarPdf } from '../../util/exportar';
import type { TabelaExportacao } from '../../util/exportar';
import { adicionarDias, hojeBrasilia, segundaDaSemana } from '../agenda/datas';
import { formatarDataHora } from '../../util/formatos';
import SemPermissao from '../SemPermissao';

interface Referencia {
  id: number;
  nome: string;
}

type Periodo = 'DIA' | 'SEMANA' | 'MES';
type Tipo = 'PARTICULAR' | 'CONVENIO';

interface Filtros {
  dataInicial: string;
  dataFinal: string;
  profissionalId?: number;
  unidadeId?: number;
  convenioId?: number;
  tipoAtendimento?: Tipo;
}

function primeiroDoMes(dia: string) {
  return `${dia.slice(0, 7)}-01`;
}

function ultimoDoMes(dia: string) {
  const ultimo = new Date(Date.UTC(Number(dia.slice(0, 4)), Number(dia.slice(5, 7)), 0)).getUTCDate();
  return `${dia.slice(0, 7)}-${String(ultimo).padStart(2, '0')}`;
}

function horas(minutos: number) {
  return `${Math.floor(minutos / 60)}h${String(minutos % 60).padStart(2, '0')}`;
}

const PERCENTUAL = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 0, maximumFractionDigits: 1 });

// ---------------------------------------------------------------- barra de filtros (a mesma nas três abas)

function BarraFiltros({
  filtros,
  aoMudar,
  aoAtualizar,
  carregando,
  extra,
  exportacao,
  ocultar = []
}: {
  filtros: Filtros;
  aoMudar: (f: Filtros) => void;
  aoAtualizar: () => void;
  carregando: boolean;
  extra?: ReactNode;
  exportacao?: ReactNode;
  /** Filtros que não se aplicam ao relatório. */
  ocultar?: ('convenio' | 'tipo')[];
}) {
  const [periodo, setPeriodo] = useState<Periodo | undefined>('MES');
  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [convenios, setConvenios] = useState<ItemApoio[]>([]);

  useEffect(() => {
    api.get<ItemApoio[]>('/apoio/profissionais').then(setProfissionais).catch(() => setProfissionais([]));
    api.get<ItemApoio[]>('/apoio/unidades').then(setUnidades).catch(() => setUnidades([]));
    api.get<ItemApoio[]>('/apoio/convenios').then(setConvenios).catch(() => setConvenios([]));
  }, []);

  const escolher = (v: Periodo) => {
    const hoje = hojeBrasilia();
    setPeriodo(v);
    if (v === 'DIA') aoMudar({ ...filtros, dataInicial: hoje, dataFinal: hoje });
    else if (v === 'SEMANA') {
      const segunda = segundaDaSemana(hoje);
      aoMudar({ ...filtros, dataInicial: segunda, dataFinal: adicionarDias(segunda, 6) });
    } else aoMudar({ ...filtros, dataInicial: primeiroDoMes(hoje), dataFinal: ultimoDoMes(hoje) });
  };
  const mudarInicial = (v: string) => {
    setPeriodo(undefined);
    aoMudar({ ...filtros, dataInicial: v, dataFinal: v && filtros.dataFinal < v ? v : filtros.dataFinal });
  };
  const mudarFinal = (v: string) => {
    setPeriodo(undefined);
    aoMudar({ ...filtros, dataFinal: v, dataInicial: v && filtros.dataInicial > v ? v : filtros.dataInicial });
  };

  return (
    <Space wrap style={{ marginBottom: 16 }}>
      <Segmented<Periodo>
        value={periodo as Periodo}
        onChange={escolher}
        options={[
          { value: 'DIA', label: 'Dia' },
          { value: 'SEMANA', label: 'Semana' },
          { value: 'MES', label: 'Mês' }
        ]}
      />
      <Input type="date" aria-label="Data inicial" style={{ width: 150 }} value={filtros.dataInicial} onChange={(e) => mudarInicial(e.target.value)} />
      <Input type="date" aria-label="Data final" style={{ width: 150 }} value={filtros.dataFinal} min={filtros.dataInicial} onChange={(e) => mudarFinal(e.target.value)} />
      <Select<number>
        allowClear
        showSearch
        optionFilterProp="label"
        style={{ width: 220 }}
        placeholder="Todos os profissionais"
        value={filtros.profissionalId}
        onChange={(v) => aoMudar({ ...filtros, profissionalId: v })}
        options={profissionais.map((x) => ({ value: x.id, label: x.nome }))}
      />
      <Select<number>
        allowClear
        showSearch
        optionFilterProp="label"
        style={{ width: 200 }}
        placeholder="Todas as unidades"
        value={filtros.unidadeId}
        onChange={(v) => aoMudar({ ...filtros, unidadeId: v })}
        options={unidades.map((x) => ({ value: x.id, label: x.nome }))}
      />
      {!ocultar.includes('convenio') && <Select<number>
        allowClear
        showSearch
        optionFilterProp="label"
        style={{ width: 200 }}
        placeholder="Todos os convênios"
        value={filtros.convenioId}
        onChange={(v) => aoMudar({ ...filtros, convenioId: v, tipoAtendimento: v ? 'CONVENIO' : filtros.tipoAtendimento })}
        options={convenios.map((x) => ({ value: x.id, label: x.nome }))}
      />}
      {!ocultar.includes('tipo') && <Select<Tipo>
        allowClear
        style={{ width: 150 }}
        placeholder="Todos os tipos"
        value={filtros.tipoAtendimento}
        onChange={(v) => aoMudar({ ...filtros, tipoAtendimento: v, convenioId: v === 'PARTICULAR' ? undefined : filtros.convenioId })}
        options={[
          { value: 'CONVENIO', label: 'Convênio' },
          { value: 'PARTICULAR', label: 'Particular' }
        ]}
      />}
      {extra}
      <Button icon={<ReloadOutlined />} onClick={aoAtualizar} loading={carregando} title="Atualizar" />
      {exportacao}
    </Space>
  );
}

// ---------------------------------------------------------------- lógica comum de cada aba

function filtrosIniciais(): Filtros {
  const hoje = hojeBrasilia();
  return { dataInicial: primeiroDoMes(hoje), dataFinal: ultimoDoMes(hoje) };
}

function useRelatorio<T>(caminho: string, filtros: Filtros, extras: Record<string, string | number | undefined> = {}) {
  const { message } = App.useApp();
  const [dados, setDados] = useState<T | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [erro, setErro] = useState('');
  const chaveExtras = JSON.stringify(extras);

  const carregar = useCallback(async () => {
    if (!filtros.dataInicial || !filtros.dataFinal || filtros.dataFinal < filtros.dataInicial) return;
    setCarregando(true);
    try {
      setDados(await api.get<T>(`/relatorios/${caminho}`, { ...filtros, ...JSON.parse(chaveExtras) }));
      setErro('');
    } catch (e) {
      setDados(null);
      const texto = e instanceof ApiErro ? e.message : 'Não foi possível gerar o relatório.';
      setErro(texto);
      message.error(texto);
    } finally {
      setCarregando(false);
    }
  }, [caminho, filtros, chaveExtras, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  return { dados, carregando, erro, carregar };
}

/** Exportação: o servidor entrega a tabela (e grava no log); o navegador faz o PDF ou o CSV. */
function BotaoExportacao({ relatorio, filtros, extras = {}, desabilitado }: { relatorio: string; filtros: object; extras?: Record<string, string | undefined>; desabilitado: boolean }) {
  const { pode, sessao } = useAuth();
  if (!pode('relatorio.exportacao', 'exportar')) return null;

  async function tabela() {
    try {
      return await api.get<TabelaExportacao>(`/relatorios/${relatorio}/exportacao`, { ...filtros, ...extras });
    } catch (e) {
      throw new Error(e instanceof ApiErro ? e.message : 'Não foi possível exportar.');
    }
  }

  return <MenuExportar disabled={desabilitado} onCsv={async () => baixarCsv(await tabela())} onPdf={async () => baixarPdf(await tabela(), sessao?.usuario.nomeCompleto ?? '')} />;
}

// ---------------------------------------------------------------- consultas por profissional

interface RelConsultas {
  itens: { profissional: Referencia; atendimentos: number; agendamentos: number }[];
  totais: { atendimentos: number; agendamentos: number };
}

function AbaConsultas() {
  const [filtros, setFiltros] = useState<Filtros>(filtrosIniciais);
  const { dados, carregando, erro, carregar } = useRelatorio<RelConsultas>('consultas-por-profissional', filtros);

  const colunas: TableColumnsType<RelConsultas['itens'][number]> = [
    { title: 'Profissional', render: (_, l) => l.profissional.nome },
    { title: 'Atendimentos (concluídas)', dataIndex: 'atendimentos', align: 'right', width: 220 },
    { title: 'Agendamentos', dataIndex: 'agendamentos', align: 'right', width: 160 }
  ];

  return (
    <>
      <BarraFiltros
        filtros={filtros}
        aoMudar={setFiltros}
        aoAtualizar={() => void carregar()}
        carregando={carregando}
        exportacao={<BotaoExportacao relatorio="consultas-por-profissional" filtros={filtros} desabilitado={!dados || dados.itens.length === 0} />}
      />
      {erro && <Alert type="warning" showIcon message={erro} style={{ marginBottom: 16 }} />}
      <Row gutter={12} style={{ marginBottom: 16 }}>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Atendimentos" value={dados?.totais.atendimentos ?? 0} />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Agendamentos" value={dados?.totais.agendamentos ?? 0} />
          </Card>
        </Col>
      </Row>
      <Typography.Paragraph type="secondary">Atendimentos são as consultas concluídas. Agendamentos são todas as consultas marcadas no período, menos as remarcadas.</Typography.Paragraph>
      <Table
        rowKey={(l) => l.profissional.id}
        size="small"
        loading={carregando && !dados}
        columns={colunas}
        dataSource={dados?.itens ?? []}
        pagination={false}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhuma consulta no período." /> }}
        summary={() =>
          dados && dados.itens.length > 0 ? (
            <Table.Summary.Row>
              <Table.Summary.Cell index={0}>
                <strong>Total</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={1} align="right">
                <strong>{dados.totais.atendimentos}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={2} align="right">
                <strong>{dados.totais.agendamentos}</strong>
              </Table.Summary.Cell>
            </Table.Summary.Row>
          ) : null
        }
      />
    </>
  );
}

// ---------------------------------------------------------------- ocupação da agenda

interface RelOcupacao {
  itens: { profissional: Referencia; minutosDisponiveis: number; minutosOcupados: number; ocupacaoPercentual: number; encaixes: number }[];
  totais: { minutosDisponiveis: number; minutosOcupados: number; ocupacaoPercentual: number; encaixes: number };
}

function AbaOcupacao() {
  const [filtros, setFiltros] = useState<Filtros>(filtrosIniciais);
  const { dados, carregando, erro, carregar } = useRelatorio<RelOcupacao>('ocupacao-agenda', filtros);

  const corDaBarra = (p: number) => (p >= 85 ? '#f5222d' : p >= 60 ? '#1f6f4a' : '#faad14');
  const colunas: TableColumnsType<RelOcupacao['itens'][number]> = [
    { title: 'Profissional', render: (_, l) => l.profissional.nome },
    { title: 'Disponível', align: 'right', width: 110, render: (_, l) => horas(l.minutosDisponiveis) },
    { title: 'Ocupado', align: 'right', width: 110, render: (_, l) => horas(l.minutosOcupados) },
    {
      title: 'Ocupação',
      width: 240,
      render: (_, l) => <Progress percent={Math.min(100, l.ocupacaoPercentual)} format={() => `${PERCENTUAL.format(l.ocupacaoPercentual)}%`} strokeColor={corDaBarra(l.ocupacaoPercentual)} size="small" />
    },
    { title: 'Encaixes', dataIndex: 'encaixes', align: 'right', width: 100 }
  ];

  return (
    <>
      <BarraFiltros
        filtros={filtros}
        aoMudar={setFiltros}
        aoAtualizar={() => void carregar()}
        carregando={carregando}
        exportacao={<BotaoExportacao relatorio="ocupacao-agenda" filtros={filtros} desabilitado={!dados || dados.itens.length === 0} />}
      />
      {erro && <Alert type="warning" showIcon message={erro} style={{ marginBottom: 16 }} />}
      <Row gutter={12} style={{ marginBottom: 16 }}>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Ocupação geral" value={dados?.totais.ocupacaoPercentual ?? 0} precision={1} suffix="%" />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Tempo disponível" value={horas(dados?.totais.minutosDisponiveis ?? 0)} />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Tempo ocupado" value={horas(dados?.totais.minutosOcupados ?? 0)} />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Encaixes (fora do cálculo)" value={dados?.totais.encaixes ?? 0} />
          </Card>
        </Col>
      </Row>
      <Typography.Paragraph type="secondary">
        O tempo disponível é o expediente do profissional, sem intervalos, feriados e recesso. O ocupado é a duração das consultas dentro do expediente; canceladas, remarcadas e encaixes não entram. Se escolher convênio ou tipo, só as consultas desse filtro contam como ocupadas.
      </Typography.Paragraph>
      <Table
        rowKey={(l) => l.profissional.id}
        size="small"
        loading={carregando && !dados}
        columns={colunas}
        dataSource={dados?.itens ?? []}
        pagination={false}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhum profissional com expediente no período." /> }}
      />
    </>
  );
}

// ---------------------------------------------------------------- cancelamentos e faltas

interface RelCancelamentos {
  itens: {
    profissional: Referencia;
    totalConsultas: number;
    canceladasPaciente: number;
    canceladasClinica: number;
    faltas: number;
    percentualCancelamento: number;
    percentualFaltas: number;
  }[];
  totais: { totalConsultas: number; canceladasPaciente: number; canceladasClinica: number; faltas: number };
  detalhe?: {
    itens: { consultaId: string; paciente: { id: string; nome: string }; profissional: Referencia; inicio: string; situacao: 'CANCELADA' | 'FALTA'; canceladoPor?: 'PACIENTE' | 'CLINICA' | null; motivo?: string | null }[];
    pagina: number;
    tamanho: number;
    total: number;
  } | null;
}

function AbaCancelamentos() {
  const [filtros, setFiltros] = useState<Filtros>(filtrosIniciais);
  const [visao, setVisao] = useState<'RESUMO' | 'DETALHE'>('RESUMO');
  const [pagina, setPagina] = useState(1);
  const { dados, carregando, erro, carregar } = useRelatorio<RelCancelamentos>('cancelamentos-faltas', filtros, { visao, pagina, tamanho: 20 });

  const aoMudarFiltros = (f: Filtros) => {
    setPagina(1);
    setFiltros(f);
  };

  const colunas: TableColumnsType<RelCancelamentos['itens'][number]> = [
    { title: 'Profissional', render: (_, l) => l.profissional.nome },
    { title: 'Consultas', dataIndex: 'totalConsultas', align: 'right', width: 100 },
    { title: 'Canceladas pelo paciente', dataIndex: 'canceladasPaciente', align: 'right', width: 190 },
    { title: 'Canceladas pela clínica', dataIndex: 'canceladasClinica', align: 'right', width: 180 },
    { title: 'Faltas', dataIndex: 'faltas', align: 'right', width: 90 },
    { title: '% cancelamento', align: 'right', width: 140, render: (_, l) => `${PERCENTUAL.format(l.percentualCancelamento)}%` },
    { title: '% faltas', align: 'right', width: 100, render: (_, l) => `${PERCENTUAL.format(l.percentualFaltas)}%` }
  ];

  const colunasDetalhe: TableColumnsType<NonNullable<RelCancelamentos['detalhe']>['itens'][number]> = [
    { title: 'Data e hora', width: 150, render: (_, l) => formatarDataHora(l.inicio) },
    { title: 'Paciente', render: (_, l) => l.paciente.nome },
    { title: 'Profissional', render: (_, l) => l.profissional.nome },
    { title: 'Situação', width: 110, render: (_, l) => (l.situacao === 'FALTA' ? <Tag color="default">Falta</Tag> : <Tag color="red">Cancelada</Tag>) },
    { title: 'Cancelado por', width: 130, render: (_, l) => (l.canceladoPor === 'PACIENTE' ? 'Paciente' : l.canceladoPor === 'CLINICA' ? 'Clínica' : '—') },
    { title: 'Motivo', render: (_, l) => l.motivo ?? '—' }
  ];

  const t = dados?.totais;
  const canceladas = (t?.canceladasPaciente ?? 0) + (t?.canceladasClinica ?? 0);

  return (
    <>
      <BarraFiltros
        filtros={filtros}
        aoMudar={aoMudarFiltros}
        aoAtualizar={() => void carregar()}
        carregando={carregando}
        extra={
          <Segmented<'RESUMO' | 'DETALHE'>
            value={visao}
            onChange={(v) => {
              setPagina(1);
              setVisao(v);
            }}
            options={[
              { value: 'RESUMO', label: 'Resumo' },
              { value: 'DETALHE', label: 'Detalhado' }
            ]}
          />
        }
        exportacao={<BotaoExportacao relatorio="cancelamentos-faltas" filtros={filtros} extras={{ visao }} desabilitado={!dados || dados.itens.length === 0} />}
      />
      {erro && <Alert type="warning" showIcon message={erro} style={{ marginBottom: 16 }} />}
      <Row gutter={12} style={{ marginBottom: 16 }}>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Consultas" value={t?.totalConsultas ?? 0} />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Canceladas" value={canceladas} valueStyle={{ color: '#f5222d' }} suffix={t && t.totalConsultas > 0 ? `(${PERCENTUAL.format((canceladas / t.totalConsultas) * 100)}%)` : undefined} />
            <Typography.Text type="secondary">
              {t?.canceladasPaciente ?? 0} pelo paciente · {t?.canceladasClinica ?? 0} pela clínica
            </Typography.Text>
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Faltas" value={t?.faltas ?? 0} valueStyle={{ color: '#8c8c8c' }} suffix={t && t.totalConsultas > 0 ? `(${PERCENTUAL.format(((t.faltas ?? 0) / t.totalConsultas) * 100)}%)` : undefined} />
          </Card>
        </Col>
      </Row>
      <Typography.Paragraph type="secondary">As consultas remarcadas não entram na contagem.</Typography.Paragraph>

      <Table
        rowKey={(l) => l.profissional.id}
        size="small"
        loading={carregando && !dados}
        columns={colunas}
        dataSource={dados?.itens ?? []}
        pagination={false}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhuma consulta no período." /> }}
        summary={() =>
          t && dados && dados.itens.length > 0 ? (
            <Table.Summary.Row>
              <Table.Summary.Cell index={0}>
                <strong>Total</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={1} align="right">
                <strong>{t.totalConsultas}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={2} align="right">
                <strong>{t.canceladasPaciente}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={3} align="right">
                <strong>{t.canceladasClinica}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={4} align="right">
                <strong>{t.faltas}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={5} align="right">
                <strong>{t.totalConsultas ? `${PERCENTUAL.format((canceladas / t.totalConsultas) * 100)}%` : '0%'}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={6} align="right">
                <strong>{t.totalConsultas ? `${PERCENTUAL.format((t.faltas / t.totalConsultas) * 100)}%` : '0%'}</strong>
              </Table.Summary.Cell>
            </Table.Summary.Row>
          ) : null
        }
      />

      {visao === 'DETALHE' && (
        <Card size="small" title="Cancelamentos e faltas, um a um" style={{ marginTop: 16 }}>
          <Table
            rowKey="consultaId"
            size="small"
            loading={carregando && !dados}
            columns={colunasDetalhe}
            dataSource={dados?.detalhe?.itens ?? []}
            scroll={{ x: 'max-content' }}
            pagination={{
              current: pagina,
              pageSize: 20,
              total: dados?.detalhe?.total ?? 0,
              showSizeChanger: false,
              onChange: setPagina
            }}
            locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhum cancelamento ou falta no período." /> }}
          />
        </Card>
      )}
    </>
  );
}

// ---------------------------------------------------------------- financeiros

const REAL = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
const dinheiro = (v: number) => REAL.format(v);

interface LinhaFaturamento {
  nome: string;
  quantidade: number;
  valor: number;
  percentual: number;
  confirmado: number;
  pendente: number;
  negado: number;
  ticketMedio: number;
}

interface RelFaturamento {
  itens: LinhaFaturamento[];
  totais: LinhaFaturamento;
}

function AbaFaturamento({ modo }: { modo: 'tipo' | 'convenio' | 'particular' }) {
  const porConvenio = modo === 'convenio';
  const particular = modo === 'particular';
  const caminho = porConvenio ? 'faturamento-por-convenio' : particular ? 'faturamento-particular' : 'faturamento-por-tipo';
  const rotuloConfirmado = porConvenio ? 'Autorizado' : particular ? 'Recebido' : 'Recebido / autorizado';
  const rotuloPendente = porConvenio ? 'Guia pendente' : particular ? 'A confirmar' : 'A confirmar / pendente';
  const [filtros, setFiltros] = useState<Filtros>(filtrosIniciais);
  const { dados, carregando, erro, carregar } = useRelatorio<RelFaturamento>(caminho, filtros);
  const t = dados?.totais;

  const colunas: TableColumnsType<LinhaFaturamento> = [
    { title: porConvenio ? 'Convênio' : particular ? 'Forma de pagamento' : 'Tipo', dataIndex: 'nome' },
    { title: 'Consultas', dataIndex: 'quantidade', align: 'right', width: 100 },
    { title: 'Valor', align: 'right', width: 130, render: (_, l) => <strong>{dinheiro(l.valor)}</strong> },
    { title: '% do total', align: 'right', width: 100, render: (_, l) => `${PERCENTUAL.format(l.percentual)}%` },
    { title: rotuloConfirmado, align: 'right', width: 170, render: (_, l) => dinheiro(l.confirmado) },
    { title: rotuloPendente, align: 'right', width: 170, render: (_, l) => dinheiro(l.pendente) },
    { title: 'Negado', align: 'right', width: 120, render: (_, l) => dinheiro(l.negado) },
    { title: 'Ticket médio', align: 'right', width: 130, render: (_, l) => dinheiro(l.ticketMedio) }
  ];

  return (
    <>
      <BarraFiltros
        filtros={filtros}
        aoMudar={setFiltros}
        aoAtualizar={() => void carregar()}
        carregando={carregando}
        ocultar={porConvenio ? ['tipo'] : particular ? ['convenio', 'tipo'] : []}
        exportacao={<BotaoExportacao relatorio={caminho} filtros={filtros} desabilitado={!dados || (t?.quantidade ?? 0) === 0} />}
      />
      {erro && <Alert type="warning" showIcon message={erro} style={{ marginBottom: 16 }} />}
      <Row gutter={12} style={{ marginBottom: 16 }}>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Faturado" value={t?.valor ?? 0} formatter={(v) => dinheiro(Number(v))} />
            <Typography.Text type="secondary">{t?.quantidade ?? 0} consultas concluídas</Typography.Text>
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small">
            {particular ? (
              <Statistic title="Não pago" value={t?.pendente ?? 0} formatter={(v) => dinheiro(Number(v))} valueStyle={{ color: '#f5222d' }} />
            ) : (
              <Statistic title="Negado" value={t?.negado ?? 0} formatter={(v) => dinheiro(Number(v))} valueStyle={{ color: '#f5222d' }} />
            )}
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title={rotuloConfirmado} value={t?.confirmado ?? 0} formatter={(v) => dinheiro(Number(v))} valueStyle={{ color: '#1f6f4a' }} />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title={particular ? 'A confirmar' : 'Pendente'} value={t?.pendente ?? 0} formatter={(v) => dinheiro(Number(v))} valueStyle={{ color: '#d48806' }} />
          </Card>
        </Col>
      </Row>
      <Typography.Paragraph type="secondary">
        Só consultas concluídas. No particular, recebido é o pagamento confirmado; no convênio, autorizado, pendente e negado seguem a situação da guia.
      </Typography.Paragraph>
      <Table
        rowKey="nome"
        size="small"
        loading={carregando && !dados}
        columns={colunas}
        dataSource={dados?.itens ?? []}
        pagination={false}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhuma consulta concluída no período." /> }}
        summary={() =>
          t && t.quantidade > 0 ? (
            <Table.Summary.Row>
              <Table.Summary.Cell index={0}>
                <strong>Total</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={1} align="right">
                <strong>{t.quantidade}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={2} align="right">
                <strong>{dinheiro(t.valor)}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={3} align="right">
                <strong>100%</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={4} align="right">
                <strong>{dinheiro(t.confirmado)}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={5} align="right">
                <strong>{dinheiro(t.pendente)}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={6} align="right">
                <strong>{dinheiro(t.negado)}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={7} align="right">
                <strong>{dinheiro(t.ticketMedio)}</strong>
              </Table.Summary.Cell>
            </Table.Summary.Row>
          ) : null
        }
      />
    </>
  );
}

interface LinhaMensal {
  mes: string;
  quantidade: number;
  particular: number;
  convenio: number;
  total: number;
  variacaoPercentual?: number | null;
}

interface RelMensal {
  itens: LinhaMensal[];
  totais: LinhaMensal;
}

function nomeDoMes(mes: string) {
  return `${mes.slice(5, 7)}/${mes.slice(0, 4)}`;
}

function AbaFaturamentoMensal() {
  const [filtros, setFiltros] = useState<Filtros>(() => {
    const hoje = hojeBrasilia();
    return { dataInicial: `${hoje.slice(0, 4)}-01-01`, dataFinal: ultimoDoMes(hoje) };
  });
  const { dados, carregando, erro, carregar } = useRelatorio<RelMensal>('faturamento-mensal', filtros);
  const maior = Math.max(1, ...(dados?.itens ?? []).map((i) => i.total));
  const t = dados?.totais;

  const colunas: TableColumnsType<LinhaMensal> = [
    { title: 'Mês', width: 100, render: (_, l) => nomeDoMes(l.mes) },
    { title: 'Consultas', dataIndex: 'quantidade', align: 'right', width: 100 },
    { title: 'Particular', align: 'right', width: 140, render: (_, l) => dinheiro(l.particular) },
    { title: 'Convênio', align: 'right', width: 140, render: (_, l) => dinheiro(l.convenio) },
    { title: 'Total', align: 'right', width: 140, render: (_, l) => <strong>{dinheiro(l.total)}</strong> },
    {
      title: 'Variação',
      align: 'right',
      width: 110,
      render: (_, l) =>
        l.variacaoPercentual == null ? (
          '—'
        ) : (
          <span style={{ color: l.variacaoPercentual >= 0 ? '#1f6f4a' : '#f5222d' }}>
            {l.variacaoPercentual > 0 ? '+' : ''}
            {PERCENTUAL.format(l.variacaoPercentual)}%
          </span>
        )
    },
    { title: 'Comparativo', width: 220, render: (_, l) => <Progress percent={(l.total / maior) * 100} showInfo={false} size="small" strokeColor="#1f6f4a" /> }
  ];

  return (
    <>
      <BarraFiltros
        filtros={filtros}
        aoMudar={setFiltros}
        aoAtualizar={() => void carregar()}
        carregando={carregando}
        exportacao={<BotaoExportacao relatorio="faturamento-mensal" filtros={filtros} desabilitado={!dados || (t?.quantidade ?? 0) === 0} />}
      />
      {erro && <Alert type="warning" showIcon message={erro} style={{ marginBottom: 16 }} />}
      <Row gutter={12} style={{ marginBottom: 16 }}>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Faturado no período" value={t?.total ?? 0} formatter={(v) => dinheiro(Number(v))} />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Particular" value={t?.particular ?? 0} formatter={(v) => dinheiro(Number(v))} />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Convênio" value={t?.convenio ?? 0} formatter={(v) => dinheiro(Number(v))} />
          </Card>
        </Col>
      </Row>
      <Typography.Paragraph type="secondary">
        Só consultas concluídas, mês a mês (meses sem faturamento aparecem zerados). A variação compara com o mês anterior do relatório. O primeiro e o último mês podem ser parciais, conforme as datas escolhidas.
      </Typography.Paragraph>
      <Table
        rowKey="mes"
        size="small"
        loading={carregando && !dados}
        columns={colunas}
        dataSource={dados?.itens ?? []}
        pagination={false}
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhum mês no período." /> }}
        summary={() =>
          t && dados && dados.itens.length > 0 ? (
            <Table.Summary.Row>
              <Table.Summary.Cell index={0}>
                <strong>Total</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={1} align="right">
                <strong>{t.quantidade}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={2} align="right">
                <strong>{dinheiro(t.particular)}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={3} align="right">
                <strong>{dinheiro(t.convenio)}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={4} align="right">
                <strong>{dinheiro(t.total)}</strong>
              </Table.Summary.Cell>
              <Table.Summary.Cell index={5} colSpan={2} />
            </Table.Summary.Row>
          ) : null
        }
      />
    </>
  );
}

// ---------------------------------------------------------------- lista de espera

type SituacaoEspera = 'AGUARDANDO' | 'NOTIFICADO' | 'ATENDIDO' | 'CANCELADO';

const ROTULO_ESPERA: Record<SituacaoEspera, { texto: string; cor: string }> = {
  AGUARDANDO: { texto: 'Aguardando', cor: 'blue' },
  NOTIFICADO: { texto: 'Horário liberado', cor: 'orange' },
  ATENDIDO: { texto: 'Atendido', cor: 'green' },
  CANCELADO: { texto: 'Cancelado', cor: 'default' }
};

interface ItemEspera {
  id: number;
  paciente: { id: string; nome: string };
  profissional?: Referencia | null;
  especialidade?: Referencia | null;
  unidade?: Referencia | null;
  situacao: SituacaoEspera;
  entrouEm: string;
  notificadoEm?: string | null;
  diasNaFila?: number | null;
  observacao?: string | null;
}

interface RelEspera {
  totais: { total: number; aguardando: number; notificado: number; atendido: number; cancelado: number; mediaDiasEmAberto: number };
  itens: ItemEspera[];
  pagina: number;
  tamanho: number;
  total: number;
}

function AbaListaEspera() {
  const [filtros, setFiltros] = useState<Filtros>(filtrosIniciais);
  const [situacao, setSituacao] = useState<SituacaoEspera | undefined>();
  const [pagina, setPagina] = useState(1);
  const { dados, carregando, erro, carregar } = useRelatorio<RelEspera>('lista-espera', filtros, { situacao, pagina, tamanho: 20 });
  const t = dados?.totais;

  const colunas: TableColumnsType<ItemEspera> = [
    { title: 'Paciente', render: (_, l) => l.paciente.nome },
    { title: 'Profissional', render: (_, l) => l.profissional?.nome ?? 'Qualquer' },
    { title: 'Especialidade', render: (_, l) => l.especialidade?.nome ?? '—' },
    { title: 'Unidade', render: (_, l) => l.unidade?.nome ?? '—' },
    { title: 'Situação', width: 150, render: (_, l) => <Tag color={ROTULO_ESPERA[l.situacao].cor}>{ROTULO_ESPERA[l.situacao].texto}</Tag> },
    { title: 'Entrou em', width: 150, render: (_, l) => formatarDataHora(l.entrouEm) },
    { title: 'Avisado em', width: 150, render: (_, l) => (l.notificadoEm ? formatarDataHora(l.notificadoEm) : '—') },
    { title: 'Dias na fila', align: 'right', width: 110, render: (_, l) => l.diasNaFila ?? '—' },
    { title: 'Observação', render: (_, l) => l.observacao ?? '—' }
  ];

  return (
    <>
      <BarraFiltros
        filtros={filtros}
        aoMudar={(f) => {
          setPagina(1);
          setFiltros(f);
        }}
        aoAtualizar={() => void carregar()}
        carregando={carregando}
        ocultar={['convenio', 'tipo']}
        extra={
          <Select<SituacaoEspera>
            allowClear
            style={{ width: 180 }}
            placeholder="Todas as situações"
            value={situacao}
            onChange={(v) => {
              setPagina(1);
              setSituacao(v);
            }}
            options={(Object.keys(ROTULO_ESPERA) as SituacaoEspera[]).map((k) => ({ value: k, label: ROTULO_ESPERA[k].texto }))}
          />
        }
        exportacao={<BotaoExportacao relatorio="lista-espera" filtros={filtros} extras={{ situacao }} desabilitado={!dados || dados.total === 0} />}
      />
      {erro && <Alert type="warning" showIcon message={erro} style={{ marginBottom: 16 }} />}
      <Row gutter={12} style={{ marginBottom: 16 }}>
        <Col xs={12} md={4}>
          <Card size="small">
            <Statistic title="Entraram na fila" value={t?.total ?? 0} />
          </Card>
        </Col>
        <Col xs={12} md={5}>
          <Card size="small">
            <Statistic title="Aguardando" value={t?.aguardando ?? 0} valueStyle={{ color: '#1677ff' }} />
          </Card>
        </Col>
        <Col xs={12} md={5}>
          <Card size="small">
            <Statistic title="Horário liberado" value={t?.notificado ?? 0} valueStyle={{ color: '#d46b08' }} />
          </Card>
        </Col>
        <Col xs={12} md={5}>
          <Card size="small">
            <Statistic title="Atendidos" value={t?.atendido ?? 0} valueStyle={{ color: '#1f6f4a' }} />
          </Card>
        </Col>
        <Col xs={12} md={5}>
          <Card size="small">
            <Statistic title="Cancelados" value={t?.cancelado ?? 0} valueStyle={{ color: '#8c8c8c' }} />
          </Card>
        </Col>
      </Row>
      <Typography.Paragraph type="secondary">
        O período é a data em que a pessoa entrou na fila. Os totais mostram o quadro todo do período; o filtro de situação afeta só a lista abaixo. Tempo médio de espera de quem ainda está na fila:{' '}
        <strong>{PERCENTUAL.format(t?.mediaDiasEmAberto ?? 0)} dias</strong>.
      </Typography.Paragraph>
      <Table
        rowKey="id"
        size="small"
        loading={carregando && !dados}
        columns={colunas}
        dataSource={dados?.itens ?? []}
        scroll={{ x: 'max-content' }}
        pagination={{ current: pagina, pageSize: 20, total: dados?.total ?? 0, showSizeChanger: false, onChange: setPagina }}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Ninguém entrou na fila no período." /> }}
      />
    </>
  );
}

// ---------------------------------------------------------------- prontuários (sem texto clínico, exceto o administrador)

interface ItemProntuarioRel {
  registroId: string;
  paciente: { id: string; nome: string };
  profissional: { id: number; nome: string; registroProfissional: string; numeroConselho: string; ufConselho: string };
  registradoEm: string;
  corrigido: boolean;
  origemAdministrativa: boolean;
  incluidoPorNome?: string | null;
  observacao?: string | null;
}

interface RelProntuarios {
  resultado: { itens: ItemProntuarioRel[]; pagina: number; tamanho: number; total: number };
  totalPacientes: number;
}

function AbaProntuarios() {
  const { sessao } = useAuth();
  const ehAdmin = !!sessao?.administrador;
  const [filtros, setFiltros] = useState<Filtros>(filtrosIniciais);
  const [incluirTexto, setIncluirTexto] = useState(false);
  const [pagina, setPagina] = useState(1);
  const { dados, carregando, erro, carregar } = useRelatorio<RelProntuarios>('prontuarios', filtros, { pagina, tamanho: 20, incluirTexto: incluirTexto ? 'true' : undefined });
  const r = dados?.resultado;

  const colunas: TableColumnsType<ItemProntuarioRel> = [
    { title: 'Data e hora', width: 150, render: (_, l) => formatarDataHora(l.registradoEm) },
    { title: 'Paciente', render: (_, l) => l.paciente.nome },
    { title: 'Profissional', render: (_, l) => `${l.profissional.nome} (${l.profissional.registroProfissional} ${l.profissional.numeroConselho}/${l.profissional.ufConselho})` },
    {
      title: 'Situação',
      width: 300,
      render: (_, l) => (
        <Space size={4} wrap>
          {l.corrigido && <Tag color="orange">Corrigido</Tag>}
          {l.origemAdministrativa && <Tag color="purple">Incluído por {l.incluidoPorNome ?? 'administrador'}</Tag>}
          {!l.corrigido && !l.origemAdministrativa && '—'}
        </Space>
      )
    },
    ...(incluirTexto ? [{ title: 'Registro', render: (_: unknown, l: ItemProntuarioRel) => <span style={{ whiteSpace: 'pre-wrap' }}>{l.observacao ?? '—'}</span> }] : [])
  ];

  return (
    <>
      <BarraFiltros
        filtros={filtros}
        aoMudar={(f) => {
          setPagina(1);
          setFiltros(f);
        }}
        aoAtualizar={() => void carregar()}
        carregando={carregando}
        extra={
          ehAdmin ? (
            <Space size={6}>
              <Switch
                checked={incluirTexto}
                onChange={(v) => {
                  setPagina(1);
                  setIncluirTexto(v);
                }}
              />
              <span>Mostrar o texto (só na tela)</span>
            </Space>
          ) : undefined
        }
        exportacao={<BotaoExportacao relatorio="prontuarios" filtros={filtros} desabilitado={!dados || (r?.total ?? 0) === 0} />}
      />
      {erro && <Alert type="warning" showIcon message={erro} style={{ marginBottom: 16 }} />}
      <Row gutter={12} style={{ marginBottom: 16 }}>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Registros no período" value={r?.total ?? 0} />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small">
            <Statistic title="Pacientes distintos" value={dados?.totalPacientes ?? 0} />
          </Card>
        </Col>
      </Row>
      <Typography.Paragraph type="secondary">
        O período é a data do registro. Por padrão o relatório não mostra o texto clínico, e a exportação nunca o inclui. Os filtros de unidade, convênio e tipo só alcançam registros ligados a uma consulta.
      </Typography.Paragraph>
      <Table
        rowKey="registroId"
        size="small"
        loading={carregando && !dados}
        columns={colunas}
        dataSource={r?.itens ?? []}
        scroll={{ x: 'max-content' }}
        pagination={{ current: pagina, pageSize: 20, total: r?.total ?? 0, showSizeChanger: false, onChange: setPagina }}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhum registro no período." /> }}
      />
    </>
  );
}

// ---------------------------------------------------------------- cadastrais (todos os dados, CPF completo)

type TipoCadastro = 'pacientes' | 'profissionais' | 'colaboradores' | 'convenios' | 'credenciamentos';

interface RelCadastro {
  titulo: string;
  colunas: { titulo: string; alinhamento: 'esquerda' | 'direita'; grupo?: string | null; resumo?: boolean }[];
  linhas: string[][];
  pagina: number;
  tamanho: number;
  total: number;
}

const FILTROS_CADASTRO: Record<TipoCadastro, { profissional?: boolean; convenio?: boolean; estrangeiro?: boolean; unidade?: boolean; vinculo?: boolean; situacao?: boolean; }> = {
  pacientes: { profissional: true, convenio: true, estrangeiro: true },
  profissionais: { unidade: true },
  colaboradores: { vinculo: true },
  convenios: {},
  credenciamentos: { convenio: true, unidade: true, situacao: true }
};

function AbaCadastro({ tipo }: { tipo: TipoCadastro }) {
  const { message } = App.useApp();
  const cfg = FILTROS_CADASTRO[tipo];
  const [ativo, setAtivo] = useState<string>();
  const [profissionalId, setProfissionalId] = useState<number>();
  const [convenioId, setConvenioId] = useState<number>();
  const [unidadeId, setUnidadeId] = useState<number>();
  const [vinculo, setVinculo] = useState<string>();
  const [estrangeiro, setEstrangeiro] = useState<string>();
  const [situacao, setSituacao] = useState<string>();
  const [pagina, setPagina] = useState(1);
  const [dados, setDados] = useState<RelCadastro | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [erro, setErro] = useState('');
  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [convenios, setConvenios] = useState<ItemApoio[]>([]);

  useEffect(() => {
    if (cfg.profissional) api.get<ItemApoio[]>('/apoio/profissionais').then(setProfissionais).catch(() => setProfissionais([]));
    if (cfg.unidade) api.get<ItemApoio[]>('/apoio/unidades').then(setUnidades).catch(() => setUnidades([]));
    if (cfg.convenio) api.get<ItemApoio[]>('/apoio/convenios').then(setConvenios).catch(() => setConvenios([]));
  }, [cfg]);

  const filtros = { ativo: ativo || undefined, estrangeiro, profissionalId, convenioId, unidadeId, vinculo, situacao };

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(await api.get<RelCadastro>(`/relatorios/cadastros/${tipo}`, { ...filtros, pagina, tamanho: 20 }));
      setErro('');
    } catch (e) {
      setDados(null);
      const texto = e instanceof ApiErro ? e.message : 'Não foi possível gerar o relatório.';
      setErro(texto);
      message.error(texto);
    } finally {
      setCarregando(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tipo, ativo, estrangeiro, profissionalId, convenioId, unidadeId, vinculo, situacao, pagina, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  const mudar = <T,>(set: (v: T) => void) => (v: T) => {
    setPagina(1);
    set(v);
  };

  // A tabela mostra só as colunas do resumo; todos os campos ficam nos detalhes da linha, por seção.
  const todas = dados?.colunas ?? [];
  const resumidas = todas.some((c) => c.resumo) ? todas.map((c, i) => ({ c, i })).filter((x) => x.c.resumo) : todas.map((c, i) => ({ c, i }));
  const colunas: TableColumnsType<string[]> = resumidas.map(({ c, i }) => ({
    title: c.titulo,
    key: String(i),
    align: c.alinhamento === 'direita' ? 'right' : 'left',
    render: (_: unknown, linha: string[]) => (i === 0 ? <strong>{linha[i] || '—'}</strong> : linha[i] ? linha[i] : '—')
  }));

  const detalhes = (linha: string[]) => {
    const secoes: string[] = [];
    todas.forEach((c) => {
      const g = c.grupo ?? 'Dados';
      if (!secoes.includes(g)) secoes.push(g);
    });
    return (
      <div style={{ padding: '4px 8px' }}>
        {secoes.map((secao) => (
          <Descriptions
            key={secao}
            size="small"
            bordered
            column={{ xs: 1, md: 2, xl: 3 }}
            title={secao}
            style={{ marginBottom: 12 }}
            items={todas
              .map((c, i) => ({ c, i }))
              .filter((x) => (x.c.grupo ?? 'Dados') === secao)
              .map(({ c, i }) => ({ key: String(i), label: c.titulo, children: linha[i] || '—' }))}
          />
        ))}
      </div>
    );
  };

  return (
    <>
      <Space wrap style={{ marginBottom: 16 }}>
        <Select<string>
          allowClear
          style={{ width: 190 }}
          placeholder="Ativos e inativos"
          value={ativo}
          onChange={mudar(setAtivo)}
          options={[
            { value: 'true', label: 'Só ativos' },
            { value: 'false', label: 'Só inativos' }
          ]}
        />
        {cfg.estrangeiro && (
          <Select<string>
            allowClear
            style={{ width: 200 }}
            placeholder="Nacionais e estrangeiros"
            value={estrangeiro}
            onChange={mudar(setEstrangeiro)}
            options={[
              { value: 'false', label: 'Pacientes nacionais' },
              { value: 'true', label: 'Pacientes estrangeiros' }
            ]}
          />
        )}
        {cfg.profissional && (
          <Select<number>
            allowClear
            showSearch
            optionFilterProp="label"
            style={{ width: 230 }}
            placeholder="Todos os profissionais"
            value={profissionalId}
            onChange={mudar(setProfissionalId)}
            options={profissionais.map((x) => ({ value: x.id, label: x.nome }))}
          />
        )}
        {cfg.convenio && (
          <Select<number>
            allowClear
            showSearch
            optionFilterProp="label"
            style={{ width: 210 }}
            placeholder="Todos os convênios"
            value={convenioId}
            onChange={mudar(setConvenioId)}
            options={convenios.map((x) => ({ value: x.id, label: x.nome }))}
          />
        )}
        {cfg.unidade && (
          <Select<number>
            allowClear
            showSearch
            optionFilterProp="label"
            style={{ width: 210 }}
            placeholder="Todas as unidades"
            value={unidadeId}
            onChange={mudar(setUnidadeId)}
            options={unidades.map((x) => ({ value: x.id, label: x.nome }))}
          />
        )}
        {cfg.vinculo && (
          <Select<string>
            allowClear
            style={{ width: 160 }}
            placeholder="Todos os vínculos"
            value={vinculo}
            onChange={mudar(setVinculo)}
            options={[
              { value: 'CLT', label: 'CLT' },
              { value: 'MEI', label: 'MEI (prestador)' }
            ]}
          />
        )}
        {cfg.situacao && (
          <Select<string>
            allowClear
            style={{ width: 190 }}
            placeholder="Todos os credenciamentos"
            value={situacao}
            onChange={mudar(setSituacao)}
            options={[
              { value: 'APROVADO', label: 'Aprovado' },
              { value: 'EM_ANALISE', label: 'Em análise' },
              { value: 'NAO_CREDENCIADO', label: 'Não credenciado' }
            ]}
          />
        )}
        <Button icon={<ReloadOutlined />} onClick={() => void carregar()} loading={carregando} title="Atualizar" />
        <BotaoExportacao
          relatorio={`cadastro-${tipo}`}
          filtros={{}}
          extras={{ ativo: ativo || undefined, estrangeiro, profissionalId: profissionalId?.toString(), convenioId: convenioId?.toString(), unidadeId: unidadeId?.toString(), vinculo, situacao }}
          desabilitado={!dados || dados.total === 0}
        />
      </Space>
      {erro && <Alert type="warning" showIcon message={erro} style={{ marginBottom: 16 }} />}
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        message="Este relatório traz dados pessoais completos, inclusive o CPF sem máscara. Cada consulta e cada exportação ficam registradas no log de acessos."
      />
      <Typography.Paragraph type="secondary">
        {dados ? `${dados.total} registro(s).` : ''} Clique no + de uma linha para ver todos os dados do cadastro, separados por seção. A exportação em PDF e CSV traz todas as colunas.
      </Typography.Paragraph>
      <Table<string[]>
        rowKey={(l, i) => `${i}-${l[0]}`}
        size="small"
        loading={carregando && !dados}
        columns={colunas}
        dataSource={dados?.linhas ?? []}
        expandable={{ expandedRowRender: detalhes }}
        scroll={{ x: 'max-content' }}
        pagination={{ current: pagina, pageSize: 20, total: dados?.total ?? 0, showSizeChanger: false, onChange: setPagina }}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhum registro encontrado." /> }}
      />
    </>
  );
}

// ---------------------------------------------------------------- tela

type Grupo = 'OPERACIONAIS' | 'FINANCEIROS' | 'CADASTROS';

export default function RelatoriosPagina() {
  const { pode } = useAuth();
  const [grupo, setGrupo] = useState<Grupo>('OPERACIONAIS');

  const abas = [
    { key: 'consultas', grupo: 'OPERACIONAIS', label: 'Consultas por profissional', tela: 'relatorio.consultas_profissional', conteudo: <AbaConsultas /> },
    { key: 'ocupacao', grupo: 'OPERACIONAIS', label: 'Ocupação da agenda', tela: 'relatorio.ocupacao', conteudo: <AbaOcupacao /> },
    { key: 'cancelamentos', grupo: 'OPERACIONAIS', label: 'Cancelamentos e faltas', tela: 'relatorio.cancelamentos_faltas', conteudo: <AbaCancelamentos /> },
    { key: 'espera', grupo: 'OPERACIONAIS', label: 'Lista de espera', tela: 'relatorio.lista_espera', conteudo: <AbaListaEspera /> },
    { key: 'prontuarios', grupo: 'OPERACIONAIS', label: 'Prontuários', tela: 'relatorio.prontuarios', conteudo: <AbaProntuarios /> },
    { key: 'cad-pacientes', grupo: 'CADASTROS', label: 'Pacientes', tela: 'relatorio.cadastro_pacientes', conteudo: <AbaCadastro tipo="pacientes" /> },
    { key: 'cad-profissionais', grupo: 'CADASTROS', label: 'Profissionais', tela: 'relatorio.cadastro_profissionais', conteudo: <AbaCadastro tipo="profissionais" /> },
    { key: 'cad-colaboradores', grupo: 'CADASTROS', label: 'Colaboradores e prestadores', tela: 'relatorio.cadastro_colaboradores', conteudo: <AbaCadastro tipo="colaboradores" /> },
    { key: 'cad-convenios', grupo: 'CADASTROS', label: 'Convênios', tela: 'relatorio.cadastro_convenios', conteudo: <AbaCadastro tipo="convenios" /> },
    { key: 'cad-credenciamentos', grupo: 'CADASTROS', label: 'Credenciamentos', tela: 'relatorio.cadastro_credenciamentos', conteudo: <AbaCadastro tipo="credenciamentos" /> },
    { key: 'fat-tipo', grupo: 'FINANCEIROS', label: 'Faturamento por tipo', tela: 'relatorio.faturamento_tipo', conteudo: <AbaFaturamento modo="tipo" /> },
    { key: 'fat-particular', grupo: 'FINANCEIROS', label: 'Faturamento particular', tela: 'relatorio.faturamento_particular', conteudo: <AbaFaturamento modo="particular" /> },
    { key: 'fat-convenio', grupo: 'FINANCEIROS', label: 'Faturamento por convênio', tela: 'relatorio.faturamento_convenio', conteudo: <AbaFaturamento modo="convenio" /> },
    { key: 'fat-mensal', grupo: 'FINANCEIROS', label: 'Faturamento mensal', tela: 'relatorio.faturamento_mensal', conteudo: <AbaFaturamentoMensal /> }
  ].filter((a) => pode(a.tela, 'visualizar'));

  if (abas.length === 0) return <SemPermissao />;

  // Só mostra o seletor de grupo se o usuário enxerga relatórios dos dois grupos; senão cai no grupo que tem.
  const grupos = (['OPERACIONAIS', 'FINANCEIROS', 'CADASTROS'] as Grupo[]).filter((g) => abas.some((a) => a.grupo === g));
  const ativo = grupos.includes(grupo) ? grupo : grupos[0];

  return (
    <>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        Relatórios
      </Typography.Title>
      {grupos.length > 1 && (
        <Segmented<Grupo>
          style={{ marginBottom: 12 }}
          value={ativo}
          onChange={setGrupo}
          options={[
            { value: 'OPERACIONAIS', label: 'Operacionais' },
            { value: 'FINANCEIROS', label: 'Financeiros' },
            { value: 'CADASTROS', label: 'Cadastros' }
          ].filter((o) => grupos.includes(o.value as Grupo))}
        />
      )}
      <Tabs key={ativo} destroyInactiveTabPane items={abas.filter((a) => a.grupo === ativo).map((a) => ({ key: a.key, label: a.label, children: a.conteudo }))} />
    </>
  );
}
