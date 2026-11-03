import { useEffect, useState } from 'react';
import { App, Modal, Table, Tag } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import type { HistoricoProfissional } from '../../api/tipos';
import { formatarDataHora } from '../../util/formatos';

/** Todos os profissionais que já atenderam o paciente, do mais recente ao mais antigo. */
export function HistoricoProfissionaisModal({ aberto, pacienteId, aoFechar }: { aberto: boolean; pacienteId: string; aoFechar: () => void }) {
  const { message } = App.useApp();
  const [itens, setItens] = useState<HistoricoProfissional[]>([]);
  const [carregando, setCarregando] = useState(false);

  useEffect(() => {
    if (!aberto) return;
    setCarregando(true);
    api
      .get<HistoricoProfissional[]>(`/pacientes/${pacienteId}/profissionais-historico`)
      .then(setItens)
      .catch((e) => message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar o histórico.'))
      .finally(() => setCarregando(false));
  }, [aberto, pacienteId, message]);

  return (
    <Modal open={aberto} title="Histórico de profissionais" footer={null} onCancel={aoFechar} destroyOnClose width={720}>
      <Table<HistoricoProfissional>
        rowKey={(r) => `${r.profissional.id}-${r.dataInicio}`}
        size="small"
        loading={carregando}
        dataSource={itens}
        pagination={false}
        locale={{ emptyText: 'Sem histórico.' }}
        columns={[
          {
            title: 'Profissional',
            render: (_, r) => (
              <>
                {r.profissional.nome} {!r.dataFim && <Tag color="green">Atual</Tag>}
              </>
            )
          },
          { title: 'Início', width: 140, render: (_, r) => formatarDataHora(r.dataInicio) },
          { title: 'Fim', width: 140, render: (_, r) => (r.dataFim ? formatarDataHora(r.dataFim) : '—') },
          { title: 'Alterado por', width: 160, render: (_, r) => r.alteradoPor?.nomeCompleto ?? '—' },
          { title: 'Motivo', render: (_, r) => r.motivo ?? '—' }
        ]}
      />
    </Modal>
  );
}
