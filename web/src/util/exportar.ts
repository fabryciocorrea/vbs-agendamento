/** Tabela entregue pelo servidor (exportação): o navegador a transforma em CSV ou PDF. */
export interface TabelaExportacao {
  titulo: string;
  filtros: string[];
  colunas: { titulo: string; alinhamento: 'esquerda' | 'direita' }[];
  linhas: string[][];
  totais?: string[] | null;
  geradoEm: string;
}

const NOME_CLINICA = 'Viver Bem Saúde';

function nomeSeguro(texto: string): string {
  return texto
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^a-zA-Z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '')
    .toLowerCase();
}

export function nomeDoArquivo(tabela: TabelaExportacao, extensao: 'pdf' | 'csv'): string {
  const dia = new Date().toLocaleDateString('en-CA', { timeZone: 'America/Sao_Paulo' }).replace(/-/g, '');
  return `${nomeSeguro(tabela.titulo)}_${dia}.${extensao}`;
}

function salvar(blob: Blob, nome: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = nome;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

function campoCsv(valor: string): string {
  let t = valor ?? '';
  // Evita que o Excel trate o texto como fórmula.
  if (t.length > 0 && ['=', '+', '-', '@'].includes(t[0])) t = `'${t}`;
  return `"${t.replace(/"/g, '""')}"`;
}

/** CSV em UTF-8 com marca de ordem e ponto e vírgula, para abrir direto no Excel brasileiro. */
export function baixarCsv(tabela: TabelaExportacao) {
  const linhas = [tabela.colunas.map((c) => campoCsv(c.titulo)).join(';')];
  for (const l of tabela.linhas) linhas.push(l.map(campoCsv).join(';'));
  if (tabela.totais) linhas.push(tabela.totais.map(campoCsv).join(';'));
  const blob = new Blob(['﻿' + linhas.join('\r\n') + '\r\n'], { type: 'text/csv;charset=utf-8' });
  salvar(blob, nomeDoArquivo(tabela, 'csv'));
}

function quando(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleString('pt-BR', { timeZone: 'America/Sao_Paulo', dateStyle: 'short', timeStyle: 'short' });
}

/**
 * PDF com cabeçalho (clínica, relatório, filtros, usuário e data e hora) e número de página.
 * As bibliotecas só são carregadas quando o usuário pede o PDF.
 */
export async function baixarPdf(tabela: TabelaExportacao, usuario: string) {
  const { jsPDF } = await import('jspdf');
  const autoTable = (await import('jspdf-autotable')).default;

  const paisagem = tabela.colunas.length > 5;
  const doc = new jsPDF({ orientation: paisagem ? 'landscape' : 'portrait', unit: 'pt', format: 'a4' });
  const largura = doc.internal.pageSize.getWidth();
  const margem = 36;

  doc.setFont('helvetica', 'bold');
  doc.setFontSize(10);
  doc.setTextColor(31, 111, 74);
  doc.text(NOME_CLINICA, margem, 34);

  doc.setFontSize(15);
  doc.setTextColor(30, 30, 30);
  doc.text(tabela.titulo, margem, 54);

  doc.setFont('helvetica', 'normal');
  doc.setFontSize(9);
  doc.setTextColor(90, 90, 90);
  const filtros = doc.splitTextToSize(tabela.filtros.join('   |   '), largura - margem * 2) as string[];
  doc.text(filtros, margem, 70);
  const yUsuario = 70 + filtros.length * 11 + 2;
  doc.text(`Gerado por ${usuario} em ${quando(tabela.geradoEm)}`, margem, yUsuario);

  const alinhar = (i: number) => (tabela.colunas[i].alinhamento === 'direita' ? 'right' : 'left');
  const colunasEstilo: Record<number, { halign: 'left' | 'right' }> = {};
  tabela.colunas.forEach((_, i) => {
    colunasEstilo[i] = { halign: alinhar(i) };
  });

  autoTable(doc, {
    startY: yUsuario + 12,
    head: [tabela.colunas.map((c) => c.titulo)],
    body: tabela.linhas,
    foot: tabela.totais ? [tabela.totais] : undefined,
    showFoot: 'lastPage',
    margin: { left: margem, right: margem, bottom: 40 },
    styles: { font: 'helvetica', fontSize: paisagem && tabela.colunas.length > 9 ? 7 : 9, cellPadding: 3, overflow: 'linebreak' },
    headStyles: { fillColor: [31, 111, 74], textColor: 255, fontStyle: 'bold' },
    footStyles: { fillColor: [240, 240, 240], textColor: 30, fontStyle: 'bold' },
    alternateRowStyles: { fillColor: [248, 248, 248] },
    columnStyles: colunasEstilo
  });

  const total = doc.getNumberOfPages();
  const altura = doc.internal.pageSize.getHeight();
  doc.setFontSize(8);
  doc.setTextColor(120, 120, 120);
  for (let i = 1; i <= total; i++) {
    doc.setPage(i);
    doc.text(`Página ${i} de ${total}`, largura - margem, altura - 20, { align: 'right' });
    doc.text(NOME_CLINICA, margem, altura - 20);
  }

  doc.save(nomeDoArquivo(tabela, 'pdf'));
}
