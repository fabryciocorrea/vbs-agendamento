import { Checkbox, Input } from 'antd';

export const SEM_NUMERO = 'S/N';

interface Props {
  value?: string;
  onChange?: (valor: string) => void;
  disabled?: boolean;
}

/**
 * Campo "Número" do endereço. Marcar "S/N" preenche o campo com S/N e o trava;
 * desmarcar limpa o campo para digitar o número. Usado dentro de um Form.Item.
 */
export default function CampoNumeroEndereco({ value, onChange, disabled }: Props) {
  const semNumero = value === SEM_NUMERO;
  return (
    <Input
      maxLength={10}
      value={value}
      disabled={disabled || semNumero}
      onChange={(e) => onChange?.(e.target.value)}
      addonAfter={
        <Checkbox
          checked={semNumero}
          disabled={disabled}
          onChange={(e) => onChange?.(e.target.checked ? SEM_NUMERO : '')}
        >
          S/N
        </Checkbox>
      }
    />
  );
}
