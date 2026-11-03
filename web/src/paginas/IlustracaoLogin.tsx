/** Ilustração própria (calendário, consultas e confirmação), nas cores do logotipo. */
export default function IlustracaoLogin() {
  return (
    <svg className="login-ilustracao" viewBox="0 0 440 340" role="img" aria-label="Calendário de consultas com horário confirmado">
      <ellipse cx="220" cy="318" rx="150" ry="10" fill="#023926" opacity="0.08" />

      {/* calendário */}
      <rect x="70" y="40" width="230" height="250" rx="20" fill="#ffffff" stroke="#cfe3d8" strokeWidth="2" />
      <path d="M70 60a20 20 0 0 1 20-20h190a20 20 0 0 1 20 20v34H70z" fill="#0b5a3c" />
      <rect x="110" y="26" width="10" height="28" rx="5" fill="#FECE22" />
      <rect x="250" y="26" width="10" height="28" rx="5" fill="#FECE22" />
      <rect x="92" y="62" width="70" height="8" rx="4" fill="#ffffff" opacity="0.9" />
      <rect x="92" y="76" width="44" height="6" rx="3" fill="#ffffff" opacity="0.5" />

      {/* dias */}
      {[0, 1, 2, 3, 4].map((linha) =>
        [0, 1, 2, 3, 4, 5].map((coluna) => {
          const x = 94 + coluna * 32;
          const y = 118 + linha * 32;
          const marcado = linha === 2 && coluna === 3;
          const ocupado = (linha === 1 && coluna === 1) || (linha === 3 && coluna === 4) || (linha === 0 && coluna === 5);
          return (
            <rect
              key={`${linha}-${coluna}`}
              x={x}
              y={y}
              width="22"
              height="22"
              rx="7"
              fill={marcado ? '#FECE22' : ocupado ? '#cfe3d8' : '#eef4f0'}
            />
          );
        })
      )}

      {/* selo de confirmação */}
      <circle cx="262" cy="212" r="26" fill="#0b5a3c" stroke="#ffffff" strokeWidth="5" />
      <path d="M250 212l9 9 16-18" fill="none" stroke="#FECE22" strokeWidth="6" strokeLinecap="round" strokeLinejoin="round" />

      {/* cartão de consulta */}
      <g>
        <rect x="262" y="96" width="150" height="74" rx="16" fill="#ffffff" stroke="#cfe3d8" strokeWidth="2" />
        <circle cx="290" cy="124" r="14" fill="#fff0b3" />
        <path d="M284 124h12M290 118v12" stroke="#0b5a3c" strokeWidth="3" strokeLinecap="round" />
        <rect x="312" y="114" width="76" height="8" rx="4" fill="#0b5a3c" />
        <rect x="312" y="130" width="54" height="7" rx="3.5" fill="#b9d3c5" />
        <rect x="282" y="150" width="104" height="8" rx="4" fill="#e8f3ed" />
      </g>

      {/* relógio */}
      <g>
        <circle cx="346" cy="250" r="34" fill="#ffffff" stroke="#cfe3d8" strokeWidth="2" />
        <circle cx="346" cy="250" r="26" fill="#e8f3ed" />
        <path d="M346 234v17l12 8" fill="none" stroke="#023926" strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" />
      </g>

      {/* planta */}
      <g>
        <path d="M70 290h52l-6 32a8 8 0 0 1-8 7H84a8 8 0 0 1-8-7z" fill="#FECE22" />
        <path d="M96 290c-20-20-26-44-14-66 12 14 18 38 14 66z" fill="#0b5a3c" />
        <path d="M98 290c4-24 16-42 36-52-2 24-14 42-36 52z" fill="#2b8a62" />
        <path d="M96 290c-2-30 4-52 18-70 6 26 0 50-18 70z" fill="#1a7550" />
      </g>
    </svg>
  );
}
