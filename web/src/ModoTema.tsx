import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { ConfigProvider } from 'antd';
import ptBR from 'antd/locale/pt_BR';
import { temaClaro, temaEscuro } from './tema';
import './tema-escuro.css';
import './layout-moderno.css';

const CHAVE = 'vbs.tema';

interface ModoTemaValor {
  escuro: boolean;
  alternar: () => void;
  definir: (escuro: boolean) => void;
}

const Contexto = createContext<ModoTemaValor | null>(null);

function inicial(): boolean {
  try {
    const salvo = localStorage.getItem(CHAVE);
    if (salvo === 'escuro') return true;
    if (salvo === 'claro') return false;
  } catch {
    /* sem armazenamento do navegador: segue o sistema */
  }
  return typeof window !== 'undefined' && window.matchMedia?.('(prefers-color-scheme: dark)').matches === true;
}

/** Guarda a escolha claro/escuro (no navegador), aplica o tema do Ant Design e as cores dos estilos inline. */
export function ModoTemaProvider({ children }: { children: ReactNode }) {
  const [escuro, setEscuro] = useState(inicial);

  useEffect(() => {
    document.documentElement.dataset.tema = escuro ? 'escuro' : 'claro';
  }, [escuro]);

  const definir = useCallback((valor: boolean) => {
    setEscuro(valor);
    try {
      localStorage.setItem(CHAVE, valor ? 'escuro' : 'claro');
    } catch {
      /* a escolha vale só até recarregar a página */
    }
  }, []);

  const valor = useMemo<ModoTemaValor>(() => ({ escuro, definir, alternar: () => definir(!escuro) }), [escuro, definir]);

  return (
    <Contexto.Provider value={valor}>
      <ConfigProvider locale={ptBR} theme={escuro ? temaEscuro : temaClaro}>
        {children}
      </ConfigProvider>
    </Contexto.Provider>
  );
}

export function useModoTema(): ModoTemaValor {
  const v = useContext(Contexto);
  if (!v) throw new Error('useModoTema fora do ModoTemaProvider');
  return v;
}
