import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Alert, App, Button, Card, Col, Empty, Row, Space, Spin, Tag } from 'antd';
import { CalendarOutlined, EnvironmentOutlined, FileTextOutlined, HomeOutlined, ReloadOutlined, VideoCameraOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../api/cliente';
import { useAuth } from '../auth/AuthContext';
import CabecalhoPagina from '../layout/CabecalhoPagina';
import { hojeBrasilia, horaBrasilia, nomeDiaSemana } from './agenda/datas';
import { CORES_STATUS, NOMES_STATUS } from './agenda/tipos';
import type { AgendaSaida, ConsultaResumo } from './agenda/tipos';

const ATUALIZA_A_CADA_MS = 60_000;
const ENCERRADAS = new Set(['CANCELADA', 'REMARCADA', 'FALTA']);

function dataPorExtenso(dia: string): string {
  const [a, m, d] = dia.split('-');
  return `${nomeDiaSemana(dia)}, ${d}/${m}/${a}`;
}

function Indicador({ titulo, valor, cor }: { titulo: string; valor: number; cor: string }) {
  return (
    <Card size="small">
      <div className="indicador">
        <span className="indicador-icone" style={{ color: cor, background: `${cor}22` }} aria-hidden="true">
          <CalendarOutlined />
        </span>
        <div>
          <span className="indicador-titulo">{titulo}</span>
          <span className="indicador-valor" style={{ color: cor }}>
            {valor}
          </span>
        </div>
      </div>
    </Card>
  );
}

/** Início do usuário vinculado a profissional: as consultas de hoje, só as dele. */
export default function MeuDia() {
  const { sessao, pode } = useAuth();
  const { message } = App.useApp();
  const navegar = useNavigate();
  const profissionalId = sessao?.profissionalId ?? null;
  const podeAgenda = pode('agenda.profissional', 'visualizar');
  const podeProntuario = pode('prontuario.registro', 'visualizar');
  const [consultas, setConsultas] = useState<ConsultaResumo[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const [atualizadoEm, setAtualizadoEm] = useState('');
  const hoje = hojeBrasilia();

  const carregar = useCallback(
    async (silencioso = false) => {
      if (!profissionalId || !podeAgenda) return;
      if (!silencioso) setCarregando(true);
      try {
        const r = await api.get<AgendaSaida>(`/agenda/profissionais/${profissionalId}`, { dataInicial: hoje, dataFinal: hoje });
        setConsultas(r.consultas);
        setErro(null);
        setAtualizadoEm(new Date().toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }));
      } catch (e) {
        const texto = e instanceof ApiErro ? e.message : 'Não foi possível carregar as consultas de hoje.';
        if (silencioso) setErro(texto);
        else message.error(texto);
      } finally {
        setCarregando(false);
      }
    },
    [profissionalId, podeAgenda, hoje, message]
  );

  useEffect(() => {
    void carregar();
    const t = window.setInterval(() => void carregar(true), ATUALIZA_A_CADA_MS);
    return () => window.clearInterval(t);
  }, [carregar]);

  const doDia = useMemo(
    () => consultas.filter((c) => !ENCERRADAS.has(c.status)).sort((a, b) => a.inicio.localeCompare(b.inicio)),
    [consultas]
  );
  const concluidas = doDia.filter((c) => c.status === 'CONCLUIDA').length;
  const emAtendimento = doDia.filter((c) => c.status === 'EM_ATENDIMENTO').length;
  const restantes = doDia.length - concluidas - emAtendimento;
  // Próxima consulta: a primeira ainda não iniciada cujo horário não passou há mais de 15 minutos (ou a que está em curso).
  const proxima = useMemo(() => {
    const emCurso = doDia.find((c) => c.status === 'EM_ATENDIMENTO');
    if (emCurso) return emCurso.id;
    const limite = Date.now() - 15 * 60_000;
    return doDia.find((c) => c.status !== 'CONCLUIDA' && new Date(c.inicio).getTime() >= limite)?.id;
  }, [doDia]);

  const nome = sessao?.usuario.nomeCompleto ?? '';

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <CabecalhoPagina icone={<HomeOutlined />} titulo={`Bem-vindo, ${nome}`} subtitulo={`Seu dia: ${dataPorExtenso(hoje)}`} />
        {podeAgenda && (
          <Space>
            {atualizadoEm && <span style={{ color: 'var(--vbs-texto-suave)', fontSize: 12 }}>Atualizado às {atualizadoEm}</span>}
            <Button icon={<ReloadOutlined />} onClick={() => void carregar()} loading={carregando} title="Atualizar agora" />
            <Button type="primary" icon={<CalendarOutlined />} onClick={() => navegar('/agenda')}>
              Abrir agenda
            </Button>
          </Space>
        )}
      </Space>

      {!podeAgenda ? (
        <Alert type="info" showIcon message="Seu perfil não tem acesso à agenda. Use o menu à esquerda para acessar as telas liberadas." />
      ) : (
        <>
          {erro && <Alert type="warning" showIcon message={erro} style={{ marginBottom: 16 }} />}
          <Row gutter={[12, 12]} style={{ marginBottom: 16 }}>
            <Col xs={12} md={6}>
              <Indicador titulo="Consultas hoje" valor={doDia.length} cor="#2b8a62" />
            </Col>
            <Col xs={12} md={6}>
              <Indicador titulo="A atender" valor={restantes} cor="#1677ff" />
            </Col>
            <Col xs={12} md={6}>
              <Indicador titulo="Em atendimento" valor={emAtendimento} cor="#fa8c16" />
            </Col>
            <Col xs={12} md={6}>
              <Indicador titulo="Concluídas" valor={concluidas} cor="#52c41a" />
            </Col>
          </Row>

          <Card title="Consultas de hoje" extra={carregando ? <Spin size="small" /> : null}>
            {doDia.length === 0 ? (
              <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Nenhuma consulta para hoje." />
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                {doDia.map((c) => {
                  const destaque = c.id === proxima;
                  return (
                    <div
                      key={c.id}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 16,
                        flexWrap: 'wrap',
                        padding: '12px 16px',
                        borderRadius: 14,
                        border: destaque ? '2px solid var(--vbs-destaque)' : '1px solid var(--vbs-borda-suave)',
                        background: 'var(--vbs-superficie-alt)'
                      }}
                    >
                      <div style={{ minWidth: 92 }}>
                        <div style={{ fontSize: 18, fontWeight: 700 }}>{horaBrasilia(c.inicio)}</div>
                        <div style={{ fontSize: 12, color: 'var(--vbs-texto-suave)' }}>até {horaBrasilia(c.fim)}</div>
                      </div>
                      <div style={{ flex: '1 1 220px', minWidth: 0 }}>
                        <div style={{ fontWeight: 600 }}>{c.paciente.nome}</div>
                        <div style={{ fontSize: 12.5, color: 'var(--vbs-texto-medio)' }}>
                          {c.especialidade.nome}
                          {' · '}
                          {c.modalidade === 'ONLINE' ? (
                            <>
                              <VideoCameraOutlined /> Online
                            </>
                          ) : (
                            <>
                              <EnvironmentOutlined /> {c.unidade.nome}
                              {c.sala ? ` · Sala ${c.sala.numero}` : ''}
                            </>
                          )}
                          {' · '}
                          {c.tipoAtendimento === 'CONVENIO' ? c.convenio?.nome ?? 'Convênio' : 'Particular'}
                        </div>
                      </div>
                      <Space wrap>
                        {destaque && <Tag color="green">{c.status === 'EM_ATENDIMENTO' ? 'Em curso' : 'Próxima'}</Tag>}
                        {c.encaixe && <Tag color="orange">Encaixe</Tag>}
                        <Tag color={CORES_STATUS[c.status]}>{NOMES_STATUS[c.status]}</Tag>
                        {podeProntuario && c.pacienteResponsavelId === profissionalId && (
                          <Button size="small" icon={<FileTextOutlined />} onClick={() => navegar(`/pacientes/${c.paciente.id}/prontuario`)}>
                            Prontuário
                          </Button>
                        )}
                      </Space>
                    </div>
                  );
                })}
              </div>
            )}
          </Card>
        </>
      )}
    </>
  );
}
