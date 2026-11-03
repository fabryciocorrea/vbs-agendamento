import { Button } from 'antd';
import { MoonOutlined, SunOutlined } from '@ant-design/icons';
import { useModoTema } from '../ModoTema';

/** Botão redondo (ao lado do sino) que alterna entre o modo claro e o escuro. */
export default function BotaoTema() {
  const { escuro, alternar } = useModoTema();
  const rotulo = escuro ? 'Mudar para o modo claro' : 'Mudar para o modo escuro';
  return <Button shape="circle" icon={escuro ? <SunOutlined /> : <MoonOutlined />} aria-label={rotulo} title={rotulo} onClick={alternar} />;
}
