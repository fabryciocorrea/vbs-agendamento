import { useState } from 'react';
import { App, Button, Dropdown } from 'antd';
import { DownloadOutlined, FileExcelOutlined, FilePdfOutlined } from '@ant-design/icons';

interface Props {
  onPdf: () => Promise<void>;
  onCsv: () => Promise<void>;
  disabled?: boolean;
}

/** Botão "Exportar" com as duas opções: PDF e CSV. */
export default function MenuExportar({ onPdf, onCsv, disabled }: Props) {
  const { message } = App.useApp();
  const [exportando, setExportando] = useState(false);

  async function executar(acao: () => Promise<void>) {
    setExportando(true);
    try {
      await acao();
    } catch (e) {
      message.error(e instanceof Error ? e.message : 'Não foi possível exportar.');
    } finally {
      setExportando(false);
    }
  }

  return (
    <Dropdown
      disabled={disabled || exportando}
      menu={{
        items: [
          { key: 'pdf', label: 'PDF', icon: <FilePdfOutlined />, onClick: () => void executar(onPdf) },
          { key: 'csv', label: 'CSV (Excel)', icon: <FileExcelOutlined />, onClick: () => void executar(onCsv) }
        ]
      }}
    >
      <Button icon={<DownloadOutlined />} loading={exportando} disabled={disabled}>
        Exportar
      </Button>
    </Dropdown>
  );
}
