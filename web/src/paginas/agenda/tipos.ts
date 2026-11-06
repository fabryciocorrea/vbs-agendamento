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
  /** Profissional responsável atual pelo paciente. */
  pacienteResponsavelId?: number;
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
  /** Consulta criada por remarcação (a original ficou REMARCADA no horário antigo). */
  remarcada?: boolean;
  /** Convênio concluído com a guia ainda aguardando o faturamento. */
  pendenteFaturamento?: boolean;
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
  /** Série do agendamento repetido (nulo quando a consulta foi agendada sozinha) e quantas consultas futuras dela ainda estão agendadas ou confirmadas. */
  observacao?: string | null;
  justificativaProfissional?: string | null;
  serieId?: string | null;
  serieFuturas?: number;
  /** Pacote que paga a consulta (quando houver). */
  pacotePacienteId?: string | null;
  pacoteNome?: string | null;
  pacotePagamentoConfirmado?: boolean | null;
  pacoteValor?: number | null;
  pacoteFormaPagamentoNome?: string | null;
  pagamentoConfirmado: boolean;
  pagamentoConfirmadoEm?: string | null;
  pagamentoConfirmadoPor?: { id: number; nomeCompleto: string } | null;
  guia?: {
    status: 'PENDENTE' | 'AUTORIZADA' | 'NEGADA' | 'CANCELADA';
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
  /** Pacote já vendido ao paciente que a consulta consome (só particular). */
  pacotePacienteId?: string | null;
  /** Venda de um pacote novo no ato do agendamento (a forma de pagamento e o pagamento recebido valem para o pacote). */
  vendaPacote?: { pacoteId: number; formaPagamentoId: number; pagamentoConfirmado: boolean } | null;
  liberacaoCredenciamento?: { justificativa: string } | null;
  /** Obrigatória quando o profissional não é o responsável atual do paciente. */
  justificativaProfissional?: string | null;
  liberacaoAdministrador?: { justificativa: string } | null;
}

export interface ConsultaSerieEntrada extends ConsultaEntrada {
  repeticao: 'SEMANAL' | 'QUINZENAL' | 'MENSAL';
  /** Total de consultas da série, contando a primeira (2 a 12). */
  quantidade: number;
}

export interface ConsultaSerie {
  criadas: Consulta[];
  ignoradas: { inicio: string; codigo?: string | null; mensagem: string }[];
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
