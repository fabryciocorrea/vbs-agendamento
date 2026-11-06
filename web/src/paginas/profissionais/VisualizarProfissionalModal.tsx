import { Descriptions, Modal, Space, Tag } from 'antd';
import type { Profissional } from '../../api/tipos';
import { formatarCpf, formatarDataIso, formatarTelefone } from '../../util/formatos';

function texto(v?: string | null): string {
  return v && v.trim() !== '' ? v : '—';
}

const REGISTROS: Record<string, string> = { CRM: 'CRM (médico)', CRP: 'CRP (psicólogo)' };

/** Cadastro completo do profissional, só para leitura (mesmo estilo da visualização do paciente). */
export default function VisualizarProfissionalModal({ profissional, aoFechar }: { profissional: Profissional | null; aoFechar: () => void }) {
  const p = profissional;
  return (
    <Modal open={!!p} title={p ? `Cadastro — ${p.nome}` : ''} onCancel={aoFechar} footer={null} width={900} destroyOnClose>
      {p && (
        <>
          <Descriptions title="Dados pessoais" size="small" bordered column={2} labelStyle={{ width: 170 }} contentStyle={{ width: 230 }} style={{ marginBottom: 16 }}>
            <Descriptions.Item label="Nome">{p.nome}</Descriptions.Item>
            <Descriptions.Item label="Situação">{p.ativo ? <Tag color="green">Ativo</Tag> : <Tag>Inativo</Tag>}</Descriptions.Item>
            <Descriptions.Item label="CPF">{p.cpf ? formatarCpf(p.cpf) : '—'}</Descriptions.Item>
            <Descriptions.Item label="RG">{texto(p.rg)}</Descriptions.Item>
            <Descriptions.Item label="Nascimento">{formatarDataIso(p.dataNascimento)}</Descriptions.Item>
            <Descriptions.Item label="Escolaridade">{texto(p.escolaridade)}</Descriptions.Item>
          </Descriptions>

          <Descriptions title="Contato" size="small" bordered column={2} labelStyle={{ width: 170 }} contentStyle={{ width: 230 }} style={{ marginBottom: 16 }}>
            <Descriptions.Item label="Celular">{p.telefone ? formatarTelefone(p.telefone) : '—'}</Descriptions.Item>
            <Descriptions.Item label="E-mail">{texto(p.email)}</Descriptions.Item>
          </Descriptions>

          <Descriptions title="Endereço" size="small" bordered column={2} labelStyle={{ width: 170 }} contentStyle={{ width: 230 }} style={{ marginBottom: 16 }}>
            <Descriptions.Item label="Endereço">
              {texto(p.endereco)}, {texto(p.numero)}
            </Descriptions.Item>
            <Descriptions.Item label="Complemento">{texto(p.complemento)}</Descriptions.Item>
            <Descriptions.Item label="Cidade/UF">
              {texto(p.cidade)}/{texto(p.uf)}
            </Descriptions.Item>
          </Descriptions>

          <Descriptions title="Conselho profissional" size="small" bordered column={2} labelStyle={{ width: 170 }} contentStyle={{ width: 230 }} style={{ marginBottom: 16 }}>
            <Descriptions.Item label="Registro">{REGISTROS[p.registroProfissional] ?? p.registroProfissional}</Descriptions.Item>
            <Descriptions.Item label="Número">{texto(p.numeroConselho)}</Descriptions.Item>
            <Descriptions.Item label="UF do conselho">{texto(p.ufConselho)}</Descriptions.Item>
          </Descriptions>

          <Descriptions title="Atendimento" size="small" bordered column={1} labelStyle={{ width: 180 }}>
            <Descriptions.Item label="Especialidades">
              {p.especialidades.length > 0 ? (
                <Space size={[4, 4]} wrap>
                  {p.especialidades.map((e) => (
                    <Tag key={e.id}>{e.nome}</Tag>
                  ))}
                </Space>
              ) : (
                '—'
              )}
            </Descriptions.Item>
            <Descriptions.Item label="Unidades">
              {p.unidades.length > 0 ? (
                <Space size={[4, 4]} wrap>
                  {p.unidades.map((u) => (
                    <Tag key={u.id}>{u.nome}</Tag>
                  ))}
                </Space>
              ) : (
                '—'
              )}
            </Descriptions.Item>
          </Descriptions>
        </>
      )}
    </Modal>
  );
}
