import { adicionarDias } from './datas';

export type Repeticao = 'NENHUMA' | 'SEMANAL' | 'QUINZENAL' | 'MENSAL';

export const OPCOES_REPETICAO: { value: Repeticao; label: string }[] = [
  { value: 'NENHUMA', label: 'Não repete' },
  { value: 'SEMANAL', label: 'Toda semana' },
  { value: 'QUINZENAL', label: 'A cada 15 dias' },
  { value: 'MENSAL', label: 'Todo mês (mesmo dia da semana)' }
];

/**
 * Datas (AAAA-MM-DD) da série, na mesma regra do servidor: semanal soma 7 dias, quinzenal 14 e mensal mantém o dia da semana e a
 * posição no mês (a 5ª ocorrência vira a última do mês). Serve só para mostrar a prévia; quem decide é o servidor.
 */
export function datasDaSerie(dia: string, repeticao: Repeticao, quantidade: number): string[] {
  const datas: string[] = [];
  if (repeticao === 'NENHUMA') return [dia];
  const base = new Date(`${dia}T12:00:00Z`);
  const posicao = Math.floor((base.getUTCDate() - 1) / 7) + 1;
  for (let k = 0; k < quantidade; k++) {
    if (repeticao === 'SEMANAL') datas.push(adicionarDias(dia, 7 * k));
    else if (repeticao === 'QUINZENAL') datas.push(adicionarDias(dia, 14 * k));
    else {
      const ano = base.getUTCFullYear();
      const mes = base.getUTCMonth() + k;
      const primeiro = new Date(Date.UTC(ano, mes, 1, 12));
      const deslocamento = (base.getUTCDay() - primeiro.getUTCDay() + 7) % 7;
      let d = 1 + deslocamento + 7 * (posicao - 1);
      const diasNoMes = new Date(Date.UTC(primeiro.getUTCFullYear(), primeiro.getUTCMonth() + 1, 0, 12)).getUTCDate();
      if (d > diasNoMes) d -= 7;
      datas.push(new Date(Date.UTC(primeiro.getUTCFullYear(), primeiro.getUTCMonth(), d, 12)).toISOString().slice(0, 10));
    }
  }
  return datas;
}

export function formatarDiaCurto(dia: string): string {
  const [a, m, d] = dia.split('-');
  return `${d}/${m}/${a}`;
}
