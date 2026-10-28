import { Button, Input, Select, Space, Typography } from 'antd';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import type { Horario, TipoHorario } from '../../api/tipos';

export const DIAS = [
  { value: 1, label: 'Segunda-feira' },
  { value: 2, label: 'Terça-feira' },
  { value: 3, label: 'Quarta-feira' },
  { value: 4, label: 'Quinta-feira' },
  { value: 5, label: 'Sexta-feira' },
  { value: 6, label: 'Sábado' },
  { value: 7, label: 'Domingo' }
];

const TIPOS: { value: TipoHorario; label: string }[] = [
  { value: 'ATENDIMENTO', label: 'Atendimento' },
  { value: 'INTERVALO', label: 'Intervalo' }
];

interface Props {
  value?: Horario[];
  onChange?: (valor: Horario[]) => void;
  /** Unidades do profissional que podem receber horários. */
  unidades: { value: number; label: string }[];
}

/** Editor da grade semanal: uma linha por período de atendimento ou de intervalo. Usado dentro de um Form.Item. */
export default function HorariosEditor({ value = [], onChange, unidades }: Props) {
  const mudar = (indice: number, parte: Partial<Horario>) =>
    onChange?.(value.map((h, i) => (i === indice ? { ...h, ...parte } : h)));
  const remover = (indice: number) => onChange?.(value.filter((_, i) => i !== indice));
  const adicionar = () =>
    onChange?.([
      ...value,
      {
        unidadeId: unidades.length === 1 ? unidades[0].value : undefined,
        diaSemana: 1,
        tipo: 'ATENDIMENTO',
        horaInicio: '08:00',
        horaFim: '12:00'
      }
    ]);

  return (
    <div>
      {value.length === 0 && (
        <Typography.Paragraph type="secondary">
          Nenhum horário. Clique em "Adicionar horário" e informe os dias e horas de atendimento.
        </Typography.Paragraph>
      )}
      {value.map((h, i) => (
        <Space key={i} wrap style={{ display: 'flex', marginBottom: 8 }}>
          <Select
            placeholder="Unidade"
            style={{ width: 220 }}
            value={h.unidadeId}
            options={unidades}
            onChange={(v) => mudar(i, { unidadeId: v })}
          />
          <Select style={{ width: 150 }} value={h.diaSemana} options={DIAS} onChange={(v) => mudar(i, { diaSemana: v })} />
          <Select<TipoHorario> style={{ width: 130 }} value={h.tipo} options={TIPOS} onChange={(v) => mudar(i, { tipo: v })} />
          <Input type="time" style={{ width: 110 }} value={h.horaInicio} onChange={(e) => mudar(i, { horaInicio: e.target.value })} />
          <span>às</span>
          <Input type="time" style={{ width: 110 }} value={h.horaFim} onChange={(e) => mudar(i, { horaFim: e.target.value })} />
          <Button danger icon={<DeleteOutlined />} title="Remover" onClick={() => remover(i)} />
        </Space>
      ))}
      <Button icon={<PlusOutlined />} onClick={adicionar} disabled={unidades.length === 0}>
        Adicionar horário
      </Button>
      {unidades.length === 0 && (
        <Typography.Text type="secondary" style={{ marginLeft: 8 }}>
          Escolha ao menos uma unidade nos dados do profissional.
        </Typography.Text>
      )}
    </div>
  );
}
