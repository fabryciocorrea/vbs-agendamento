import { useCallback, useEffect, useState } from 'react';
import { App, Button, Col, Form, Modal, Radio, Row, Select, Table, Tag } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import type { ConsentimentoHistorico } from '../../api/tipos';
import { formatarDataHora } from '../../util/formatos';

const OBRIGATORIO = 'Campo Não Preenchido';
const ROTULO: Record<string, string> = {
  TRATAMENTO_DADOS: 'Tratamento de dados',
  CONTATO_SMS_WHATSAPP: 'Contato por SMS e WhatsApp'
};
const ORIGEM: Record<string, string> = { PRESENCIAL: 'Presencial', TELEFONE: 'Telefone', SISTEMA: 'Sistema' };

interface Valores {
  finalidade: 'TRATAMENTO_DADOS' | 'CONTATO_SMS_WHATSAPP';
  concedido: boolean;
  origem: 'PRESENCIAL' | 'TELEFONE';
}

/** Estado atual de cada finalidade: a linha mais recente (a lista vem da mais nova para a mais antiga). */
export function estadoAtual(historico: ConsentimentoHistorico[]): ConsentimentoHistorico[] {
  const vistos = new Set<string>();
  const atuais: ConsentimentoHistorico[] = [];
  for (const c of historico) {
    if (!vistos.has(c.finalidade)) {
      vistos.add(c.finalidade);
      atuais.push(c);
    }
  }
  return atuais.sort((a, b) => a.finalidade.localeCompare(b.finalidade));
}

/** Histórico de consentimentos e registro de uma mudança (E3): cada registro cria uma linha nova, sem apagar as anteriores. */
export default function ConsentimentosModal({
  aberto,
  pacienteId,
  podeRegistrar,
  menor,
  aoFechar,
  aoMudar
}: {
  aberto: boolean;
  pacienteId: string;
  podeRegistrar: boolean;
  /** Paciente menor de idade: quem decide é o responsável legal. */
  menor?: boolean;
  aoFechar: () => void;
  /** Chamado com o histórico atualizado depois de cada registro. */
  aoMudar: (historico: ConsentimentoHistorico[]) => void;
}) {
  const { message } = App.useApp();
  const [form] = Form.useForm<Valores>();
  const [itens, setItens] = useState<ConsentimentoHistorico[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [salvando, setSalvando] = useState(false);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      const lista = await api.get<ConsentimentoHistorico[]>(`/pacientes/${pacienteId}/consentimentos`);
      setItens(lista);
      return lista;
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os consentimentos.');
      return null;
    } finally {
      setCarregando(false);
    }
  }, [pacienteId, message]);

  useEffect(() => {
    if (aberto) void carregar();
  }, [aberto, carregar]);

  async function registrar() {
    let v: Valores;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    setSalvando(true);
    try {
      await api.post(`/pacientes/${pacienteId}/consentimentos`, v);
      message.success('Consentimento registrado.');
      form.resetFields();
      const lista = await carregar();
      if (lista) aoMudar(lista);
    } catch (e) {
      if (e instanceof ApiErro && e.codigo === 'TRATAMENTO_DADOS_PACIENTE_ATIVO') message.warning(e.message);
      else if (e instanceof ApiErro && e.erros.length > 0) form.setFields(e.erros.map((x) => ({ name: x.campo as keyof Valores, errors: [x.mensagem] })));
      else message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Modal open={aberto} title="Consentimentos (LGPD)" footer={null} onCancel={aoFechar} destroyOnClose width={820}>
      {podeRegistrar && (
        <Form<Valores> form={form} layout="vertical" preserve={false} style={{ marginBottom: 8 }}>
          <Row gutter={16}>
            <Col xs={24} md={8}>
              <Form.Item name="finalidade" label="Finalidade" rules={[{ required: true, message: OBRIGATORIO }]}>
                <Select
                  placeholder="Selecione"
                  options={[
                    { value: 'CONTATO_SMS_WHATSAPP', label: ROTULO.CONTATO_SMS_WHATSAPP },
                    { value: 'TRATAMENTO_DADOS', label: ROTULO.TRATAMENTO_DADOS }
                  ]}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="concedido" label={menor ? 'Decisão do responsável legal' : 'Decisão do paciente'} rules={[{ required: true, message: OBRIGATORIO }]}>
                <Radio.Group>
                  <Radio value={true}>Concede</Radio>
                  <Radio value={false}>Revoga</Radio>
                </Radio.Group>
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="origem" label="Como foi dado" rules={[{ required: true, message: OBRIGATORIO }]}>
                <Select
                  placeholder="Selecione"
                  options={[
                    { value: 'PRESENCIAL', label: 'Presencialmente' },
                    { value: 'TELEFONE', label: 'Por telefone' }
                  ]}
                />
              </Form.Item>
            </Col>
          </Row>
          <Button type="primary" loading={salvando} onClick={() => void registrar()}>
            Registrar
          </Button>
        </Form>
      )}
      <Table<ConsentimentoHistorico>
        style={{ marginTop: 16 }}
        rowKey={(r) => `${r.finalidade}-${r.dataRegistro}`}
        size="small"
        loading={carregando}
        dataSource={itens}
        pagination={false}
        locale={{ emptyText: 'Nenhum registro.' }}
        columns={[
          { title: 'Finalidade', render: (_, r) => ROTULO[r.finalidade] ?? r.finalidade },
          { title: 'Decisão', width: 120, render: (_, r) => (r.concedido ? <Tag color="green">Concedido</Tag> : <Tag>Revogado</Tag>) },
          { title: 'Origem', width: 110, render: (_, r) => ORIGEM[r.origem] ?? r.origem },
          { title: 'Data e hora', width: 140, render: (_, r) => formatarDataHora(r.dataRegistro) },
          { title: 'Registrado por', width: 150, render: (_, r) => r.registradoPor?.nomeCompleto ?? '—' }
        ]}
      />
    </Modal>
  );
}
