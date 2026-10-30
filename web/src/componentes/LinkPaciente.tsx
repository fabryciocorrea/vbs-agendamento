import { useState } from 'react';
import { App, Button } from 'antd';
import { api, ApiErro } from '../api/cliente';
import type { Paciente } from '../api/tipos';
import { useAuth } from '../auth/AuthContext';
import FormularioPaciente from '../paginas/pacientes/FormularioPaciente';

interface Props {
  /** Identificador (guid) do paciente. */
  id: string;
  nome: string;
  /** Chamado depois de salvar o cadastro, para a tela recarregar a lista. */
  aoSalvar?: () => void;
}

/** Nome do paciente que abre o cadastro numa janela sobre a tela atual. Sem permissão de visualizar, mostra só o texto. */
export default function LinkPaciente({ id, nome, aoSalvar }: Props) {
  const { message } = App.useApp();
  const { pode } = useAuth();
  const [paciente, setPaciente] = useState<Paciente | null>(null);

  if (!pode('paciente.cadastro', 'visualizar')) return <>{nome}</>;

  // A consulta ao cadastro completo fica registrada no log.
  async function abrir() {
    try {
      setPaciente(await api.get<Paciente>(`/pacientes/${id}`));
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível abrir o cadastro do paciente.');
    }
  }

  return (
    <>
      <Button type="link" style={{ padding: 0, height: 'auto', whiteSpace: 'normal', textAlign: 'left' }} title="Abrir o cadastro do paciente" onClick={() => void abrir()}>
        {nome}
      </Button>
      <FormularioPaciente
        aberto={!!paciente}
        paciente={paciente}
        aoFechar={() => setPaciente(null)}
        aoSalvar={() => {
          setPaciente(null);
          aoSalvar?.();
        }}
      />
    </>
  );
}
