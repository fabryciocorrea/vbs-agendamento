import { Descriptions, Modal, Tag } from 'antd';
import type { Paciente } from '../../api/tipos';
import { formatarCep, formatarCpf, formatarDataIso, formatarTelefone } from '../../util/formatos';

const SEXOS: Record<string, string> = { M: 'Masculino', F: 'Feminino', O: 'Outro' };

function texto(v?: string | null): string {
  return v && v.trim() !== '' ? v : '—';
}

/** Cadastro completo do paciente, só para leitura (abre pela ação "Visualizar" da lista). */
export default function VisualizarPacienteModal({ paciente, aoFechar }: { paciente: Paciente | null; aoFechar: () => void }) {
  const p = paciente;
  const resp = p?.responsavelLegal ?? null;
  return (
    <Modal open={!!p} title={p ? `Cadastro — ${p.nome}` : ''} onCancel={aoFechar} footer={null} width={820} destroyOnClose>
      {p && (
        <>
          <Descriptions title="Dados pessoais" size="small" bordered column={{ xs: 1, md: 2 }} style={{ marginBottom: 16 }}>
            <Descriptions.Item label="Nome">
              {p.nome} {p.menorDeIdade && <Tag color="gold">Menor</Tag>}
            </Descriptions.Item>
            <Descriptions.Item label="Situação">{p.ativo ? <Tag color="green">Ativo</Tag> : <Tag>Inativo</Tag>}</Descriptions.Item>
            {p.estrangeiro ? (
              <Descriptions.Item label={`Documento (${texto(p.tipoDocumentoEstrangeiro)})`}>{texto(p.numeroDocumentoEstrangeiro)}</Descriptions.Item>
            ) : (
              <Descriptions.Item label="CPF">{p.cpf ? formatarCpf(p.cpf) : '—'}</Descriptions.Item>
            )}
            <Descriptions.Item label="RG">{texto(p.rg)}</Descriptions.Item>
            <Descriptions.Item label="Nascimento">{formatarDataIso(p.dataNascimento)}</Descriptions.Item>
            <Descriptions.Item label="Sexo">{p.sexo ? SEXOS[p.sexo] ?? p.sexo : '—'}</Descriptions.Item>
            <Descriptions.Item label="Escolaridade">{texto(p.escolaridade)}</Descriptions.Item>
            <Descriptions.Item label="Profissional responsável">{p.profissionalResponsavel.nome}</Descriptions.Item>
            <Descriptions.Item label="Mãe">{p.maeNaoPossui ? 'Não possui' : texto(p.nomeMae)}</Descriptions.Item>
            <Descriptions.Item label="Pai">{texto(p.nomePai)}</Descriptions.Item>
          </Descriptions>

          <Descriptions title="Contato" size="small" bordered column={{ xs: 1, md: 2 }} style={{ marginBottom: 16 }}>
            <Descriptions.Item label="Celular">{p.telefone ? formatarTelefone(p.telefone) : '—'}</Descriptions.Item>
            <Descriptions.Item label="E-mail">{texto(p.email)}</Descriptions.Item>
          </Descriptions>

          <Descriptions title="Endereço" size="small" bordered column={{ xs: 1, md: 2 }} style={{ marginBottom: 16 }}>
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
            <Descriptions title="Responsável legal" size="small" bordered column={{ xs: 1, md: 2 }}>
              <Descriptions.Item label="Nome">{resp.nome}</Descriptions.Item>
              <Descriptions.Item label="CPF">{formatarCpf(resp.cpf)}</Descriptions.Item>
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
