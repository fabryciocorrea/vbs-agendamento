import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { App, Badge, Button, Drawer, Empty, List, Segmented, Tag, Typography } from 'antd';
import { BellOutlined, CheckOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../api/cliente';
import type { ContagemNaoLidas, NotificacaoInterna, Pagina, TipoNotificacao } from '../api/tipos';
import { useAuth } from '../auth/AuthContext';
import { formatarDataHora } from '../util/formatos';

const TIPOS: Record<TipoNotificacao, { texto: string; cor: string; rota: string }> = {
  HORARIO_DISPONIVEL: { texto: 'Horário liberado', cor: 'orange', rota: '/lista-espera' },
  SEM_RESPOSTA_48H: { texto: 'Sem resposta (48 h)', cor: 'gold', rota: '/agenda' },
  SEM_RESPOSTA_24H: { texto: 'Sem resposta (24 h)', cor: 'volcano', rota: '/agenda' },
  CONTATO_MANUAL: { texto: 'Contato manual', cor: 'blue', rota: '/agenda' },
  RESPOSTA_PACIENTE: { texto: 'Resposta do paciente', cor: 'cyan', rota: '/agenda' },
  FALHA_ENVIO: { texto: 'Falha no envio', cor: 'red', rota: '/agenda' },
  CREDENCIAMENTO: { texto: 'Credenciamento', cor: 'purple', rota: '/consultas-sem-credenciamento' }
};

const INTERVALO_MS = 60_000;

/** Sino da barra do sistema: contador de avisos não lidos (consulta a cada minuto) e gaveta com a lista. */
export default function SinoNotificacoes() {
  const { pode } = useAuth();
  const { message } = App.useApp();
  const navegar = useNavigate();
  const podeVer = pode('notificacao.interna', 'visualizar');
  const podeMarcar = pode('notificacao.interna', 'editar');

  const [total, setTotal] = useState(0);
  const [aberto, setAberto] = useState(false);
  const [filtro, setFiltro] = useState<'NAO_LIDAS' | 'TODAS'>('NAO_LIDAS');
  const [pagina, setPagina] = useState(1);
  const [dados, setDados] = useState<Pagina<NotificacaoInterna> | null>(null);
  const [carregando, setCarregando] = useState(false);

  const contar = useCallback(async () => {
    try {
      setTotal((await api.get<ContagemNaoLidas>('/notificacoes-internas/nao-lidas/contagem')).total);
    } catch {
      /* o contador é só um aviso: falha de rede não deve incomodar */
    }
  }, []);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setDados(await api.get<Pagina<NotificacaoInterna>>('/notificacoes-internas', { lida: filtro === 'NAO_LIDAS' ? false : undefined, pagina, tamanho: 10 }));
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os avisos.');
    } finally {
      setCarregando(false);
    }
  }, [filtro, pagina, message]);

  useEffect(() => {
    if (!podeVer) return;
    void contar();
    const id = window.setInterval(() => {
      if (!document.hidden) void contar();
    }, INTERVALO_MS);
    return () => window.clearInterval(id);
  }, [podeVer, contar]);

  useEffect(() => {
    if (aberto) void carregar();
  }, [aberto, carregar]);

  if (!podeVer) return null;

  async function marcarLida(n: NotificacaoInterna) {
    try {
      await api.patch<NotificacaoInterna>(`/notificacoes-internas/${n.id}/leitura`, {});
      await Promise.all([carregar(), contar()]);
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível marcar como lido.');
    }
  }

  async function abrir(n: NotificacaoInterna) {
    if (podeMarcar && !n.lida) await marcarLida(n);
    setAberto(false);
    navegar(TIPOS[n.tipo].rota);
  }

  return (
    <>
      <Badge count={total} size="small" overflowCount={99}>
        <Button shape="circle" icon={<BellOutlined />} aria-label="Avisos" title="Avisos" onClick={() => setAberto(true)} />
      </Badge>
      <Drawer title="Avisos da recepção" width={440} open={aberto} onClose={() => setAberto(false)}>
        <Segmented
          style={{ marginBottom: 12 }}
          value={filtro}
          onChange={(v) => {
            setPagina(1);
            setFiltro(v as 'NAO_LIDAS' | 'TODAS');
          }}
          options={[
            { value: 'NAO_LIDAS', label: 'Não lidos' },
            { value: 'TODAS', label: 'Todos' }
          ]}
        />
        <List<NotificacaoInterna>
          loading={carregando}
          dataSource={dados?.itens ?? []}
          locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={filtro === 'NAO_LIDAS' ? 'Nenhum aviso pendente.' : 'Nenhum aviso.'} /> }}
          pagination={{ current: pagina, pageSize: 10, total: dados?.total ?? 0, showSizeChanger: false, size: 'small', hideOnSinglePage: true, onChange: setPagina }}
          renderItem={(n) => (
            <List.Item
              style={{ opacity: n.lida ? 0.65 : 1 }}
              actions={[
                <Button key="abrir" type="link" size="small" onClick={() => void abrir(n)}>
                  Abrir
                </Button>,
                ...(podeMarcar && !n.lida
                  ? [
                      <Button key="lida" type="link" size="small" icon={<CheckOutlined />} onClick={() => void marcarLida(n)}>
                        Lido
                      </Button>
                    ]
                  : [])
              ]}
            >
              <List.Item.Meta
                title={
                  <>
                    <Tag color={TIPOS[n.tipo].cor}>{TIPOS[n.tipo].texto}</Tag>
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      {formatarDataHora(n.criadaEm)}
                    </Typography.Text>
                  </>
                }
                description={
                  <>
                    <div>{n.mensagem}</div>
                    {n.consulta && (
                      <div>
                        {n.consulta.paciente.nome} · consulta em {formatarDataHora(n.consulta.inicio)}
                      </div>
                    )}
                    {n.lida && n.lidaPor && (
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                        Lido por {n.lidaPor.nomeCompleto} em {formatarDataHora(n.lidaEm)}
                      </Typography.Text>
                    )}
                  </>
                }
              />
            </List.Item>
          )}
        />
      </Drawer>
    </>
  );
}
