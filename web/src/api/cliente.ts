import type { ErroCampo } from './tipos';

// Em desenvolvimento o Vite repassa /api para o servidor (veja vite.config.ts).
const BASE = '/api/v1';
const CHAVE_TOKEN = 'vbs.token';

/** Erro devolvido pela API no formato application/problem+json do contrato. */
export class ApiErro extends Error {
  status: number;
  titulo: string;
  codigo?: string;
  erros: ErroCampo[];
  extras: Record<string, unknown>;

  constructor(
    status: number,
    titulo: string,
    detalhe?: string,
    codigo?: string,
    erros: ErroCampo[] = [],
    extras: Record<string, unknown> = {}
  ) {
    super(detalhe ?? titulo);
    this.name = 'ApiErro';
    this.status = status;
    this.titulo = titulo;
    this.codigo = codigo;
    this.erros = erros;
    this.extras = extras;
  }
}

// O token fica só na aba (sessionStorage): fechar a aba encerra o acesso nesta máquina.
export function obterToken(): string | null {
  try {
    return sessionStorage.getItem(CHAVE_TOKEN);
  } catch {
    return null;
  }
}

export function guardarToken(token: string | null): void {
  try {
    if (token) sessionStorage.setItem(CHAVE_TOKEN, token);
    else sessionStorage.removeItem(CHAVE_TOKEN);
  } catch {
    // Sem armazenamento disponível: a sessão vale só até recarregar a página.
  }
}

let aoPerderSessao: (() => void) | null = null;

/** Chamado quando a API responde 401 numa rota autenticada (sessão expirada ou encerrada). */
export function definirAoPerderSessao(funcao: (() => void) | null): void {
  aoPerderSessao = funcao;
}

type Metodo = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
type Parametros = Record<string, string | number | boolean | null | undefined>;

function montarConsulta(parametros?: Parametros): string {
  if (!parametros) return '';
  const consulta = new URLSearchParams();
  for (const [chave, valor] of Object.entries(parametros)) {
    if (valor === undefined || valor === null || valor === '') continue;
    consulta.set(chave, String(valor));
  }
  const texto = consulta.toString();
  return texto ? `?${texto}` : '';
}

async function requisitar<T>(metodo: Metodo, caminho: string, corpo?: unknown, publico = false): Promise<T> {
  const cabecalhos: Record<string, string> = { Accept: 'application/json' };
  // FormData (envio de arquivo): o navegador define o Content-Type com o limite do multipart.
  const ehForm = typeof FormData !== 'undefined' && corpo instanceof FormData;
  if (corpo !== undefined && !ehForm) cabecalhos['Content-Type'] = 'application/json';
  const token = obterToken();
  if (token && !publico) cabecalhos['Authorization'] = `Bearer ${token}`;

  let resposta: Response;
  try {
    resposta = await fetch(BASE + caminho, {
      method: metodo,
      headers: cabecalhos,
      body: corpo === undefined ? undefined : ehForm ? (corpo as FormData) : JSON.stringify(corpo)
    });
  } catch {
    throw new ApiErro(0, 'Sem conexão', 'Não foi possível falar com o servidor. Confira se ele está rodando.');
  }

  if (resposta.status === 204) return undefined as T;

  const texto = await resposta.text();
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let json: any;
  try {
    json = texto ? JSON.parse(texto) : undefined;
  } catch {
    json = undefined;
  }

  if (!resposta.ok) {
    if (resposta.status === 401 && !publico && aoPerderSessao) aoPerderSessao();
    // eslint-disable-next-line @typescript-eslint/no-unused-vars
    const { title, detail, codigo, erros, status, ...resto } = json ?? {};
    throw new ApiErro(resposta.status, title ?? 'Erro', detail, codigo, erros ?? [], resto);
  }

  return json as T;
}

export const api = {
  get: <T>(caminho: string, parametros?: Parametros) => requisitar<T>('GET', caminho + montarConsulta(parametros)),
  post: <T>(caminho: string, corpo?: unknown) => requisitar<T>('POST', caminho, corpo ?? {}),
  put: <T>(caminho: string, corpo: unknown) => requisitar<T>('PUT', caminho, corpo),
  patch: <T>(caminho: string, corpo: unknown) => requisitar<T>('PATCH', caminho, corpo),
  delete: <T>(caminho: string) => requisitar<T>('DELETE', caminho),
  /** Envia um arquivo (multipart). O campo do formulário é "arquivo". */
  upload: <T>(caminho: string, arquivo: File) => {
    const form = new FormData();
    form.append('arquivo', arquivo);
    return requisitar<T>('POST', caminho, form);
  },
  /** Baixa um arquivo autenticado e o entrega ao navegador com o nome informado. */
  baixar: async (caminho: string, nomeArquivo: string): Promise<void> => {
    const token = obterToken();
    let resposta: Response;
    try {
      resposta = await fetch(BASE + caminho, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
    } catch {
      throw new ApiErro(0, 'Sem conexão', 'Não foi possível falar com o servidor. Confira se ele está rodando.');
    }
    if (!resposta.ok) {
      if (resposta.status === 401 && aoPerderSessao) aoPerderSessao();
      let detalhe: string | undefined;
      try {
        detalhe = (await resposta.json())?.detail;
      } catch {
        detalhe = undefined;
      }
      throw new ApiErro(resposta.status, 'Erro', detalhe ?? 'Não foi possível baixar o arquivo.');
    }
    const url = URL.createObjectURL(await resposta.blob());
    const a = document.createElement('a');
    a.href = url;
    a.download = nomeArquivo;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  },
  /** POST sem token e sem a reação de "sessão perdida" (login e rotas públicas). */
  postPublico: <T>(caminho: string, corpo: unknown) => requisitar<T>('POST', caminho, corpo, true)
};
