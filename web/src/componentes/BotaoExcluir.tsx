import { App, Button } from 'antd';
import { DeleteOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../api/cliente';
import { useAuth } from '../auth/AuthContext';

interface Props {
  /** Ex.: "o convênio", "a sala". Aparece na pergunta de confirmação. */
  artigoTipo: string;
  /** Nome do registro, como o usuário o conhece. */
  nome: string;
  /** Caminho da API, ex.: /convenios/3 */
  caminho: string;
  /** Aviso extra mostrado na confirmação (vínculos de cadastro que saem junto). */
  aviso?: string;
  aoExcluir: () => void | Promise<void>;
}

/** Excluir definitivamente: só aparece para o administrador. O servidor recusa (409) quando há histórico; aí o caminho é Inativar. */
export default function BotaoExcluir({ artigoTipo, nome, caminho, aviso, aoExcluir }: Props) {
  const { message, modal } = App.useApp();
  const { sessao } = useAuth();
  if (!sessao?.administrador) return null;

  function pedirConfirmacao() {
    modal.confirm({
      title: 'Excluir definitivamente',
      content: (
        <div>
          <div>
            Excluir {artigoTipo} <strong>{nome}</strong>? Esta ação não pode ser desfeita.
          </div>
          <div style={{ marginTop: 8 }}>
            Só é possível se não houver histórico (consultas, prontuário etc.); nesse caso, use Inativar.{aviso ? ` ${aviso}` : ''}
          </div>
        </div>
      ),
      okText: 'Excluir',
      okButtonProps: { danger: true },
      cancelText: 'Cancelar',
      onOk: async () => {
        try {
          await api.delete(caminho);
          message.success('Excluído.');
          await aoExcluir();
        } catch (e) {
          if (e instanceof ApiErro && e.codigo === 'REGISTRO_COM_VINCULOS') message.warning(e.message);
          else message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
        }
      }
    });
  }

  return <Button size="small" danger icon={<DeleteOutlined />} title="Excluir" onClick={pedirConfirmacao} />;
}
