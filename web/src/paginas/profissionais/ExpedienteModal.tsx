import { useEffect, useState } from 'react';
import { Alert, App, Modal, Spin } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import type { Horario, HorariosSaida, ProfissionalResumo } from '../../api/tipos';
import HorariosEditor from './HorariosEditor';

interface Props {
  profissional: ProfissionalResumo | null;
  aoFechar: () => void;
}

/** Consulta e substitui a grade de horários de um profissional já cadastrado. */
export default function ExpedienteModal({ profissional, aoFechar }: Props) {
  const { message, modal } = App.useApp();
  const [horarios, setHorarios] = useState<Horario[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [salvando, setSalvando] = useState(false);
  const [erros, setErros] = useState<string[]>([]);

  useEffect(() => {
    if (!profissional) return;
    setErros([]);
    setCarregando(true);
    api
      .get<Horario[]>(`/profissionais/${profissional.id}/horarios`)
      .then(setHorarios)
      .catch((e) => message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar o expediente.'))
      .finally(() => setCarregando(false));
  }, [profissional, message]);

  async function salvar() {
    if (!profissional) return;
    setErros([]);
    setSalvando(true);
    try {
      const saida = await api.put<HorariosSaida>(`/profissionais/${profissional.id}/horarios`, { horarios });
      message.success('Expediente salvo.');
      const fora = saida.consultasForaDoExpediente;
      if (fora.quantidade > 0) {
        modal.warning({
          title: 'Consultas fora do novo expediente',
          content: (
            <div>
              <p>
                {fora.quantidade} consulta(s) já marcada(s) ficaram fora da nova grade. Elas não foram alteradas nem canceladas; a
                recepção precisa tratá-las.
              </p>
              <ul style={{ paddingLeft: 18 }}>
                {fora.itens.map((c) => (
                  <li key={c.consultaId}>{new Date(c.dataInicio).toLocaleString('pt-BR')}</li>
                ))}
              </ul>
            </div>
          )
        });
      }
      aoFechar();
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        setErros(
          e.erros.map((x) => {
            const m = /^horarios\[(\d+)\]/.exec(x.campo);
            return m ? `Linha ${Number(m[1]) + 1}: ${x.mensagem}` : x.mensagem;
          })
        );
      } else {
        message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      }
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Modal
      open={profissional !== null}
      title={profissional ? `Expediente de ${profissional.nome}` : 'Expediente'}
      okText="Salvar expediente"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void salvar()}
      onCancel={aoFechar}
      maskClosable={false}
      destroyOnClose
      width={900}
    >
      <Spin spinning={carregando}>
        {erros.length > 0 && (
          <Alert
            type="error"
            showIcon
            style={{ marginBottom: 12 }}
            message="Confira o expediente"
            description={
              <ul style={{ margin: 0, paddingLeft: 18 }}>
                {erros.map((t, i) => (
                  <li key={i}>{t}</li>
                ))}
              </ul>
            }
          />
        )}
        <HorariosEditor
          value={horarios}
          onChange={setHorarios}
          unidades={(profissional?.unidades ?? []).map((u) => ({ value: u.id, label: u.nome }))}
        />
      </Spin>
    </Modal>
  );
}
