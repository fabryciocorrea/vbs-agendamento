import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { App } from 'antd';
import { api, definirAoPerderSessao, guardarToken, obterToken } from '../api/cliente';
import type { SessaoSaida, UsuarioAtualSaida } from '../api/tipos';

export type Acao = 'visualizar' | 'incluir' | 'editar' | 'excluir' | 'exportar';

interface AuthValor {
  sessao: UsuarioAtualSaida | null;
  carregando: boolean;
  entrar: (nomeUsuario: string, senha: string) => Promise<void>;
  sair: () => Promise<void>;
  pode: (tela: string, acao: Acao) => boolean;
}

const AuthContext = createContext<AuthValor | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const { message } = App.useApp();
  const [sessao, setSessao] = useState<UsuarioAtualSaida | null>(null);
  const [carregando, setCarregando] = useState<boolean>(() => obterToken() !== null);

  // Ao abrir a página com token guardado, confirma no servidor se a sessão ainda vale.
  useEffect(() => {
    if (!obterToken()) return;
    api
      .get<UsuarioAtualSaida>('/auth/eu')
      .then(setSessao)
      .catch(() => guardarToken(null))
      .finally(() => setCarregando(false));
  }, []);

  // Qualquer 401 numa rota autenticada derruba a sessão local.
  useEffect(() => {
    definirAoPerderSessao(() => {
      guardarToken(null);
      setSessao((atual) => {
        if (atual) message.warning('Sua sessão expirou. Entre novamente.');
        return null;
      });
    });
    return () => definirAoPerderSessao(null);
  }, [message]);

  const entrar = useCallback(async (nomeUsuario: string, senha: string) => {
    const saida = await api.postPublico<SessaoSaida>('/auth/login', { nomeUsuario, senha });
    guardarToken(saida.accessToken);
    try {
      setSessao(await api.get<UsuarioAtualSaida>('/auth/eu'));
    } catch (e) {
      guardarToken(null);
      throw e;
    }
  }, []);

  const sair = useCallback(async () => {
    try {
      await api.post('/auth/logout');
    } catch {
      // Mesmo que o servidor não responda, a sessão local é encerrada.
    }
    guardarToken(null);
    setSessao(null);
  }, []);

  const pode = useCallback(
    (tela: string, acao: Acao) => {
      const p = sessao?.permissoes.telas.find((t) => t.telaCodigo === tela);
      if (!p) return false;
      switch (acao) {
        case 'visualizar': return p.podeVisualizar;
        case 'incluir': return p.podeIncluir;
        case 'editar': return p.podeEditar;
        case 'excluir': return p.podeExcluir;
        case 'exportar': return p.podeExportar;
      }
    },
    [sessao]
  );

  const valor = useMemo(() => ({ sessao, carregando, entrar, sair, pode }), [sessao, carregando, entrar, sair, pode]);
  return <AuthContext.Provider value={valor}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthValor {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth deve ser usado dentro de AuthProvider');
  return ctx;
}
