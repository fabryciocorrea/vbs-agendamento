import { Result } from 'antd';

export default function SemPermissao() {
  return <Result status="403" title="Acesso negado" subTitle="Seu perfil não tem permissão para acessar esta tela." />;
}
