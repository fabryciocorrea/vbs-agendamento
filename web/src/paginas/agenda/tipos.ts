import type { Referencia } from '../../api/tipos';

export interface ParametrosAgenda {
  duracaoPadraoConsultaMin: number;
  antecedenciaCancelamentoHoras: number;
  limiteEncaixesDia?: number | null;
  recessoFimAnoInicio?: string | null; // AAAA-MM-DD
  recessoFimAnoFim?: string | null;
}

export interface Feriado {
  data: string; // AAAA-MM-DD
  nome: string;
  automatico?: boolean;
}

export interface Slot {
  inicio: string;
  fim: string;
}

export interface Bloqueio {
  tipo: 'INTERVALO' | 'FERIADO' | 'RECESSO';
  inicio: string;
  fim: string;
  descricao?: string | null;
}

export interface PeriodoExpediente {
  unidade: Referencia;
  inicio: string;
  fim: string;
}

export type StatusConsulta =
  | 'AGENDADA'
  | 'CONFIRMADA'
  | 'CHECK_IN'
  | 'EM_ATENDIMENTO'
  | 'CONCLUIDA'
  | 'CANCELADA'
  | 'FALTA'
  | 'REMARCADA';

export interface ConsultaResumo {
  id: string;
  versao: string;
  status: StatusConsulta;
  paciente: { id: string; nome: string };
  profissional: Referencia;
  especialidade: Referencia;
  unidade: Referencia;
  sala?: { id: number; numero: string; andar: string } | null;
  inicio: string;
  fim: string;
  modalidade: 'PRESENCIAL' | 'ONLINE';
  tipoAtendimento: 'PARTICULAR' | 'CONVENIO';
  convenio?: Referencia | null;
  valor?: number | null;
  encaixe: boolean;
}

export interface Aviso {
  codigo: string;
  mensagem: string;
}

export interface Consulta extends ConsultaResumo {
  linkOnline?: string | null;
  numeroCarteirinha?: string | null;
  carteirinhaVencida: boolean;
  formaPagamento: Referencia;
  pagamentoConfirmado: boolean;
  pagamentoConfirmadoEm?: string | null;
  pagamentoConfirmadoPor?: { id: number; nomeCompleto: string } | null;
  guia?: {
    status: 'PENDENTE' | 'AUTORIZADA' | 'NEGADA';
    numero?: string | null;
    observacao?: string | null;
    atualizadoEm?: string | null;
    atualizadoPor?: { id: number; nomeCompleto: string } | null;
  } | null;
  liberacaoAdministrador?: { liberadoPor: { id: number; nomeCompleto: string }; justificativa: string; em: string } | null;
  credenciamentoLiberado?: { liberadoPor: { id: number; nomeCompleto: string }; justificativa: string } | null;
  criadoEm: string;
  criadoPor?: { id: number; nomeCompleto: string } | null;
  avisos?: Aviso[];
}

export interface AgendaSaida {
  profissional: Referencia;
  expediente: PeriodoExpediente[];
  bloqueios: Bloqueio[];
  consultas: ConsultaResumo[];
}

export interface ConsultaEntrada {
  pacienteId: string;
  profissionalId: number;
  especialidadeId: number;
  unidadeId: number;
  salaId?: number | null;
  modalidade: 'PRESENCIAL' | 'ONLINE';
  tipoAtendimento: 'PARTICULAR' | 'CONVENIO';
  convenioId?: number | null;
  valor: number;
  formaPagamentoId?: number | null;
  inicio: string; // UTC, ISO
  fim?: string | null;
  encaixe: boolean;
  pagamentoConfirmado: boolean;
  liberacaoCredenciamento?: { justificativa: string } | null;
  liberacaoAdministrador?: { justificativa: string } | null;
}

export const NOMES_STATUS: Record<StatusConsulta, string> = {
  AGENDADA: 'Agendada',
  CONFIRMADA: 'Confirmada',
  CHECK_IN: 'Check-in',
  EM_ATENDIMENTO: 'Em atendimento',
  CONCLUIDA: 'Concluída',
  CANCELADA: 'Cancelada',
  FALTA: 'Falta',
  REMARCADA: 'Remarcada'
};

export const CORES_STATUS: Record<StatusConsulta, string> = {
  AGENDADA: 'blue',
  CONFIRMADA: 'geekblue',
  CHECK_IN: 'cyan',
  EM_ATENDIMENTO: 'gold',
  CONCLUIDA: 'green',
  CANCELADA: 'red',
  FALTA: 'volcano',
  REMARCADA: 'purple'
};
