import type { ThemeConfig } from 'antd';

// Cores extraídas do logotipo da Viver Bem.
export const cores = {
  verde: '#023926',
  verdeClaro: '#0b5a3c',
  amarelo: '#FECE22',
  fundo: '#f4f6f4'
};

export const tema: ThemeConfig = {
  token: {
    colorPrimary: cores.verde,
    colorLink: cores.verdeClaro,
    colorInfo: cores.verdeClaro,
    colorBgLayout: cores.fundo,
    borderRadius: 8
  },
  components: {
    Layout: { siderBg: cores.verde, triggerBg: '#01281a', headerBg: '#ffffff', headerPadding: '0 24px' },
    Menu: {
      darkItemBg: cores.verde,
      darkSubMenuItemBg: cores.verde,
      darkItemColor: 'rgba(255,255,255,0.85)',
      darkItemHoverBg: '#0b5a3c',
      darkItemHoverColor: '#ffffff',
      darkItemSelectedBg: cores.amarelo,
      darkItemSelectedColor: cores.verde
    },
    Button: { primaryShadow: 'none' },
    Table: { headerBg: '#eaf0ec' }
  }
};
