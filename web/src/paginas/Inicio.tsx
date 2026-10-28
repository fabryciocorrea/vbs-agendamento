import { Typography } from 'antd';
import { useAuth } from '../auth/AuthContext';

export default function Inicio() {
  const { sessao } = useAuth();
  return (
    <>
      <Typography.Title level={3}>Bem-vindo, {sessao?.usuario.nomeCompleto}</Typography.Title>
      <Typography.Paragraph type="secondary">
        Use o menu à esquerda para acessar as telas liberadas para o seu perfil.
      </Typography.Paragraph>
    </>
  );
}
