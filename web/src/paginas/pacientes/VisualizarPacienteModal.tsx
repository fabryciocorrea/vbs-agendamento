import { Descriptions, Modal, Tag } from 'antd';
import type { Paciente } from '../../api/tipos';
import { formatarCep, formatarCpf, formatarDataIso, formatarTelefone } from '../../util/formatos';

/** O RG também aparece mascarado: só os 3 últimos caracteres ficam visíveis (ex.: ******789). */
function mascararRg(rg?: string | null): string {
  const t = (rg ?? '').trim();
  if (t === '') return '—';
  return t.length <= 3 ? '***' : `${'*'.repeat(t.length - 3)}${t.slice(-3)}`;
}

/** P71: na visualização o CPF aparece mascarado, no mesmo formato da lista (***.123.456-**). */
function mascararCpf(cpf: string): string {
  const d = cpf.replace(/\D/g, '');
  return d.length === 11 ? `***.${d.slice(3, 6)}.${d.slice(6, 9)}-**` : '—';
}

const SEXOS: Record<string, string> = { M: 'Masculino', F: 'Feminino', O: 'Outro' };

function texto(v?: string | null): string {
  return v && v.trim() !== '' ? v : '—';
}

/** Cadastro completo do paciente, só para leitura (abre pela ação "Visualizar" da lista). */
/**
 * P73: o servidor já devolve CPF e RG mascarados a quem só visualiza (`dadosMascarados`), então a tela mostra o texto como veio.
 * `mascarar` só vale como reserva, se a resposta não trouxer esse aviso.
 */
export default function VisualizarPacienteModal({ paciente, aoFechar, mascarar = true }: { paciente: Paciente | null; aoFechar: () => void; mascarar?: boolean }) {
  const p = paciente;
  const resp = p?.responsavelLegal ?? null;
  const jaMascarado = p?.dadosMascarados === true;
  const mascararAqui = p?.dadosMascarados === undefined ? mascarar : false;
  return (
    <Modal open={!!p} title={p ? `Cadastro — ${p.nome}` : ''} onCancel={aoFechar} footer={null} width={900} destroyOnClose>
      {p && (
        <>
          <Descriptions title="Dados pessoais" size="small" bordered column={2} labelStyle={{ width: 170 }} contentStyle={{ width: 230 }} style={{ marginBottom: 16 }}>
            <Descriptions.Item label="Nome">
              {p.nome} {p.menorDeIdade && <Tag color="gold">Menor</Tag>}
            </Descriptions.Item>
            <Descriptions.Item label="Situação">{p.ativo ? <Tag color="green">Ativo</Tag> : <Tag>Inativo</Tag>}</Descriptions.Item>
            {p.estrangeiro ? (
              <Descriptions.Item label={`Documento (${texto(p.tipoDocumentoEstrangeiro)})`}>{!jaMascarado && mascararAqui ? mascararRg(p.numeroDocumentoEstrangeiro) : texto(p.numeroDocumentoEstrangeiro)}</Descriptions.Item>
            ) : (
              <Descriptions.Item label="CPF">{p.cpf ? (jaMascarado ? p.cpf : mascararAqui ? mascararCpf(p.cpf) : formatarCpf(p.cpf)) : '—'}</Descriptions.Item>
            )}
            <Descriptions.Item label="RG">{jaMascarado ? texto(p.rg) : mascararAqui ? mascararRg(p.rg) : texto(p.rg)}</Descriptions.Item>
            <Descriptions.Item label="Nascimento">{formatarDataIso(p.dataNascimento)}</Descriptions.Item>
            <Descriptions.Item label="Sexo">{p.sexo ? SEXOS[p.sexo] ?? p.sexo : '—'}</Descriptions.Item>
            <Descriptions.Item label="Escolaridade">{texto(p.escolaridade)}</Descriptions.Item>
            <Descriptions.Item label="Profissional responsável">{p.profissionalResponsavel.nome}</Descriptions.Item>
            <Descriptions.Item label="Mãe">{p.maeNaoPossui ? 'Não possui' : texto(p.nomeMae)}</Descriptions.Item>
            <Descriptions.Item label="Pai">{texto(p.nomePai)}</Descriptions.Item>
          </Descriptions>

          <Descriptions title="Contato" size="small" bordered column={2} labelStyle={{ width: 170 }} contentStyle={{ width: 230 }} style={{ marginBottom: 16 }}>
            <Descriptions.Item label="Celular">{p.telefone ? formatarTelefone(p.telefone) : '—'}</Descriptions.Item>
            <Descriptions.Item label="E-mail">{texto(p.email)}</Descriptions.Item>
          </Descriptions>

          <Descriptions title="Endereço" size="small" bordered column={2} labelStyle={{ width: 170 }} contentStyle={{ width: 230 }} style={{ marginBottom: 16 }}>
            <Descriptions.Item label="CEP">{p.cep ? formatarCep(p.cep) : '—'}</Descriptions.Item>
            <Descriptions.Item label="País">{texto(p.pais)}</Descriptions.Item>
            <Descriptions.Item label="Endereço">
              {texto(p.endereco)}, {texto(p.numero)}
            </Descriptions.Item>
            <Descriptions.Item label="Complemento">{texto(p.complemento)}</Descriptions.Item>
            <Descriptions.Item label="Bairro">{texto(p.bairro)}</Descriptions.Item>
            <Descriptions.Item label="Cidade/UF">
              {texto(p.cidade)}/{texto(p.uf)}
            </Descriptions.Item>
          </Descriptions>

          <Descriptions title="Atendimento" size="small" bordered column={1} labelStyle={{ width: 180 }} style={{ marginBottom: resp ? 16 : 0 }}>
            <Descriptions.Item label="Tipo de atendimento">{p.convenio ? p.convenio.convenio.nomeFantasia : 'Particular'}</Descriptions.Item>
          </Descriptions>

          {resp && (
            <Descriptions title="Responsável legal" size="small" bordered column={2} labelStyle={{ width: 170 }} contentStyle={{ width: 230 }}>
              <Descriptions.Item label="Nome">{resp.nome}</Descriptions.Item>
              <Descriptions.Item label="CPF">{jaMascarado || resp.dadosMascarados ? resp.cpf : mascararAqui ? mascararCpf(resp.cpf) : formatarCpf(resp.cpf)}</Descriptions.Item>
              <Descriptions.Item label="Nascimento">{formatarDataIso(resp.dataNascimento)}</Descriptions.Item>
              <Descriptions.Item label="Celular">{formatarTelefone(resp.telefone)}</Descriptions.Item>
              <Descriptions.Item label="E-mail">{resp.email}</Descriptions.Item>
            </Descriptions>
          )}
        </>
      )}
    </Modal>
  );
}
