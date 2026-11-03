import type { ReactNode } from 'react';
import { ConfigProvider, theme } from 'antd';
import IlustracaoLogin from './IlustracaoLogin';
import './login.css';

/** Moldura das telas de acesso: formulário no painel verde, marca e ilustração no painel claro. */
export default function LoginMoldura({ titulo, subtitulo, children }: { titulo: string; subtitulo: string; children: ReactNode }) {
  return (
    <ConfigProvider theme={{ inherit: false, algorithm: theme.defaultAlgorithm, token: { colorPrimary: '#023926', borderRadius: 10 } }}>
    <div className="login-pagina">
      <section className="login-esquerda">
        <div className="login-caixa">
          <img className="login-icone" src="/logo-icone.png" alt="Viver Bem Saúde" />
          <h1 className="login-titulo">{titulo}</h1>
          <p className="login-subtitulo">{subtitulo}</p>
          {children}
          <div className="login-rodape">Viver Bem Saúde · Centro Integrado de Psicologia</div>
        </div>
      </section>
      <aside className="login-direita" aria-hidden="true">
        <img className="login-logo" src="/logo-completo.png" alt="" />
        <IlustracaoLogin />
        <p className="login-chamada">
          Agenda, pacientes e prontuários em um só lugar.
          <small>Gestão de Agendamentos e Consultas</small>
        </p>
      </aside>
    </div>
    </ConfigProvider>
  );
}
