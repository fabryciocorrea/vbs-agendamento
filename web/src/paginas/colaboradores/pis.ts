import { somenteDigitos } from '../../util/formatos';

/** Máscara do PIS/PASEP: 000.00000.00-0. */
export function formatarPis(valor: string | null | undefined): string {
  const d = somenteDigitos(valor).slice(0, 11);
  let r = d;
  if (d.length > 3) r = `${d.slice(0, 3)}.${d.slice(3)}`;
  if (d.length > 8) r = `${r.slice(0, 9)}.${d.slice(8)}`;
  if (d.length > 10) r = `${r.slice(0, 12)}-${d.slice(10)}`;
  return r;
}

/** PIS/PASEP válido: 11 dígitos com dígito verificador (pesos 3,2,9,8,7,6,5,4,3,2). */
export function pisValido(valor: string | null | undefined): boolean {
  const d = somenteDigitos(valor);
  if (d.length !== 11 || /^(\d)\1{10}$/.test(d)) return false;
  const pesos = [3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
  const soma = pesos.reduce((t, p, i) => t + Number(d[i]) * p, 0);
  let dv = 11 - (soma % 11);
  if (dv >= 10) dv = 0;
  return dv === Number(d[10]);
}
