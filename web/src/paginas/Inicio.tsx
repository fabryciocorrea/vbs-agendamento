import { useCallback, useEffect, useRef, useState } from 'react';
import { Alert, App, Button, Card, Col, Empty, Input, Row, Select, Space, Statistic, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { HomeOutlined, BarChartOutlined, CalendarOutlined, CheckCircleOutlined, CheckSquareOutlined, ClockCircleOutlined, CloseCircleOutlined, PlayCircleOutlined, ReloadOutlined, TeamOutlined, UserDeleteOutlined } from '@ant-design/icons';
import type { ReactNode } from 'react';
import CabecalhoPagina from '../layout/CabecalhoPagina';
import MeuDia from './MeuDia';
import AvisoSemVinculo from '../auth/AvisoSemVinculo';
import { useNavigate } from 'react-router-dom';
import { api, ApiErro } from '../api/cliente';
import type { ItemApoio } from '../api/tipos';
import { useAuth } from '../auth/AuthContext';
import { hojeBrasilia, horaBrasilia } from './agenda/datas';
import { formatarDataHora } from '../util/formatos';
import { CORES_STATUS, NOMES_STATUS } from './agenda/tipos';
import type { StatusConsulta } from './agenda/tipos';

interface PainelConsulta {
  consultaId: string;
  inicio: string;
  status: StatusConsulta;
  paciente: string;
  profissional: string;
  especialidade: string;
  unidade: string;
  tipoAtendimento: 'PARTICULAR' | 'CONVENIO';
  convenio?: string | null;
  encaixe: boolean;
  alertas: string[];
}

interface Distribuicao {
  tipo: 'PARTICULAR' | 'CONVENIO';
  nome: string;
  quantidade: number;
}

interface PainelDoDia {
  data: string;
  dataInicial: string;
  dataFinal: string;
  distribuicao: Distribuicao[];
  atualizadoEm: string;
  totais: {
    agendadas: number;
    confirmadas: number;
    checkIn: number;
    emAtendimento: number;
    concluidas: number;
    canceladas: number;
    faltas: number;
    remarcadas: number;
    encaixes: number;
    total: number;
  };
  pendencias: {
    emAtraso: number;
    pagamentoParticularPendente: number;
    guiasPendentes?: number | null;
    guiasParaCancelar?: number | null;
  };
  proximas: PainelConsulta[];
  listaEspera?: { aguardando: number; horarioLiberado: number } | null;
}

const ATUALIZA_A_CADA_MS = 30_000;
const COR_PARTICULAR = '#e0a800';
const CORES_CONVENIOS = ['#1f6f4a', '#1677ff', '#722ed1', '#13c2c2', '#eb2f96', '#fa541c', '#2f54eb', '#a0d911', '#8c8c8c'];
const PERCENTUAL = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 0, maximumFractionDigits: 1 });

interface ItemGrafico extends Distribuicao {
  cor: string;
  percentual: number;
}

/** Dá uma cor a cada item (particular sempre amarelo; convênios em sequência) e calcula a porcentagem sobre o total. */
function prepararItens(itens: Distribuicao[]): { itens: ItemGrafico[]; total: number } {
  const total = itens.reduce((a, i) => a + i.quantidade, 0);
  let indice = 0;
  return {
    total,
    itens: itens.map((i) => ({
      ...i,
      cor: i.tipo === 'PARTICULAR' ? COR_PARTICULAR : CORES_CONVENIOS[indice++ % CORES_CONVENIOS.length],
      percentual: total === 0 ? 0 : (i.quantidade / total) * 100
    }))
  };
}

/** Barras horizontais: um atendimento por convênio e por particular, do maior para o menor, com a porcentagem de cada um. */
function GraficoBarras({ itens }: { itens: ItemGrafico[] }) {
  const maximo = Math.max(1, ...itens.map((i) => i.quantidade));
  return (
    <Space direction="vertical" size={8} style={{ width: '100%' }}>
      {itens.map((i) => (
        <div key={`${i.tipo}-${i.nome}`}>
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13 }}>
            <span>{i.nome}</span>
            <span>
              <strong>{i.quantidade}</strong> ({PERCENTUAL.format(i.percentual)}%)
            </span>
          </div>
          <div style={{ background: 'var(--vbs-trilho)', borderRadius: 4, height: 12 }}>
            <div style={{ width: `${(i.quantidade / maximo) * 100}%`, minWidth: 4, height: 12, borderRadius: 4, background: i.cor }} />
          </div>
        </div>
      ))}
    </Space>
  );
}

