import type { ReactNode } from 'react';

/** Título da página: ícone em quadrado arredondado, título grande e subtítulo. */
export default function CabecalhoPagina({ icone, titulo, subtitulo }: { icone: ReactNode; titulo: string; subtitulo?: string }) {
  return (
    <div className="cabecalho-pagina">
      <span className="cabecalho-icone" aria-hidden="true">
        {icone}
      </span>
      <div style={{ minWidth: 0 }}>
        <h1 className="cabecalho-titulo">{titulo}</h1>
        {subtitulo && <span className="cabecalho-subtitulo">{subtitulo}</span>}
      </div>
    </div>
  );
}
