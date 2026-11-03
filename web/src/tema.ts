import { theme, type ThemeConfig } from 'antd';

// Cores extraídas do logotipo da Viver Bem.
export const cores = {
  verde: '#023926',
  verdeClaro: '#0b5a3c',
  amarelo: '#FECE22',
  fundo: '#f4f6f4'
};

const componentesComuns: ThemeConfig['components'] = {
  Menu: {
    darkItemBg: 'transparent',
    darkSubMenuItemBg: 'transparent',
    darkItemColor: 'rgba(255,255,255,0.88)',
    darkItemHoverBg: 'rgba(255,255,255,0.10)',
    darkItemHoverColor: '#ffffff',
    darkItemSelectedBg: '#6fbf3f',
    darkItemSelectedColor: '#ffffff',
    itemBorderRadius: 12,
    itemMarginInline: 10,
    itemMarginBlock: 3,
    itemHeight: 42,
    iconSize: 17
  },
  Button: { primaryShadow: 'none' }
};

export const temaClaro: ThemeConfig = {
  token: {
    colorPrimary: cores.verde,
    colorLink: cores.verdeClaro,
    colorInfo: cores.verdeClaro,
    colorBgLayout: cores.fundo,
    borderRadius: 10,
    borderRadiusLG: 16
  },
  components: {
    ...componentesComuns,
    Layout: { siderBg: cores.verde, triggerBg: '#01281a', headerBg: '#ffffff', headerPadding: '0 24px' },
    Table: { headerBg: '#eaf0ec', headerBorderRadius: 12 }
  }
};

export const temaEscuro: ThemeConfig = {
  algorithm: theme.darkAlgorithm,
  token: {
    // O verde da marca é escuro demais para botões e links sobre fundo escuro.
    colorPrimary: '#2b8a62',
    colorLink: '#5fc79a',
    colorInfo: '#5fc79a',
    colorBgLayout: '#0f1411',
    colorBgContainer: '#18201b',
    colorBgElevated: '#1f2a24',
    borderRadius: 10,
    borderRadiusLG: 16
  },
  components: {
    ...componentesComuns,
    Layout: { siderBg: '#01281a', triggerBg: '#011c12', headerBg: '#18201b', headerPadding: '0 24px' },
    Table: { headerBg: '#1f2a24', headerBorderRadius: 12 }
  }
};

/** Mantido para quem ainda importa `tema`. */
export const tema = temaClaro;
