import { useCallback, useEffect, useState } from 'react';
import { Alert, App, Button, Card, Col, Input, Row, Select, Space, Spin, Statistic, Tabs, Typography } from 'antd';
import { DownloadOutlined } from '@ant-design/icons';
import { api, ApiErro, obterToken } from '../../api/cliente';
import type { ItemApoio } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import { hojeIso } from '../../util/formatos';
import GuiasConvenioAba from './FaturamentoGuiasPagina';
import ParticularesAba from './ParticularesAba';

const REAIS = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

interface QuantidadeValor {
  quantidade: number;
  valor: number;
}

interface Resumo {
  concluidas: number;
  valorTotal: number;
  particular: { quantidade: number; total: number; recebido: QuantidadeValor; naoConfirmado: QuantidadeValor };
  convenio: { quantidade: number; total: number; autorizado: QuantidadeValor; pendente: QuantidadeValor; negado: QuantidadeValor };
}

function Indicador({ titulo, v, cor }: { titulo: string; v: QuantidadeValor; cor?: string }) {
  return (
    <Card size="small">
      <Statistic title={titulo} value={REAIS.format(v.valor)} valueStyle={{ color: cor, fontSize: 22 }} />
      <Typography.Text type="secondary">{v.quantidade} consulta(s)</Typography.Text>
    </Card>
  );
}

function ResumoAba() {
  const { message } = App.useApp();
  const { pode } = useAuth();
  const podeExportar = pode('faturamento.financeiro', 'exportar');

  const [de, setDe] = useState(() => hojeIso().slice(0, 8) + '01');
  const [ate, setAte] = useState(() => hojeIso());
  const [unidadeId, setUnidadeId] = useState<number | undefined>();
  const [unidades, setUnidades] = useState<ItemApoio[]>([]);
  const [resumo, setResumo] = useState<Resumo | null>(null);
  const [carregando, setCarregando] = useState(false);
  const [exportando, setExportando] = useState(false);
  const [erro, setErro] = useState('');

  useEffect(() => {
    api.get<ItemApoio[]>('/apoio/unidades').then(setUnidades).catch(() => undefined);
  }, []);

  const carregar = useCallback(async () => {
    if (!de || !ate) {
      setErro('Informe a data inicial e a data final.');
      setResumo(null);
      return;
    }
    setErro('');
    setCarregando(true);
    try {
      setResumo(await api.get<Resumo>('/faturamento/resumo', { dataInicial: de, dataFinal: ate, unidadeId }));
    } catch (e) {
      setResumo(null);
      setErro(e instanceof ApiErro ? e.message : 'Não foi possível carregar o resumo.');
    } finally {
      setCarregando(false);
    }
  }, [de, ate, unidadeId]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  async function exportar() {
    setExportando(true);
    try {
      const consulta = new URLSearchParams({ dataInicial: de, dataFinal: ate });
      if (unidadeId) consulta.set('unidadeId', String(unidadeId));
      const resposta = await fetch(`/api/v1/faturamento/exportacao?${consulta.toString()}`, {
        headers: { Authorization: `Bearer ${obterToken() ?? ''}` }
      });
      if (!resposta.ok) {
        let texto = 'Não foi possível exportar.';
        try {
          const j = await resposta.json();
          texto = j.detail ?? j.title ?? texto;
        } catch {
          // mantém a mensagem padrão
        }
        throw new Error(texto);
      }
      const blob = await resposta.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `faturamento_${de.replace(/-/g, '')}_${ate.replace(/-/g, '')}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (e) {
      message.error(e instanceof Error ? e.message : 'Não foi possível exportar.');
    } finally {
      setExportando(false);
    }
  }

  return (
    <>
      <Typography.Paragraph type="secondary">
        Valores das consultas concluídas no período. No particular, recebido é o pagamento confirmado pela recepção; no convênio, vale a situação da guia.
      </Typography.Paragraph>

      <Space style={{ marginBottom: 16 }} wrap>
        <Input type="date" style={{ width: 160 }} value={de} onChange={(e) => setDe(e.target.value)} title="Consultas a partir de" />
        <Input type="date" style={{ width: 160 }} value={ate} onChange={(e) => setAte(e.target.value)} title="Consultas até" />
        <Select
          allowClear
          placeholder="Todas as unidades"
          style={{ width: 220 }}
          value={unidadeId}
          onChange={setUnidadeId}
          options={unidades.map((u) => ({ value: u.id, label: u.nome }))}
        />
        {podeExportar && (
          <Button icon={<DownloadOutlined />} loading={exportando} disabled={!resumo} onClick={() => void exportar()}>
            Exportar CSV
          </Button>
        )}
      </Space>

      {erro && <Alert type="error" showIcon message={erro} style={{ marginBottom: 16 }} />}

      <Spin spinning={carregando}>
        {resumo && (
          <>
            <Row gutter={[16, 16]}>
              <Col xs={24} md={12}>
                <Card size="small">
                  <Statistic title="Consultas concluídas" value={resumo.concluidas} />
                </Card>
              </Col>
              <Col xs={24} md={12}>
                <Card size="small">
                  <Statistic title="Valor total concluído" value={REAIS.format(resumo.valorTotal)} />
                </Card>
              </Col>
            </Row>

            <Typography.Title level={5} style={{ marginTop: 24 }}>
              Particular - {REAIS.format(resumo.particular.total)} ({resumo.particular.quantidade} consulta(s))
            </Typography.Title>
            <Row gutter={[16, 16]}>
              <Col xs={24} md={12}>
                <Indicador titulo="Recebido (pagamento confirmado)" v={resumo.particular.recebido} cor="#389e0d" />
              </Col>
              <Col xs={24} md={12}>
                <Indicador titulo="Sem pagamento confirmado" v={resumo.particular.naoConfirmado} cor="#cf1322" />
              </Col>
            </Row>

            <Typography.Title level={5} style={{ marginTop: 24 }}>
              Convênio - {REAIS.format(resumo.convenio.total)} ({resumo.convenio.quantidade} consulta(s))
            </Typography.Title>
            <Row gutter={[16, 16]}>
              <Col xs={24} md={8}>
                <Indicador titulo="Guia autorizada" v={resumo.convenio.autorizado} cor="#389e0d" />
              </Col>
              <Col xs={24} md={8}>
                <Indicador titulo="Guia pendente" v={resumo.convenio.pendente} cor="#d48806" />
              </Col>
              <Col xs={24} md={8}>
                <Indicador titulo="Guia negada" v={resumo.convenio.negado} cor="#cf1322" />
              </Col>
            </Row>
          </>
        )}
      </Spin>
    </>
  );
}

export default function FaturamentoFinanceiroPagina() {
  return (
    <>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        Faturamento
      </Typography.Title>
      <Tabs
        defaultActiveKey="resumo"
        items={[
          { key: 'resumo', label: 'Resumo financeiro', children: <ResumoAba /> },
          { key: 'convenio', label: 'Convênio (guias)', children: <GuiasConvenioAba /> },
          { key: 'particular', label: 'Particular (pagamentos)', children: <ParticularesAba /> }
        ]}
      />
    </>
  );
}
