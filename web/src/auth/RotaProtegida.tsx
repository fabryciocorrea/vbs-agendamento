import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { Spin } from 'antd';
import { useAuth } from './AuthContext';
import SemPermissao from '../paginas/SemPermissao';

/** Sem sessão vai para o login; com `tela`, exige também a permissão de visualizar. */
export default function RotaProtegida({ children, tela }: { children: ReactNode; tela?: string }) {
  const { sessao, carregando, pode } = useAuth();
  const local = useLocation();

  if (carregando) {
    return (
      <div className="centralizado">
        <Spin size="large" />
      </div>
    );
  }
  if (!sessao) return <Navigate to="/login" replace state={{ de: local.pathname }} />;
  if (tela && !pode(tela, 'visualizar')) return <SemPermissao />;
  return <>{children}</>;
}
