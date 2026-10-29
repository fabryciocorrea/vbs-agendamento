import { useState } from 'react';
import { App, Input, Space, Typography } from 'antd';

type Api = ReturnType<typeof App.useApp>;

function Campo({ aoMudar }: { aoMudar: (v: string) => void }) {
  const [tamanho, setTamanho] = useState(0);
  return (
    <div style={{ width: '100%', marginBottom: 12 }}>
      <Input.TextArea
        autoFocus
        rows={3}
        maxLength={500}
        placeholder="Justificativa (mínimo de 10 caracteres)"
        onChange={(ev) => {
          aoMudar(ev.target.value);
          setTamanho(ev.target.value.length);
        }}
      />
      <div style={{ textAlign: 'right', fontSize: 12, color: '#8c8c8c', marginTop: 4 }}>{tamanho} / 500</div>
    </div>
  );
}

/**
 * Janela só com o campo de justificativa, aberta ao administrador quando uma regra bloqueia a operação.
 * Devolve a justificativa (10 a 500 caracteres) ou null se ele voltar sem liberar.
 */
export function pedirJustificativa(modal: Api['modal'], message: Api['message'], aviso: string): Promise<string | null> {
  return new Promise<string | null>((resolve) => {
    let texto = '';
    modal.confirm({
      title: 'Justificativa da liberação',
      width: 520,
      okText: 'Liberar',
      cancelText: 'Voltar',
      maskClosable: false,
      content: (
        <Space direction="vertical" style={{ width: '100%' }}>
          <Typography.Text type="secondary">{aviso}</Typography.Text>
          <Campo aoMudar={(v) => (texto = v)} />
        </Space>
      ),
      onOk: () => {
        if (texto.trim().length < 10) {
          message.warning('Informe a justificativa, com ao menos 10 caracteres.');
          return Promise.reject(new Error('justificativa curta'));
        }
        resolve(texto.trim());
        return undefined;
      },
      onCancel: () => resolve(null)
    });
  });
}
