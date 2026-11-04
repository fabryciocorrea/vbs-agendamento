import { useState } from 'react';
import { Avatar, Button, Descriptions, Divider, Modal, Popover, Typography } from 'antd';
import { KeyOutlined, LogoutOutlined, UserOutlined } from '@ant-design/icons';
import { useAuth } from '../auth/AuthContext';
import { cores } from '../tema';
import AlterarSenhaModal from './AlterarSenhaModal';

/** Iniciais do primeiro e do último nome (ex.: "Fabrycio Correa" vira "FC"); ignora traços e símbolos. */
function iniciais(nome: string): string {
  const palavras = nome.split(/\s+/).filter((p) => /\p{L}/u.test(p));
  if (palavras.length === 0) return '?';
  const primeira = palavras[0][0];
  const ultima = palavras.length > 1 ? palavras[palavras.length - 1][0] : '';
  return (primeira + ultima).toLocaleUpperCase('pt-BR');
}

/** Ícone com as iniciais do usuário; ao clicar mostra o perfil de acesso, "Meu perfil" e "Sair". */
export default function MenuUsuario() {
  const { sessao, sair } = useAuth();
  const [aberto, setAberto] = useState(false);
  const [perfilAberto, setPerfilAberto] = useState(false);
  const [senhaAberta, setSenhaAberta] = useState(false);
  if (!sessao) return null;
  const u = sessao.usuario;

  const conteudo = (
    <div style={{ minWidth: 220 }}>
      <div style={{ fontWeight: 600 }}>{u.nomeCompleto}</div>
      <Typography.Text type="secondary">{u.perfil.nome}</Typography.Text>
      <Divider style={{ margin: '10px 0' }} />
      <Button
        type="text"
        block
        icon={<UserOutlined />}
        style={{ textAlign: 'left' }}
        onClick={() => {
          setAberto(false);
          setPerfilAberto(true);
        }}
      >
        Meu perfil
      </Button>
      <Button
        type="text"
        block
        icon={<KeyOutlined />}
        style={{ textAlign: 'left' }}
        onClick={() => {
          setAberto(false);
          setSenhaAberta(true);
        }}
      >
        Alterar senha
      </Button>
      <Button type="text" block danger icon={<LogoutOutlined />} style={{ textAlign: 'left' }} onClick={() => void sair()}>
        Sair
      </Button>
    </div>
  );

  return (
    <>
      <Popover content={conteudo} trigger="click" placement="bottomRight" open={aberto} onOpenChange={setAberto} arrow={false}>
        <Avatar
          role="button"
          tabIndex={0}
          aria-label="Menu do usuário"
          style={{ background: cores.verde, color: cores.amarelo, cursor: 'pointer', fontWeight: 600 }}
          onKeyDown={(e) => {
            if (e.key === 'Enter' || e.key === ' ') setAberto((v) => !v);
          }}
        >
          {iniciais(u.nomeCompleto)}
        </Avatar>
      </Popover>
      <Modal title="Meu perfil" open={perfilAberto} footer={null} onCancel={() => setPerfilAberto(false)} destroyOnClose>
        <Descriptions column={1} size="small" bordered>
          <Descriptions.Item label="Nome">{u.nomeCompleto}</Descriptions.Item>
          <Descriptions.Item label="Usuário (login)">{u.nomeUsuario}</Descriptions.Item>
          <Descriptions.Item label="Perfil de acesso">{u.perfil.nome}</Descriptions.Item>
        </Descriptions>
        <Button
          icon={<KeyOutlined />}
          style={{ marginTop: 16 }}
          onClick={() => {
            setPerfilAberto(false);
            setSenhaAberta(true);
          }}
        >
          Alterar minha senha
        </Button>
      </Modal>
      <AlterarSenhaModal aberto={senhaAberta} aoFechar={() => setSenhaAberta(false)} />
    </>
  );
}
