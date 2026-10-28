import { useEffect, useState } from 'react';
import { App, Form, Modal, Select, Table, Tag } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import type { HistoricoProfissional, ItemApoio, Paciente } from '../../api/tipos';
import { formatarDataHora } from '../../util/formatos';

const OBRIGATORIO = 'Campo Não Preenchido';

/** Troca do profissional responsável (RN18). O profissional anterior perde o acesso ao prontuário (RN16). */
export function TrocaProfissionalModal({
  aberto,
  pacienteId,
  profissionalAtualId,
  aoFechar,
  aoTrocar
}: {
  aberto: boolean;
  pacienteId: string;
  profissionalAtualId: number;
  aoFechar: () => void;
  aoTrocar: (paciente: Paciente) => void;
}) {
  const { message } = App.useApp();
  const [form] = Form.useForm<{ profissionalId: number }>();
  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);
  const [salvando, setSalvando] = useState(false);

  useEffect(() => {
    if (!aberto) return;
    api
      .get<ItemApoio[]>('/apoio/profissionais')
      .then(setProfissionais)
      .catch(() => message.error('Não foi possível carregar a lista de profissionais.'));
  }, [aberto, message]);

  async function enviar() {
    let v: { profissionalId: number };
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    setSalvando(true);
    try {
      const atualizado = await api.put<Paciente>(`/pacientes/${pacienteId}/profissional-responsavel`, { profissionalId: v.profissionalId });
      message.success('Profissional responsável trocado.');
      aoTrocar(atualizado);
    } catch (e) {
      if (e instanceof ApiErro && e.codigo === 'PROFISSIONAL_JA_RESPONSAVEL') {
        form.setFields([{ name: 'profissionalId', errors: [e.message] }]);
      } else if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields([{ name: 'profissionalId', errors: [e.erros[0].mensagem] }]);
      } else {
        message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      }
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Modal
      open={aberto}
      title="Trocar profissional responsável"
      okText="Trocar"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
    >
      <p style={{ marginTop: 0 }}>
        A troca vale na hora e fica registrada no histórico. O profissional anterior deixa de ver e de preencher o prontuário; o novo
        passa a ver todos os registros, inclusive os anteriores.
      </p>
      <Form form={form} layout="vertical" preserve={false}>
        <Form.Item name="profissionalId" label="Novo profissional responsável" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select
            showSearch
            optionFilterProp="label"
            placeholder="Selecione"
            options={profissionais
              .filter((p) => p.id !== profissionalAtualId)
              .map((p) => ({ value: p.id, label: p.complemento ? `${p.nome} (${p.complemento})` : p.nome }))}
          />
        </Form.Item>
      </Form>
    </Modal>
  );
}

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
          { title: 'Alterado por', width: 160, render: (_, r) => r.alteradoPor?.nomeCompleto ?? '—' }
        ]}
      />
    </Modal>
  );
}
