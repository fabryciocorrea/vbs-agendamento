export function somenteDigitos(valor: string | null | undefined): string {
  return (valor ?? '').replace(/\D/g, '');
}

export function formatarCnpj(valor: string | null | undefined): string {
  const d = somenteDigitos(valor).slice(0, 14);
  let r = d;
  if (d.length > 2) r = `${d.slice(0, 2)}.${d.slice(2)}`;
  if (d.length > 5) r = `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5)}`;
  if (d.length > 8) r = `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8)}`;
  if (d.length > 12) r = `${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}`;
  return r;
}

export function formatarCep(valor: string | null | undefined): string {
  const d = somenteDigitos(valor).slice(0, 8);
  return d.length > 5 ? `${d.slice(0, 5)}-${d.slice(5)}` : d;
}

export function cnpjValido(valor: string | null | undefined): boolean {
  const d = somenteDigitos(valor);
  if (d.length !== 14 || /^(\d)\1+$/.test(d)) return false;
  const digito = (base: string, pesos: number[]) => {
    const soma = base.split('').reduce((acc, n, i) => acc + Number(n) * pesos[i], 0);
    const resto = soma % 11;
    return resto < 2 ? 0 : 11 - resto;
  };
  const d1 = digito(d.slice(0, 12), [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
  const d2 = digito(d.slice(0, 13), [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
  return d1 === Number(d[12]) && d2 === Number(d[13]);
}

export const UFS = [
  'AC', 'AL', 'AP', 'AM', 'BA', 'CE', 'DF', 'ES', 'GO', 'MA', 'MT', 'MS', 'MG', 'PA',
  'PB', 'PR', 'PE', 'PI', 'RJ', 'RN', 'RS', 'RO', 'RR', 'SC', 'SP', 'SE', 'TO'
];

/** Converte "2025-01-31" em "31/01/2025" sem passar por fuso horário. */
export function formatarDataIso(iso: string | null | undefined): string {
  if (!iso) return '';
  const [a, m, d] = iso.slice(0, 10).split('-');
  return `${d}/${m}/${a}`;
}

/** Data de hoje no formato AAAA-MM-DD (hora local do navegador). */
export function hojeIso(): string {
  const h = new Date();
  const mm = String(h.getMonth() + 1).padStart(2, '0');
  const dd = String(h.getDate()).padStart(2, '0');
  return `${h.getFullYear()}-${mm}-${dd}`;
}

export function formatarCpf(valor: string | null | undefined): string {
  const d = somenteDigitos(valor).slice(0, 11);
  let r = d;
  if (d.length > 3) r = `${d.slice(0, 3)}.${d.slice(3)}`;
  if (d.length > 6) r = `${d.slice(0, 3)}.${d.slice(3, 6)}.${d.slice(6)}`;
  if (d.length > 9) r = `${d.slice(0, 3)}.${d.slice(3, 6)}.${d.slice(6, 9)}-${d.slice(9)}`;
  return r;
}

export function cpfValido(valor: string | null | undefined): boolean {
  const d = somenteDigitos(valor);
  if (d.length !== 11 || /^(\d)\1+$/.test(d)) return false;
  const digito = (base: string, pesoInicial: number) => {
    const soma = base.split('').reduce((acc, n, i) => acc + Number(n) * (pesoInicial - i), 0);
    const resto = (soma * 10) % 11;
    return resto === 10 ? 0 : resto;
  };
  return digito(d.slice(0, 9), 10) === Number(d[9]) && digito(d.slice(0, 10), 11) === Number(d[10]);
}

/** (11) 98765-4321 para exibir; o servidor recebe só os dígitos. */
export function formatarTelefone(valor: string | null | undefined): string {
  const d = somenteDigitos(valor).slice(0, 13);
  if (d.length <= 2) return d;
  if (d.length <= 6) return `(${d.slice(0, 2)}) ${d.slice(2)}`;
  if (d.length <= 10) return `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`;
  if (d.length === 11) return `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`;
  return d; // 12 ou 13 dígitos (com 55): mostra sem máscara
}

/** Verdadeiro se a pessoa tem menos de 18 anos na data de hoje (data no formato AAAA-MM-DD). */
export function ehMenorDeIdade(nascimentoIso: string | null | undefined): boolean {
  if (!nascimentoIso || nascimentoIso.length < 10) return false;
  const [a, m, d] = nascimentoIso.slice(0, 10).split('-').map(Number);
  const hoje = new Date();
  let idade = hoje.getFullYear() - a;
  if (hoje.getMonth() + 1 < m || (hoje.getMonth() + 1 === m && hoje.getDate() < d)) idade--;
  return idade < 18;
}

/** Data e hora (ISO em UTC, vinda do servidor) no horário do navegador, no formato DD/MM/AAAA HH:mm. */
export function formatarDataHora(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' });
}

/** Máscara de celular com DDD: (00) 00000-0000. Aceita no máximo 11 dígitos. */
export function formatarCelular(valor: string | null | undefined): string {
  const d = somenteDigitos(valor).slice(0, 11);
  if (d.length <= 2) return d;
  if (d.length <= 7) return `(${d.slice(0, 2)}) ${d.slice(2)}`;
  return `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`;
}

/** Celular válido: DDD mais 9 dígitos (11 dígitos no total). */
export function celularValido(valor: string | null | undefined): boolean {
  return somenteDigitos(valor).length === 11;
}
