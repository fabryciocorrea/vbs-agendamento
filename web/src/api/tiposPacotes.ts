/** Pacotes de consultas particulares (modelos e pacotes vendidos a pacientes). */

export interface PacoteReferenciaPaciente {
  id: string;
  nome: string;
}

export interface Pacote {
  id: number;
  ativo: boolean;
  nome: string;
  especialidade: { id: number; nome: string };
  quantidadeSessoes: number;
  valor: number;
  valorSessao: number;
  validadeDias: number;
  criadoEm: string;
  atualizadoEm: string;
}

export type SituacaoPacote = 'DISPONIVEL' | 'ENCERRADO' | 'CANCELADO';

export interface PacotePaciente {
  id: string;
  nome: string;
  paciente: PacoteReferenciaPaciente;
  especialidade: { id: number; nome: string };
  quantidadeSessoes: number;
  sessoesUsadas: number;
  sessoesRestantes: number;
  valor: number;
  valorSessao: number;
  dataInicio: string; // yyyy-MM-dd
  dataValidade: string; // yyyy-MM-dd
  formaPagamento: { id: number; nome: string };
  pagamentoConfirmado: boolean;
  pagamentoConfirmadoEm?: string | null;
  status: 'ATIVO' | 'CANCELADO';
  situacao: SituacaoPacote;
  vendidoEm: string;
  vendidoPor?: { id: number; nomeCompleto: string } | null;
  canceladoEm?: string | null;
  motivoCancelamento?: string | null;
}

export interface SessaoPacote {
  consultaId: string;
  inicio: string;
  status: string;
  profissional: string;
  consome: boolean;
}

export interface PacotePacienteDetalhe extends PacotePaciente {
  sessoes: SessaoPacote[];
}

export interface VendaPacoteEntrada {
  pacoteId: number;
  formaPagamentoId: number;
  pagamentoConfirmado: boolean;
}

export const NOMES_SITUACAO_PACOTE: Record<SituacaoPacote, string> = {
  DISPONIVEL: 'Disponível',
  ENCERRADO: 'Encerrado',
  CANCELADO: 'Cancelado'
};

export function formatarMoeda(valor: number | null | undefined): string {
  return (valor ?? 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

/** yyyy-MM-dd para dd/MM/yyyy (sem passar por Date, para não mudar o dia por causa do fuso). */
export function formatarDiaPacote(dia: string | null | undefined): string {
  if (!dia) return '—';
  const [a, m, d] = dia.slice(0, 10).split('-');
  return `${d}/${m}/${a}`;
}
