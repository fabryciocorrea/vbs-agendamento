import { useEffect, useState } from 'react';
import { Alert, App, Form, Input, Modal, Select } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import type { ItemApoio } from '../../api/tipos';

const OBRIGATORIO = 'Campo Não Preenchido';

interface Valores {
  profissionalId: number;
  motivo: string;
}

/**
 * Transferência de prontuário: o paciente passa a ser de outro profissional (RN18). O profissional anterior deixa de ver e de preencher
 * o prontuário (RN16); o novo vê todos os registros, que seguem em nome de quem os escreveu (RN19).
 */
export default function TransferenciaProntuarioModal({
  aberto, pacienteId, pacienteNome, profissionalAtual, aoFechar, aoTransferir
}: {
  aberto: boolean;
  pacienteId: string;
  pacienteNome: string;
  profissionalAtual: { id: number; nome: string };
  aoFechar: () => void;
  aoTransferir: (novoResponsavel: { id: number; nome: string }) => void;
}) {
  const { message, modal } = App.useApp();
  const [form] = Form.useForm<Valores>();
  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);
  const [salvando, setSalvando] = useState(false);
  const [erroGeral, setErroGeral] = useState('');

  useEffect(() => {
    if (!aberto) return;
    setErroGeral('');
    form.resetFields();
    api
      .get<ItemApoio[]>('/apoio/profissionais')
      .then(setProfissionais)
      .catch(() => setErroGeral('Não foi possível carregar a lista de profissionais.'));
  }, [aberto, form]);

  async function enviar(v: Valores) {
    setSalvando(true);
    setErroGeral('');
    try {
      const r = await api.post<{ profissionalResponsavel: { id: number; nome: string }; consultasFuturasComProfissionalAnterior: number }>(
        `/pacientes/${pacienteId}/prontuario/transferencia`,
        { profissionalId: v.profissionalId, motivo: v.motivo }
      );
      message.success('Prontuário transferido.');
      if (r.consultasFuturasComProfissionalAnterior > 0) {
        const n = r.consultasFuturasComProfissionalAnterior;
        modal.warning({
          title: 'Há consultas futuras com o profissional anterior',
          content: `${pacienteNome} tem ${n} consulta${n > 1 ? 's' : ''} agendada${n > 1 ? 's' : ''} ou confirmada${n > 1 ? 's' : ''} com ${profissionalAtual.nome}. Elas não mudaram: remarque ou cancele na Agenda, se for o caso.`,
          okText: 'Entendi'
        });
      }
      aoTransferir(r.profissionalResponsavel);
    } catch (e) {
      if (e instanceof ApiErro) {
        const campos = e.erros
          .filter((x) => x.campo === 'profissionalId' || x.campo === 'motivo')
          .map((x) => ({ name: x.campo as keyof Valores, errors: [x.mensagem] }));
        if (e.codigo === 'PROFISSIONAL_JA_RESPONSAVEL') form.setFields([{ name: 'profissionalId', errors: [e.message] }]);
        else if (campos.length > 0) form.setFields(campos);
        else setErroGeral(e.message);
      } else setErroGeral('Não foi possível transferir o prontuário.');
    } finally {
      setSalvando(false);
    }
  }

  async function confirmar() {
    let v: Valores;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    const novo = profissionais.find((p) => p.id === v.profissionalId)?.nome ?? 'o novo profissional';
    modal.confirm({
      title: 'Transferir o prontuário?',
      content: `${profissionalAtual.nome} deixa de ver e de preencher o prontuário de ${pacienteNome}. ${novo} passa a ser o responsável e vê todos os registros, inclusive os anteriores.`,
      okText: 'Transferir',
      cancelText: 'Voltar',
      onOk: () => enviar(v)
    });
  }

  return (
    <Modal
      title="Transferência de prontuário"
      open={aberto}
      okText="Transferir"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void confirmar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
    >
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        message="Paciente: seguirá com outro profissional"
        description="Os registros já feitos continuam em nome de quem os escreveu. As consultas já agendadas não mudam de profissional."
      />
      <Form form={form} layout="vertical" requiredMark preserve={false}>
        {erroGeral && <Alert type="error" showIcon message={erroGeral} style={{ marginBottom: 16 }} />}
        <Form.Item label="Profissional responsável atual">
          <Input value={profissionalAtual.nome} disabled />
        </Form.Item>
        <Form.Item name="profissionalId" label="Novo profissional responsável" rules={[{ required: true, message: OBRIGATORIO }]}>
          <Select<number>
            showSearch
            optionFilterProp="label"
            placeholder="Selecione o profissional"
            options={profissionais
              .filter((p) => p.id !== profissionalAtual.id)
              .map((p) => ({ value: p.id, label: p.complemento ? `${p.nome} (${p.complemento})` : p.nome }))}
          />
        </Form.Item>
        <Form.Item
          name="motivo"
          label="Motivo da transferência"
          rules={[{ required: true, whitespace: true, message: OBRIGATORIO }, { min: 10, message: 'Mínimo de 10 caracteres' }]}
        >
          <Input.TextArea rows={3} maxLength={500} showCount placeholder="Ex.: o paciente pediu para passar a consultar com outro profissional. Não escreva texto clínico." />
        </Form.Item>
      </Form>
    </Modal>
  );
}