/** Rosca: uma fatia por convênio e por particular, com a legenda de cada um e a porcentagem. */
function GraficoRosca({ itens, total }: { itens: ItemGrafico[]; total: number }) {
  let acumulado = 0;
  const fatias = itens.map((i) => {
    const ini = acumulado;
    acumulado += i.percentual;
    return `${i.cor} ${ini}% ${acumulado}%`;
  });
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 24, flexWrap: 'wrap' }}>
      <div
        role="img"
        aria-label={itens.map((i) => `${i.nome}: ${i.quantidade}`).join('; ')}
        style={{
          width: 150,
          height: 150,
          flex: '0 0 auto',
          borderRadius: '50%',
          background: total === 0 ? 'var(--vbs-trilho)' : `conic-gradient(${fatias.join(', ')})`,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center'
        }}
      >
        <div style={{ width: 94, height: 94, borderRadius: '50%', background: 'var(--vbs-superficie)', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center' }}>
          <strong style={{ fontSize: 22, lineHeight: 1 }}>{total}</strong>
          <span style={{ fontSize: 11, color: 'var(--vbs-texto-suave)' }}>atendimentos</span>
        </div>
      </div>
      <Space direction="vertical" size={4}>
        {itens.length === 0 && <Typography.Text type="secondary">Sem atendimentos neste dia.</Typography.Text>}
        {itens.map((i) => (
          <span key={`${i.tipo}-${i.nome}`}>
            <span style={{ display: 'inline-block', width: 12, height: 12, borderRadius: 3, background: i.cor, marginRight: 8 }} />
            {i.nome}: <strong>{i.quantidade}</strong> ({PERCENTUAL.format(i.percentual)}%)
          </span>
        ))}
      </Space>
    </div>
  );
}

