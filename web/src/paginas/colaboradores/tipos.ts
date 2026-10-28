import type { UsuarioReferencia } from '../../api/tipos';

export type TipoVinculo = 'CLT' | 'MEI';

export interface Colaborador {
  /** Matrícula: é o CODIGO_CLB_PRT do banco. */
  id: number;
  ativo: boolean;
  tipoVinculo: TipoVinculo;
  nome: string;
  cpf?: string | null;
  razaoSocial?: string | null;
  nomeFantasia?: string | null;
  cnpj?: string | null;
  telefone: string;
  email: string;
  endereco: string;
  numero: string;
  complemento?: string | null;
  cidade: string;
  uf: string;
  cargo: string;
  ctpsNumero?: string | null;
  ctpsSerie?: string | null;
  ctpsUf?: string | null;
  ctpsDataEmissao?: string | null;
  pisPasep?: string | null;
  dataAdmissao?: string | null;
  criadoEm?: string;
  criadoPor?: UsuarioReferencia | null;
  atualizadoEm?: string;
  atualizadoPor?: UsuarioReferencia | null;
}

export interface ColaboradorEntrada {
  tipoVinculo: TipoVinculo;
  nome: string;
  cpf?: string | null;
  razaoSocial?: string | null;
  nomeFantasia?: string | null;
  cnpj?: string | null;
  telefone: string;
  email: string;
  endereco: string;
  numero: string;
  complemento?: string | null;
  cidade: string;
  uf: string;
  cargo: string;
  ctpsNumero?: string | null;
  ctpsSerie?: string | null;
  ctpsUf?: string | null;
  ctpsDataEmissao?: string | null;
  pisPasep?: string | null;
  dataAdmissao?: string | null;
}
