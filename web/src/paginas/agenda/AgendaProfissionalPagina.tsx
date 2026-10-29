import { useCallback, useEffect, useMemo, useState } from 'react';
import { App, Button, Card, Checkbox, Descriptions, Modal, Segmented, Select, Space, Spin, Tag, Typography } from 'antd';
import { LeftOutlined, PlusOutlined, RightOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import { formatarDataHora } from '../../util/formatos';
import {
  adicionarDias,
  adicionarMeses,
  dataCurta,
  diaBrasilia,
  diaDaSemana,
  hojeBrasilia,
  horaBrasilia,
  nomeDiaSemana,
  nomeMesAno,
  primeiroDoMes,
  segundaDaSemana,
  ultimoDoMes
} from './datas';
import AcoesConsulta from './AcoesConsulta';
import NovaConsultaModal from './NovaConsultaModal';
import { CORES_STATUS, NOMES_STATUS } from './tipos';
import type { AgendaSaida, Consulta, ConsultaResumo } from './tipos';

type Visao = 'dia' | 'semana' | 'mes';

const REAIS = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

function dias(base: string, visao: Visao): string[] {
  if (visao === 'dia') return [base];
  if (visao === 'mes') {
    const primeiro = primeiroDoMes(base);
    const total = Number(ultimoDoMes(base).slice(8, 10));
    return Array.from({ length: total }, (_, i) => adicionarDias(primeiro, i));
  }
  const segunda = segundaDaSemana(base);
  // A clínica atende de segunda a sexta.
  return Array.from({ length: 5 }, (_, i) => adicionarDias(segunda, i));
}

function DetalheConsulta({ id, aoFechar, aoAlterar }: { id: string | null; aoFechar: () => void; aoAlterar: (novoId?: string) => void }) {
  const { message } = App.useApp();
  const [consulta, setConsulta] = useState<Consulta | null>(null);
  const [carregando, setCarregando] = useState(false);

  const [recarga, setRecarga] = useState(0);

  useEffect(() => {
    if (!id) {
      setConsulta(null);
      return;
    }
    setCarregando(true);
    api
      .get<Consulta>(`/consultas/${id}`)
      .then(setConsulta)
      .catch((e) => {
        message.error(e instanceof ApiErro ? e.message : 'Não foi possível abrir a consulta.');
        aoFechar();
      })
      .finally(() => setCarregando(false));
  }, [id, recarga, message, aoFechar]);

  return (
    <Modal open={!!id} title="Consulta" footer={null} onCancel={aoFechar} width={620} destroyOnClose>
      {carregando || !consulta ? (
        <Spin />
      ) : (
        <Descriptions column={1} size="small" bordered>
          <Descriptions.Item label="Situação">
            <Tag color={CORES_STATUS[consulta.status]}>{NOMES_STATUS[consulta.status]}</Tag>
            {consulta.encaixe && <Tag color="orange">Encaixe</Tag>}
          </Descriptions.Item>
          <Descriptions.Item label="Paciente">{consulta.paciente.nome}</Descriptions.Item>
          <Descriptions.Item label="Profissional">{consulta.profissional.nome}</Descriptions.Item>
          <Descriptions.Item label="Especialidade">{consulta.especialidade.nome}</Descriptions.Item>
          <Descriptions.Item label="Unidade">{consulta.unidade.nome}</Descriptions.Item>
          <Descriptions.Item label="Horário">
            {formatarDataHora(consulta.inicio)}
          </Descriptions.Item>
          <Descriptions.Item label="Modalidade">
            {consulta.modalidade === 'ONLINE' ? 'Online' : `Presencial${consulta.sala ? ` - sala ${consulta.sala.numero} (andar ${consulta.sala.andar})` : ''}`}
          </Descriptions.Item>
          {consulta.modalidade === 'ONLINE' && (
            <Descriptions.Item label="Link da consulta">{consulta.linkOnline ?? 'Ainda não informado'}</Descriptions.Item>
          )}
          <Descriptions.Item label="Atendimento">
            {consulta.tipoAtendimento === 'CONVENIO'
              ? `Convênio ${consulta.convenio?.nome ?? ''}${consulta.numeroCarteirinha ? ` - carteirinha ${consulta.numeroCarteirinha}` : ''}`
              : 'Particular'}
          </Descriptions.Item>
          {consulta.carteirinhaVencida && (
            <Descriptions.Item label="Carteirinha">
              <Tag color="red">Vencida</Tag>
            </Descriptions.Item>
          )}
          {consulta.valor !== undefined && consulta.valor !== null && (
            <Descriptions.Item label="Valor">{REAIS.format(consulta.valor)}</Descriptions.Item>
          )}
          <Descriptions.Item label="Forma de pagamento">{consulta.formaPagamento.nome}</Descriptions.Item>
          {consulta.tipoAtendimento === 'CONVENIO' ? (
            <Descriptions.Item label="Guia do convênio">
              {consulta.guia?.status === 'AUTORIZADA' ? (
                <Tag color="green">Autorizada{consulta.guia.numero ? ` - nº ${consulta.guia.numero}` : ''}</Tag>
              ) : consulta.guia?.status === 'NEGADA' ? (
                <Tag color="red">Negada</Tag>
              ) : (
                <Tag color="gold">Aguardando o faturamento</Tag>
              )}
              {consulta.guia?.observacao && <div style={{ marginTop: 4 }}>{consulta.guia.observacao}</div>}
            </Descriptions.Item>
          ) : (
            <Descriptions.Item label="Pagamento">
              {consulta.pagamentoConfirmado ? `Confirmado em ${formatarDataHora(consulta.pagamentoConfirmadoEm)}` : 'Pendente'}
            </Descriptions.Item>
          )}
          {consulta.credenciamentoLiberado && (
            <Descriptions.Item label="Credenciamento liberado">
              {consulta.credenciamentoLiberado.liberadoPor.nomeCompleto}: {consulta.credenciamentoLiberado.justificativa}
            </Descriptions.Item>
          )}
          {consulta.liberacaoAdministrador && (
            <Descriptions.Item label="Liberação do administrador">
              <Tag color="volcano">Liberada</Tag> {consulta.liberacaoAdministrador.liberadoPor.nomeCompleto} em {formatarDataHora(consulta.liberacaoAdministrador.em)}:{' '}
              {consulta.liberacaoAdministrador.justificativa}
            </Descriptions.Item>
          )}
          <Descriptions.Item label="Agendada em">
            {formatarDataHora(consulta.criadoEm)}
            {consulta.criadoPor ? ` por ${consulta.criadoPor.nomeCompleto}` : ''}
          </Descriptions.Item>
        </Descriptions>
      )}
      {consulta && (
        <AcoesConsulta
          consulta={consulta}
          aoAlterar={(novoId) => {
            aoAlterar(novoId);
            setRecarga((n) => n + 1);
          }}
        />
      )}
    </Modal>
  );
}

export default function AgendaProfissionalPagina() {
  const { message } = App.useApp();
  const { sessao, pode } = useAuth();
  const podeIncluir = pode('consulta.agendamento', 'incluir');
  const medico = sessao?.profissionalId ?? null;

  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [profissionalId, setProfissionalId] = useState<number | undefined>(medico ?? undefined);
  const [unidadeId, setUnidadeId] = useState<number | undefined>(undefined);
  const [visao, setVisao] = useState<Visao>('semana');
  const [base, setBase] = useState<string>(hojeBrasilia());
  const [canceladas, setCanceladas] = useState(false);
  const [agenda, setAgenda] = useState<AgendaSaida | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [novaAberta, setNovaAberta] = useState(false);
  const [diaNova, setDiaNova] = useState<string | undefined>(undefined);
  const [detalheId, setDetalheId] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<ItemApoio[]>('/apoio/profissionais')
      .then(setProfissionais)
      .catch(() => setProfissionais([]));
    api
      .get<ItemApoio[]>('/apoio/unidades')
      .then(setUnidades)
      .catch(() => setUnidades([]));
  }, []);

  const lista = useMemo(() => dias(base, visao), [base, visao]);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      // Sem profissional escolhido, mostra a agenda de todos.
      setAgenda(
        await api.get<AgendaSaida>(profissionalId ? `/agenda/profissionais/${profissionalId}` : '/agenda/geral', {
          dataInicial: lista[0],
          dataFinal: lista[lista.length - 1],
          unidadeId,
          incluirCanceladas: canceladas
        })
      );
    } catch (e) {
      setAgenda(null);
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar a agenda.');
    } finally {
      setCarregando(false);
    }
  }, [profissionalId, lista, unidadeId, canceladas, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  function mover(sentido: -1 | 1) {
    setBase((b) => {
      if (visao === 'semana') return adicionarDias(b, sentido * 7);
      if (visao === 'mes') return adicionarMeses(b, sentido);
      // Na visão do dia, pula sábado e domingo.
      let novo = adicionarDias(b, sentido);
      while (diaDaSemana(novo) >= 6) novo = adicionarDias(novo, sentido);
      return novo;
    });
  }

  const hoje = hojeBrasilia();
  const fecharDetalhe = useCallback(() => setDetalheId(null), []);

  function porDia(dia: string) {
    const consultas = (agenda?.consultas ?? []).filter((c) => diaBrasilia(c.inicio) === dia);
    const expediente = (agenda?.expediente ?? []).filter((e) => diaBrasilia(e.inicio) === dia);
    const bloqueios = (agenda?.bloqueios ?? []).filter((b) => diaBrasilia(b.inicio) === dia);
    return { consultas, expediente, bloqueios };
  }

  function abrirNova(dia?: string) {
    setDiaNova(dia && dia >= hoje ? dia : undefined);
    setNovaAberta(true);
  }

  /** Mês em grade de segunda a sexta; clicar num dia abre a visão do dia. */
  function visaoMes() {
    const primeiro = primeiroDoMes(base);
    const ultimo = ultimoDoMes(base);
    const celulas: (string | null)[] = [];
    for (let d = segundaDaSemana(primeiro); d <= ultimo; d = adicionarDias(d, 1)) {
      if (diaDaSemana(d) >= 6) continue;
      celulas.push(d >= primeiro ? d : null);
    }
    const nomes = ['Segunda', 'Terça', 'Quarta', 'Quinta', 'Sexta'];
    return (
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(5, minmax(0, 1fr))', gap: 8 }}>
        {nomes.map((n) => (
          <div key={n} style={{ fontWeight: 600, padding: '4px 8px' }}>
            {n}
          </div>
        ))}
        {celulas.map((dia, i) => {
          if (!dia) return <div key={`vazio${i}`} />;
          const { consultas, bloqueios } = porDia(dia);
          const feriado = bloqueios.find((b) => b.tipo === 'FERIADO' || b.tipo === 'RECESSO');
          const visiveis = consultas.slice(0, 3);
          return (
            <div
              key={dia}
              onClick={() => {
                setBase(dia);
                setVisao('dia');
              }}
              style={{
                cursor: 'pointer',
                minHeight: 110,
                padding: 8,
                borderRadius: 8,
                border: dia === hoje ? '2px solid #023926' : '1px solid #e6e6e6',
                background: feriado ? '#fafafa' : '#fff'
              }}
            >
              <div style={{ fontWeight: dia === hoje ? 700 : 500 }}>{Number(dia.slice(8, 10))}</div>
              {feriado && (
                <Tag color={feriado.tipo === 'FERIADO' ? 'red' : 'magenta'} style={{ whiteSpace: 'normal', marginBottom: 4 }}>
                  {feriado.descricao}
                </Tag>
              )}
              {visiveis.map((c) => (
                <div key={c.id} style={{ fontSize: 12, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                  <strong>
                    {horaBrasilia(c.inicio)}
                  </strong>{' '}
                  {c.paciente.nome}
                </div>
              ))}
              {consultas.length > visiveis.length && (
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  + {consultas.length - visiveis.length} consulta(s)
                </Typography.Text>
              )}
            </div>
          );
        })}
      </div>
    );
  }

  function itemConsulta(c: ConsultaResumo) {
    return (
      <div
        key={c.id}
        onClick={() => setDetalheId(c.id)}
        style={{ cursor: 'pointer', padding: '6px 8px', border: '1px solid #e6e6e6', borderRadius: 6, marginBottom: 6, background: '#fff' }}
      >
        <Space size={4} wrap>
          <strong>
            {horaBrasilia(c.inicio)}
          </strong>
          <Tag color={CORES_STATUS[c.status]} style={{ marginInlineEnd: 0 }}>
            {NOMES_STATUS[c.status]}
          </Tag>
          {c.encaixe && <Tag color="orange" style={{ marginInlineEnd: 0 }}>Encaixe</Tag>}
        </Space>
        <div>{c.paciente.nome}</div>
        {!profissionalId && <div style={{ fontSize: 12, color: '#023926' }}>{c.profissional.nome}</div>}
      </div>
    );
  }

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Typography.Title level={3} style={{ margin: 0 }}>
          Agenda do profissional
        </Typography.Title>
        {podeIncluir && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => abrirNova()}>
            Nova consulta
          </Button>
        )}
      </Space>

      <Space style={{ marginBottom: 16 }} wrap>
        <Select<number>
          showSearch
          optionFilterProp="label"
          style={{ width: 300 }}
          allowClear={medico === null}
          placeholder="Todos os profissionais"
          value={profissionalId}
          onChange={setProfissionalId}
          disabled={medico !== null}
          options={profissionais.map((p) => ({ value: p.id, label: p.nome }))}
        />
        <Select<number>
          allowClear
          style={{ width: 240 }}
          placeholder="Todas as unidades"
          value={unidadeId}
          onChange={setUnidadeId}
          options={unidades.map((u) => ({ value: u.id, label: u.nome }))}
        />
        <Segmented<Visao>
          value={visao}
          onChange={setVisao}
          options={[
            { value: 'dia', label: 'Dia' },
            { value: 'semana', label: 'Semana' },
            { value: 'mes', label: 'Mês' }
          ]}
        />
        <Space.Compact>
          <Button icon={<LeftOutlined />} onClick={() => mover(-1)} title="Anterior" />
          {visao === 'mes' && (
            <Button onClick={() => setBase(hoje)} title="Ir para o mês atual">
              {nomeMesAno(base)}
            </Button>
          )}
          <Button icon={<RightOutlined />} onClick={() => mover(1)} title="Próximo" />
        </Space.Compact>
        <Checkbox checked={canceladas} onChange={(e) => setCanceladas(e.target.checked)}>
          Mostrar canceladas e remarcadas
        </Checkbox>
      </Space>

      <Spin spinning={carregando}>
          {visao === 'mes' ? (
            visaoMes()
          ) : (
          <div style={{ display: 'grid', gridTemplateColumns: visao === 'dia' ? '1fr' : 'repeat(auto-fit, minmax(230px, 1fr))', gap: 12 }}>
            {lista.map((dia) => {
              const { consultas, expediente, bloqueios } = porDia(dia);
              const fechado = bloqueios.some((b) => b.tipo === 'FERIADO' || b.tipo === 'RECESSO');
              return (
                <Card
                  key={dia}
                  size="small"
                  title={
                    <span style={{ fontWeight: dia === hoje ? 700 : 500 }}>
                      {nomeDiaSemana(dia)} {dataCurta(dia)}
                      {dia === hoje && <Tag color="green" style={{ marginInlineStart: 8 }}>Hoje</Tag>}
                    </span>
                  }
                  extra={
                    podeIncluir && !fechado && dia >= hoje ? (
                      <Button size="small" type="text" icon={<PlusOutlined />} title="Nova consulta neste dia" onClick={() => abrirNova(dia)} />
                    ) : null
                  }
                  style={{ background: fechado ? '#fafafa' : undefined }}
                >
                  {bloqueios
                    .filter((b) => b.tipo !== 'INTERVALO')
                    .map((b) => (
                      <Tag key={`${b.tipo}${b.inicio}`} color={b.tipo === 'FERIADO' ? 'red' : 'magenta'} style={{ marginBottom: 6 }}>
                        {b.tipo === 'FERIADO' ? 'Feriado' : 'Recesso'}: {b.descricao}
                      </Tag>
                    ))}
                  {expediente.map((e) => (
                    <div key={e.inicio + e.unidade.id} style={{ fontSize: 12, color: '#555', marginBottom: 2 }}>
                      Atende {horaBrasilia(e.inicio)}-{horaBrasilia(e.fim)} ({e.unidade.nome})
                    </div>
                  ))}
                  {bloqueios
                    .filter((b) => b.tipo === 'INTERVALO')
                    .map((b) => (
                      <div key={b.inicio} style={{ fontSize: 12, color: '#999', marginBottom: 2 }}>
                        Intervalo {horaBrasilia(b.inicio)}-{horaBrasilia(b.fim)}
                      </div>
                    ))}
                  {profissionalId && expediente.length === 0 && !fechado && consultas.length === 0 && (
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      Sem expediente
                    </Typography.Text>
                  )}
                  <div style={{ marginTop: 8 }}>{consultas.map(itemConsulta)}</div>
                </Card>
              );
            })}
          </div>
          )}
      </Spin>

      <NovaConsultaModal
        aberto={novaAberta}
        profissionalId={profissionalId}
        unidadeId={unidadeId}
        dia={diaNova}
        aoFechar={() => setNovaAberta(false)}
        aoSalvar={() => {
          setNovaAberta(false);
          void carregar();
        }}
      />
      <DetalheConsulta
        id={detalheId}
        aoFechar={fecharDetalhe}
        aoAlterar={(novoId) => {
          if (novoId) setDetalheId(novoId);
          void carregar();
        }}
      />
    </>
  );
}
