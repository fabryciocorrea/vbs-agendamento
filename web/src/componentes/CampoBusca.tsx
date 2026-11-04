import { useState } from 'react';
import type { ComponentProps } from 'react';
import { Input } from 'antd';

/**
 * Campo de busca das listas (Input.Search). Com o campo em foco o texto de ajuda (placeholder) some e volta
 * ao sair: o navegador desenha o cursor do campo vazio pelo texto de ajuda, que ficava pequeno.
 */
export default function CampoBusca({ placeholder, onFocus, onBlur, ...resto }: ComponentProps<typeof Input.Search>) {
  const [focado, setFocado] = useState(false);
  return (
    <Input.Search
      {...resto}
      placeholder={focado ? undefined : placeholder}
      onFocus={(e) => {
        setFocado(true);
        onFocus?.(e);
      }}
      onBlur={(e) => {
        setFocado(false);
        onBlur?.(e);
      }}
    />
  );
}
