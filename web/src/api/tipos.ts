// Tipos que espelham o contrato OpenAPI (nomes e formatos iguais aos da API).

export interface ErroCampo {
  campo: string;
  mensagem: string;
}

export interface PerfilReferencia {
  id: number;
  nome: string;
}

export interface UsuarioResumo {
  id: number;
  nomeUsuario: string;
  nomeCompleto: string;
  perfil: PerfilReferencia;
}

export interface SessaoSaida {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  usuario: UsuarioResumo;
}

export interface PermissaoTela {
  telaId: number;
  telaCodigo: string;
  podeVisualizar: boolean;
  podeIncluir: boolean;
  podeEditar: boolean;
  podeExcluir: boolean;
  podeExportar: boolean;
}

export interface PermissaoCampo {
  campoId: number;
  campoCodigo: string;
  podeVisualizar: boolean;
  podeEditar: boolean;
}

export interface UsuarioAtualSaida {
  usuario: UsuarioResumo;
  profissionalId: number | null;
  permissoes: {
    telas: PermissaoTela[];
    campos: PermissaoCampo[];
  };
}

export interface Pagina<T> {
  itens: T[];
  pagina: number;
  tamanho: number;
  total: number;
}

export interface UsuarioReferencia {
  id: number;
  nomeCompleto: string;
}

export interface Unidade {
  id: number;
  ativo: boolean;
  razaoSocial: string;
  nomeFantasia: string;
  cnpj: string;
  cep: string;
  endereco: string;
  numero: string;
  complemento?: string | null;
  cidade: string;
  uf: string;
  criadoEm?: string;
  criadoPor?: UsuarioReferencia | null;
  atualizadoEm?: string;
  atualizadoPor?: UsuarioReferencia | null;
}

export interface UnidadeEntrada {
  razaoSocial: string;
  nomeFantasia: string;
  cnpj: string;
  cep: string;
  endereco: string;
  numero: string;
  complemento?: string | null;
  cidade: string;
  uf: string;
}

export interface Sala {
  id: number;
  ativo: boolean;
  unidade: { id: number; nome: string };
  numero: string;
  andar: string;
  criadoEm?: string;
  criadoPor?: UsuarioReferencia | null;
  atualizadoEm?: string;
  atualizadoPor?: UsuarioReferencia | null;
}

export interface SalaEntrada {
  unidadeId: number;
  numero: string;
  andar: string;
}

export interface Especialidade {
  id: number;
  ativo: boolean;
  nome: string;
  criadoEm?: string;
  criadoPor?: UsuarioReferencia | null;
  atualizadoEm?: string;
  atualizadoPor?: UsuarioReferencia | null;
}

export interface Convenio {
  id: number;
  ativo: boolean;
  razaoSocial: string;
  nomeFantasia?: string | null;
  cnpj: string;
  codigoAns: string;
  dataInscricao: string; // AAAA-MM-DD
  inscricaoMunicipal?: string | null;
  inscricaoEstadual?: string | null;
  formaPagamentoId?: number | null;
}

export type TipoFormaPagamento = 'DINHEIRO' | 'PIX' | 'CARTAO_CREDITO' | 'CARTAO_DEBITO' | 'CONVENIO';
export type CondicaoFormaPagamento = 'A_VISTA' | 'A_PRAZO';

export interface FormaPagamento {
  id: number;
  ativo: boolean;
  descricao: string;
  tipo: TipoFormaPagamento;
  condicao: CondicaoFormaPagamento;
  convenio?: { id: number; nome: string } | null;
}

export interface Referencia {
  id: number;
  nome: string;
}

export interface ItemApoio {
  id: number;
  nome: string;
  ativo: boolean;
  complemento?: string | null;
}

export type TipoHorario = 'ATENDIMENTO' | 'INTERVALO';

export interface Horario {
  unidadeId?: number;
  diaSemana: number; // 1 = segunda ... 7 = domingo
  tipo: TipoHorario;
  horaInicio: string; // HH:mm
  horaFim: string; // HH:mm
}

export interface ProfissionalResumo {
  id: number;
  ativo: boolean;
  nome: string;
  registro: string;
  registroProfissional: 'CRM' | 'CRP';
  numeroConselho: string;
  ufConselho: string;
  especialidades: Referencia[];
  unidades: Referencia[];
}

