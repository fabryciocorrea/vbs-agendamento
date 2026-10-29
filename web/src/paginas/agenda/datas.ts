// Datas da agenda valem no horário de Brasília (as regras do servidor também).
const FUSO = 'America/Sao_Paulo';

/** Hora HH:mm de um instante ISO, no horário de Brasília. */
export function horaBrasilia(iso: string): string {
  return new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone: FUSO });
}

/** Data AAAA-MM-DD de um instante ISO, no horário de Brasília. */
export function diaBrasilia(iso: string): string {
  return new Date(iso).toLocaleDateString('en-CA', { timeZone: FUSO });
}

/** Instante ISO (UTC) de uma data AAAA-MM-DD e hora HH:mm de Brasília. */
export function instanteBrasilia(dia: string, hora: string): string {
  return new Date(`${dia}T${hora}:00-03:00`).toISOString();
}

export function adicionarDias(dia: string, dias: number): string {
  const d = new Date(`${dia}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + dias);
  return d.toISOString().slice(0, 10);
}

/** Dia da semana: 1 = segunda ... 7 = domingo. */
export function diaDaSemana(dia: string): number {
  const n = new Date(`${dia}T12:00:00Z`).getUTCDay();
  return n === 0 ? 7 : n;
}

export function segundaDaSemana(dia: string): string {
  return adicionarDias(dia, 1 - diaDaSemana(dia));
}

export function hojeBrasilia(): string {
  return new Date().toLocaleDateString('en-CA', { timeZone: FUSO });
}

const NOMES_DIA = ['', 'Segunda', 'Terça', 'Quarta', 'Quinta', 'Sexta', 'Sábado', 'Domingo'];
export function nomeDiaSemana(dia: string): string {
  return NOMES_DIA[diaDaSemana(dia)];
}

export function dataCurta(dia: string): string {
  const [, m, d] = dia.split('-');
  return `${d}/${m}`;
}

export function primeiroDoMes(dia: string): string {
  return `${dia.slice(0, 7)}-01`;
}

export function adicionarMeses(dia: string, n: number): string {
  const [a, m] = dia.split('-').map(Number);
  return new Date(Date.UTC(a, m - 1 + n, 1)).toISOString().slice(0, 10);
}

export function ultimoDoMes(dia: string): string {
  return adicionarDias(adicionarMeses(dia, 1), -1);
}

/** "Novembro 2026". */
export function nomeMesAno(dia: string): string {
  const nome = new Date(`${dia}T12:00:00Z`).toLocaleDateString('pt-BR', { month: 'long', timeZone: 'UTC' });
  return `${nome.charAt(0).toUpperCase()}${nome.slice(1)} ${dia.slice(0, 4)}`;
}
