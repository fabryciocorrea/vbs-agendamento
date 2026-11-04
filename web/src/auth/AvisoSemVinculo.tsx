import { Card, Result } from 'antd';

/** RN49: perfil de profissional sem "Profissional vinculado". Aparece dentro do layout (menu, cabeçalho e tema). */
export default function AvisoSemVinculo() {
  return (
    <Card>
      <Result
        status="warning"
        title="Seu usuário não está vinculado a um profissional"
        subTitle="Procure o administrador do sistema para vincular o seu usuário ao seu cadastro de profissional."
      />
    </Card>
  );
}
