import { useEffect, useState } from 'react';
import { App, Button, Modal, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { api, ApiErro } from '../api/cliente';
import type { ListaEspera, Pagina } from '../api/tipos';
import { formatarDataHora } from '../util/formatos';

interface GuiaPendente {
  consultaId: string;
  inicio: string;
  paciente: string;
  convenio: { id: number; nome: string };
  numeroCarteirinha?: string | null;
  especialidade: { id: number; nome: string };
  profissional: { id: number; nome: string };
  valor: number;
}

interface PropsGuias {
  aberto: boolean;
  /** Início do painel (AAAA-MM-DD): as pendências contadas são as dessa data em diante. */
  dataInicial: string;
  onFechar: () => void;
  /** Abre a tela completa de Faturamento (opcional). */
  onAbrirTela?: () => void;
}

/** Guias de convênio pendentes, em uma janela sobre o painel. */
export function ModalGuiasPendentes({ aberto, dataInicial, onFechar, onAbrirTela }: PropsGuias) {
  const { message } = App.useApp();
  const [itens, setItens] = useState<GuiaPendente[]>([]);
  const [total, setTotal] = useState(0);
  const [carregando, setCarregando] = useState(false);

  useEffect(() => {
    if (!aberto) return;
    setCarregando(true);
    api
      .get<Pagina<GuiaPendente>>('/faturamento/guias', { status: 'PENDENTE', dataInicial, pagina: 1, tamanho: 100 })
      .then((r) => {
        setItens(r.itens);
        setTotal(r.total);
      })
      .catch((e) => message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar as guias.'))
      .finally(() => setCarregando(false));
  }, [aberto, dataInicial, message]);

  const colunas: TableColumnsType<GuiaPendente> = [
    { title: 'Data e hora', width: 140, render: (_, g) => <strong>{formatarDataHora(g.inicio)}</strong> },
    { title: 'Paciente', dataIndex: 'paciente' },
    { title: 'Convênio', render: (_, g) => g.convenio.nome },
    { title: 'Carteirinha', width: 130, render: (_, g) => g.numeroCarteirinha ?? '—' },
    { title: 'Profissional', render: (_, g) => g.profissional.nome },
    { title: 'Especialidade', render: (_, g) => g.especialidade.nome },
    { title: 'Guia', width: 100, render: () => <Tag color="gold">Pendente</Tag> }
  ];

  return (
    <Modal
      open={aberto}
      onCancel={onFechar}
      width={1000}
      title="Guias de convênio pendentes"
      footer={
        <>
          {onAbrirTela && <Button onClick={onAbrirTela}>Abrir o Faturamento</Button>}
          <Button type="primary" onClick={onFechar}>
            Fechar
          </Button>
        </>
      }
    >
      <Table<GuiaPendente>
        size="small"
        rowKey="consultaId"
        loading={carregando}
        columns={colunas}
        dataSource={itens}
        pagination={false}
        scroll={{ y: 420 }}
        locale={{ emptyText: 'Nenhuma guia pendente.' }}
      />
      {total > itens.length && (
        <Typography.Text type="secondary" style={{ display: 'block', marginTop: 8 }}>
          Mostrando {itens.length} de {total}. Veja todas no Faturamento.
        </Typography.Text>
      )}
    </Modal>
  );
}

interface PropsEspera {
  aberto: boolean;
  onFechar: () => void;
  onAbrirTela?: () => void;
}

/** Lista de espera (aguardando e com horário liberado), em uma janela sobre o painel. */
export function ModalListaEspera({ aberto, onFechar, onAbrirTela }: PropsEspera) {
  const { message } = App.useApp();
  const [itens, setItens] = useState<ListaEspera[]>([]);
  const [carregando, setCarregando] = useState(false);

  useEffect(() => {
    if (!aberto) return;
    setCarregando(true);
    Promise.all([
      api.get<Pagina<ListaEspera>>('/lista-espera', { status: 'NOTIFICADO', pagina: 1, tamanho: 100 }),
      api.get<Pagina<ListaEspera>>('/lista-espera', { status: 'AGUARDANDO', pagina: 1, tamanho: 100 })
    ])
      .then(([liberados, aguardando]) => setItens([...liberados.itens, ...aguardando.itens]))
      .catch((e) => message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar a lista de espera.'))
      .finally(() => setCarregando(false));
  }, [aberto, message]);

  const colunas: TableColumnsType<ListaEspera> = [
    { title: 'Paciente', render: (_, l) => l.paciente.nome },
    { title: 'Profissional', render: (_, l) => l.profissional?.nome ?? 'Qualquer' },
    { title: 'Especialidade', render: (_, l) => l.especialidade?.nome ?? '—' },
    { title: 'Unidade', render: (_, l) => l.unidade?.nome ?? '—' },
    { title: 'Na fila desde', width: 140, render: (_, l) => formatarDataHora(l.criadoEm) },
    {
      title: 'Situação',
      width: 150,
      render: (_, l) => (l.status === 'NOTIFICADO' ? <Tag color="orange">Horário liberado</Tag> : <Tag color="blue">Aguardando</Tag>)
    }
  ];

  return (
    <Modal
      open={aberto}
      onCancel={onFechar}
      width={1000}
      title="Lista de espera"
      footer={
        <>
          {onAbrirTela && <Button onClick={onAbrirTela}>Abrir a lista de espera</Button>}
          <Button type="primary" onClick={onFechar}>
            Fechar
          </Button>
        </>
      }
    >
      <Table<ListaEspera>
        size="small"
        rowKey="id"
        loading={carregando}
        columns={colunas}
        dataSource={itens}
        pagination={false}
        scroll={{ y: 420 }}
        locale={{ emptyText: 'Ninguém na lista de espera.' }}
      />
    </Modal>
  );
}