/** Painel: o que está acontecendo no dia ou no período e o que precisa de atenção. */
function PainelDia() {
  const { message } = App.useApp();
  const { pode } = useAuth();
  const navegar = useNavigate();
  const [dataInicial, setDataInicial] = useState(hojeBrasilia());
  const [dataFinal, setDataFinal] = useState(hojeBrasilia());
  const [unidadeId, setUnidadeId] = useState<number | undefined>();
  const [profissionalId, setProfissionalId] = useState<number | undefined>();
  const [tipo, setTipo] = useState<'PARTICULAR' | 'CONVENIO' | undefined>();
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);
  const [painel, setPainel] = useState<PainelDoDia | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [erro, setErro] = useState('');

  useEffect(() => {
    api.get<ItemApoio[]>('/apoio/unidades').then(setUnidades).catch(() => setUnidades([]));
    api.get<ItemApoio[]>('/apoio/profissionais').then(setProfissionais).catch(() => setProfissionais([]));
  }, []);

  const periodoValido = !!dataInicial && !!dataFinal && dataFinal >= dataInicial && dataInicial.length === 10 && dataFinal.length === 10;
  const sequencia = useRef(0);
  const carregar = useCallback(
    async (silencioso = false) => {
      if (!periodoValido) return;
      const minha = ++sequencia.current;
      if (!silencioso) setCarregando(true);
      try {
        const resposta = await api.get<PainelDoDia>('/dashboards/painel-do-dia', { dataInicial: dataInicial || undefined, dataFinal: dataFinal || dataInicial || undefined, unidadeId, profissionalId, tipo });
        if (minha !== sequencia.current) return;
        setPainel(resposta);
        setErro('');
      } catch (e) {
        if (minha !== sequencia.current) return;
        const texto = e instanceof ApiErro ? e.message : 'Não foi possível carregar o painel.';
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
    // O painel se atualiza sozinho a cada 30 segundos.
    const t = window.setInterval(() => void carregar(true), ATUALIZA_A_CADA_MS);
    return () => window.clearInterval(t);
  }, [carregar]);

  const t = painel?.totais;
  const grafico = prepararItens(painel?.distribuicao ?? []);
  const cartoes: { titulo: string; valor?: number; cor: string; icone: ReactNode }[] = [
    { titulo: 'Total', valor: t?.total, cor: '#2b8a62', icone: <TeamOutlined /> },
    { titulo: 'Agendadas', valor: t?.agendadas, cor: '#1677ff', icone: <CalendarOutlined /> },
    { titulo: 'Confirmadas', valor: t?.confirmadas, cor: '#13c2c2', icone: <CheckCircleOutlined /> },
    { titulo: 'Check-in', valor: t?.checkIn, cor: '#fa8c16', icone: <ClockCircleOutlined /> },
    { titulo: 'Em atendimento', valor: t?.emAtendimento, cor: '#722ed1', icone: <PlayCircleOutlined /> },
    { titulo: 'Concluídas', valor: t?.concluidas, cor: '#52c41a', icone: <CheckSquareOutlined /> },
    { titulo: 'Canceladas', valor: t?.canceladas, cor: '#f5222d', icone: <CloseCircleOutlined /> },
    { titulo: 'Faltas', valor: t?.faltas, cor: '#8c8c8c', icone: <UserDeleteOutlined /> }
  ];

  const p = painel?.pendencias;
  const avisos: { texto: string; qtd: number; destino?: string }[] = [
    { texto: 'consulta(s) com o horário passado sem check-in', qtd: p?.emAtraso ?? 0, destino: pode('agenda.profissional', 'visualizar') ? '/agenda' : undefined },
    { texto: 'particular(es) com pagamento a confirmar', qtd: p?.pagamentoParticularPendente ?? 0, destino: pode('agenda.profissional', 'visualizar') ? '/agenda' : undefined },
    { texto: 'guia(s) de convênio pendente(s)', qtd: p?.guiasPendentes ?? 0, destino: pode('faturamento.financeiro', 'visualizar') ? '/faturamento' : undefined },
    { texto: 'guia(s) autorizada(s) de consulta cancelada: cancelar no convênio', qtd: p?.guiasParaCancelar ?? 0, destino: pode('faturamento.financeiro', 'visualizar') ? '/faturamento' : undefined }
  ].filter((a) => a.qtd > 0);

  const colunas: TableColumnsType<PainelConsulta> = [
    { title: 'Data e hora', width: 150, render: (_, c) => <strong>{formatarDataHora(c.inicio)}</strong> },
    {
      title: 'Paciente',
      render: (_, c) => (
        <>
          {c.paciente} {c.encaixe && <Tag color="orange">Encaixe</Tag>}
        </>
      )
    },
    { title: 'Profissional', dataIndex: 'profissional' },
    { title: 'Especialidade', dataIndex: 'especialidade' },
    { title: 'Atendimento', width: 150, render: (_, c) => (c.tipoAtendimento === 'CONVENIO' ? c.convenio ?? 'Convênio' : 'Particular') },
    { title: 'Situação', width: 130, render: (_, c) => <Tag color={CORES_STATUS[c.status]}>{NOMES_STATUS[c.status]}</Tag> },
    {
      title: 'Avisos',
      render: (_, c) =>
        c.alertas.map((a) => (
          <Tag key={a} color={a === 'Atrasada' ? 'red' : 'gold'}>
            {a}
          </Tag>
        ))
    }
  ];

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <CabecalhoPagina icone={<BarChartOutlined />} titulo="Painel" subtitulo="Acompanhe as principais informações do sistema" />
        <Space wrap>
          <Input type="date" aria-label="Data inicial" style={{ width: 150 }} value={dataInicial} onChange={(e) => setDataInicial(e.target.value)} />
          <Input type="date" aria-label="Data final" style={{ width: 150 }} value={dataFinal} min={dataInicial} status={periodoValido ? undefined : 'error'} onChange={(e) => setDataFinal(e.target.value)} />
          <Select<number>
            allowClear
            showSearch
            optionFilterProp="label"
            style={{ width: 220 }}
            placeholder="Todas as unidades"
            value={unidadeId}
            onChange={setUnidadeId}
            options={unidades.map((u) => ({ value: u.id, label: u.nome }))}
          />
          <Select<number>
            allowClear
            showSearch
            optionFilterProp="label"
            style={{ width: 240 }}
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
          <Col key={c.titulo} xs={12} sm={8} md={6} xl={3}>
            <Card size="small" loading={carregando && !painel}>
              <div className="indicador">
                <span className="indicador-icone" style={{ color: c.cor, background: `${c.cor}22` }} aria-hidden="true">
                  {c.icone}
                </span>
                <div>
                  <span className="indicador-titulo">{c.titulo}</span>
                  <span className="indicador-valor" style={{ color: c.cor }}>
                    {c.valor ?? 0}
                  </span>
                </div>
              </div>
            </Card>
          </Col>
        ))}
      </Row>
      {t && (t.encaixes > 0 || t.remarcadas > 0) && (
        <Typography.Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
          {t.encaixes > 0 && `${t.encaixes} encaixe(s) já contado(s) acima. `}
          {t.remarcadas > 0 && `${t.remarcadas} consulta(s) remarcada(s) para outro horário.`}
        </Typography.Text>
      )}

      <Row gutter={[12, 12]} style={{ marginTop: 16 }}>
        <Col xs={24} lg={painel?.listaEspera ? 16 : 24}>
          <Card size="small" title="Precisa de atenção">
            {avisos.length === 0 ? (
              <Typography.Text type="secondary">Nenhuma pendência no momento.</Typography.Text>
            ) : (
              <Space direction="vertical" style={{ width: '100%' }}>
                {avisos.map((a) => (
                  <div key={a.texto} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <Tag color="gold" style={{ minWidth: 32, textAlign: 'center' }}>
                      {a.qtd}
                    </Tag>
                    <span>{a.texto}</span>
                    {a.destino && (
                      <Button type="link" size="small" onClick={() => navegar(a.destino!)}>
                        abrir
                      </Button>
                    )}
                  </div>
                ))}
              </Space>
            )}
          </Card>
        </Col>
        {painel?.listaEspera && (
          <Col xs={24} lg={8}>
            <Card size="small" title="Lista de espera">
              <Space size="large">
                <Statistic title="Aguardando" value={painel.listaEspera.aguardando} />
                <Statistic title="Horário liberado" value={painel.listaEspera.horarioLiberado} valueStyle={painel.listaEspera.horarioLiberado > 0 ? { color: '#fa8c16' } : undefined} />
              </Space>
              {pode('listaespera.cadastro', 'visualizar') && (
                <div style={{ marginTop: 8 }}>
                  <Button type="link" size="small" style={{ padding: 0 }} onClick={() => navegar('/lista-espera')}>
                    abrir a lista de espera
                  </Button>
                </div>
              )}
            </Card>
          </Col>
        )}
      </Row>

      <Row gutter={[12, 12]} style={{ marginTop: 16 }}>
        <Col xs={24} lg={14}>
          <Card size="small" title="Atendimentos por convênio e particular" extra={<Typography.Text type="secondary">sem canceladas, remarcadas e faltas</Typography.Text>}>
            {grafico.itens.length === 0 ? (
              <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhum atendimento neste dia." />
            ) : (
              <GraficoBarras itens={grafico.itens} />
            )}
          </Card>
        </Col>
        <Col xs={24} lg={10}>
          <Card size="small" title="Participação de cada convênio e do particular">
            <GraficoRosca itens={grafico.itens} total={grafico.total} />
          </Card>
        </Col>
      </Row>

      <Card size="small" title="Consultas em andamento" style={{ marginTop: 16 }} extra={painel ? <Typography.Text type="secondary">Atualizado às {horaBrasilia(painel.atualizadoEm)}</Typography.Text> : null}>
        <Table<PainelConsulta>
          rowKey="consultaId"
          size="small"
          columns={colunas}
          dataSource={painel?.proximas ?? []}
          loading={carregando && !painel}
          pagination={false}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhuma consulta em andamento no período." /> }}
        />
        {(painel?.proximas.length ?? 0) >= 12 && (
          <Typography.Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
            Mostrando as 12 primeiras. A agenda tem a lista completa.
          </Typography.Text>
        )}
      </Card>
    </>
  );
}

export default function Inicio() {
  const { sessao, pode } = useAuth();
  if (sessao?.semVinculoProfissional) return <AvisoSemVinculo />;
  // O painel não é para o médico (RN20): mesmo com a liberação no perfil, o servidor responde 403.
  const medico = (sessao?.profissionalId ?? null) !== null;
  if (pode('dashboard.painel', 'visualizar') && !medico) return <PainelDia />;
  if (medico) return <MeuDia />;
  return (
    <>
      <div style={{ marginBottom: 16 }}>
        <CabecalhoPagina icone={<HomeOutlined />} titulo={`Bem-vindo, ${sessao?.usuario.nomeCompleto ?? ''}`} subtitulo="Use o menu à esquerda para acessar as telas liberadas para o seu perfil" />
      </div>
    </>
  );
}
