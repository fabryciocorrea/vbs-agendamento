import { useState } from 'react';
import { App, Button } from 'antd';
import { api, ApiErro } from '../api/cliente';
import type { Convenio } from '../api/tipos';
import { useAuth } from '../auth/AuthContext';
import { FormularioConvenio } from '../paginas/convenios/ConveniosPagina';

interface Props {
  id: number;
  nome: string;
  /** Chamado depois de salvar o cadastro, para a tela recarregar a lista. */
  aoSalvar?: () => void;
}

/** Nome do convênio que abre o cadastro numa janela sobre a tela atual. Sem permissão de visualizar, mostra só o texto. */
export default function LinkConvenio({ id, nome, aoSalvar }: Props) {
  const { message } = App.useApp();
  const { pode } = useAuth();
  const [convenio, setConvenio] = useState<Convenio | null>(null);

  if (!pode('convenio.cadastro', 'visualizar')) return <>{nome}</>;

  async function abrir() {
    try {
      setConvenio(await api.get<Convenio>(`/convenios/${id}`));
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível abrir o cadastro do convênio.');
    }
  }

  return (
    <>
      <Button type="link" style={{ padding: 0, height: 'auto', whiteSpace: 'normal', textAlign: 'left' }} title="Abrir o cadastro do convênio" onClick={() => void abrir()}>
        {nome}
      </Button>
      <FormularioConvenio
        aberto={!!convenio}
        convenio={convenio}
        aoFechar={() => setConvenio(null)}
        aoSalvar={() => {
          setConvenio(null);
          aoSalvar?.();
        }}
      />
    </>
  );
}