export interface Profissional {
  id: number;
  ativo: boolean;
  nome: string;
  cpf: string;
  rg?: string | null;
  dataNascimento: string;
  telefone: string;
  email: string;
  endereco: string;
  numero: string;
  complemento?: string | null;
  cidade: string;
  uf: string;
  escolaridade?: string | null;
  registroProfissional: 'CRM' | 'CRP';
  numeroConselho: string;
  ufConselho: string;
  especialidades: Referencia[];
  unidades: Referencia[];
  quantidadePacientes: number;
}

export interface HorariosSaida {
  horarios: Horario[];
  consultasForaDoExpediente: {
    quantidade: number;
    itens: { consultaId: string; dataInicio: string }[];
  };
}

export interface CampoSistema {
  id: number;
  codigo: string;
  nome: string;
  obrigatorio: boolean;
}

export interface TelaSistema {
  id: number;
  codigo: string;
  nome: string;
  modulo: string;
  campos: CampoSistema[];
}

export interface PerfilResumo {
  id: number;
  nome: string;
  descricao?: string | null;
  protegido: boolean;
  quantidadeUsuarios: number;
}

export interface PermissoesPerfil {
  telas: {
    telaId: number;
    telaCodigo?: string;
    podeVisualizar: boolean;
    podeIncluir: boolean;
    podeEditar: boolean;
    podeExcluir: boolean;
    podeExportar: boolean;
  }[];
  campos: {
    campoId: number;
    campoCodigo?: string;
    podeVisualizar: boolean;
    podeEditar: boolean;
  }[];
}

export interface Perfil extends PerfilResumo {
  permissoes: PermissoesPerfil;
}

export interface AvisoPerfil {
  telaId: number;
  campoId: number;
  mensagem: string;
}

export interface PerfilComAvisos extends Perfil {
  avisos: AvisoPerfil[];
}

export interface Usuario {
  id: number;
  nomeUsuario: string;
  nomeCompleto: string;
  email: string;
  cargo: string;
  ativo: boolean;
  perfil: PerfilReferencia;
  profissionalId?: number | null;
  profissionalNome?: string | null;
  bloqueado: boolean;
  bloqueadoEm?: string | null;
  ultimoLoginEm?: string | null;
}

export interface PacienteResumo {
  id: string;
  ativo: boolean;
  nome: string;
  cpfMascarado?: string | null;
  dataNascimento: string; // AAAA-MM-DD
  menorDeIdade: boolean;
  telefone?: string | null;
  convenio?: { id: number; nomeFantasia: string } | null;
  profissionalResponsavel: Referencia;
}

export interface ConsentimentoPaciente {
  finalidade: 'TRATAMENTO_DADOS' | 'CONTATO_SMS_WHATSAPP';
  concedido: boolean;
  origem: string;
  dataRegistro: string;
}

export interface ResponsavelLegal {
  id: number;
  nome: string;
  cpf: string;
  rg?: string | null;
  dataNascimento: string;
  telefone: string;
  email: string;
  escolaridade?: string | null;
  endereco?: string | null;
  numero?: string | null;
  complemento?: string | null;
  cidade?: string | null;
  uf?: string | null;
  consentimentosAtuais?: ConsentimentoPaciente[];
  quantidadePacientes?: number;
}

export interface HistoricoProfissional {
  profissional: Referencia;
  dataInicio: string;
  dataFim?: string | null;
  alteradoPor?: UsuarioReferencia | null;
}

export interface ConsentimentoHistorico extends ConsentimentoPaciente {
  registradoPor?: UsuarioReferencia | null;
}

export interface Paciente {
  id: string;
  ativo: boolean;
  nome: string;
  estrangeiro: boolean;
  cpf?: string | null;
  tipoDocumentoEstrangeiro?: string | null;
  numeroDocumentoEstrangeiro?: string | null;
  rg?: string | null;
  dataNascimento: string;
  menorDeIdade: boolean;
  telefone?: string | null;
  email?: string | null;
  endereco: string;
  numero: string;
  complemento?: string | null;
  cidade: string;
  uf: string;
  escolaridade?: string | null;
  convenio?: {
    convenio: { id: number; nomeFantasia: string };
    numeroCarteirinha: string;
    dataValidadeCarteirinha: string;
    carteirinhaVencida: boolean;
  } | null;
  profissionalResponsavel: Referencia;
  responsavelLegal?: ResponsavelLegal | null;
  consentimentosAtuais: ConsentimentoPaciente[];
}
