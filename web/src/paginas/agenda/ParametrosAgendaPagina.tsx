import { useCallback, useEffect, useState } from 'react';
import { App, Button, Card, Form, Input, InputNumber, Popconfirm, Space, Table, Tag, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { api, ApiErro } from '../../api/cliente';
import { useAuth } from '../../auth/AuthContext';
import { formatarDataIso } from '../../util/formatos';
import type { Feriado, ParametrosAgenda } from './tipos';

const OBRIGATORIO = 'Campo Não Preenchido';

interface ValoresParametros {
  duracaoPadraoConsultaMin: number | null;
  antecedenciaCancelamentoHoras: number | null;
  limiteEncaixesDia: number | null;
  recessoFimAnoInicio: string;
  recessoFimAnoFim: string;
}

function CartaoParametros({ podeEditar }: { podeEditar: boolean }) {
  const { message } = App.useApp();
  const [form] = Form.useForm<ValoresParametros>();
  const [salvando, setSalvando] = useState(false);
  const [carregando, setCarregando] = useState(true);

  useEffect(() => {
    api
      .get<ParametrosAgenda>('/parametros-agenda')
      .then((p) =>
        form.setFieldsValue({
          duracaoPadraoConsultaMin: p.duracaoPadraoConsultaMin,
          antecedenciaCancelamentoHoras: p.antecedenciaCancelamentoHoras,
          limiteEncaixesDia: p.limiteEncaixesDia ?? null,
          recessoFimAnoInicio: p.recessoFimAnoInicio ?? '',
          recessoFimAnoFim: p.recessoFimAnoFim ?? ''
        })
      )
      .catch((e) => message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os parâmetros.'))
      .finally(() => setCarregando(false));
  }, [form, message]);

  async function salvar() {
    let v: ValoresParametros;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    const corpo: ParametrosAgenda = {
      duracaoPadraoConsultaMin: v.duracaoPadraoConsultaMin ?? 0,
      antecedenciaCancelamentoHoras: v.antecedenciaCancelamentoHoras ?? 0,
      limiteEncaixesDia: v.limiteEncaixesDia ?? null,
      recessoFimAnoInicio: v.recessoFimAnoInicio || null,
      recessoFimAnoFim: v.recessoFimAnoFim || null
    };
    setSalvando(true);
    try {
      await api.put<ParametrosAgenda>('/parametros-agenda', corpo);
      message.success('Parâmetros gravados. Valem para os novos agendamentos.');
    } catch (e) {
      if (e instanceof ApiErro && e.erros.length > 0) {
        form.setFields(e.erros.map((x) => ({ name: x.campo as keyof ValoresParametros, errors: [x.mensagem] })));
      } else {
        message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      }
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Card title="Parâmetros da agenda" loading={carregando} style={{ marginBottom: 24 }}>
      <Form<ValoresParametros> form={form} layout="vertical" requiredMark disabled={!podeEditar} style={{ maxWidth: 560 }}>
        <Form.Item
          name="duracaoPadraoConsultaMin"
          label="Duração padrão da consulta (minutos)"
          rules={[{ required: true, message: OBRIGATORIO }]}
        >
          <InputNumber min={5} max={240} style={{ width: 160 }} />
        </Form.Item>
        <Form.Item
          name="antecedenciaCancelamentoHoras"
          label="Antecedência mínima para cancelamento (horas)"
          rules={[{ required: true, message: OBRIGATORIO }]}
        >
          <InputNumber min={0} max={720} style={{ width: 160 }} />
        </Form.Item>
        <Form.Item name="limiteEncaixesDia" label="Limite de encaixes por profissional e por dia" extra="Vazio: sem limite.">
          <InputNumber min={0} style={{ width: 160 }} />
        </Form.Item>
        <Space align="start" wrap>
          <Form.Item name="recessoFimAnoInicio" label="Recesso de fim de ano - início">
            <Input type="date" style={{ width: 170 }} />
          </Form.Item>
          <Form.Item name="recessoFimAnoFim" label="Recesso de fim de ano - fim">
            <Input type="date" style={{ width: 170 }} />
          </Form.Item>
        </Space>
        <Typography.Paragraph type="secondary">
          Durante o recesso a agenda fica fechada para novos agendamentos. As consultas já marcadas não mudam.
        </Typography.Paragraph>
        {podeEditar && (
          <Button type="primary" loading={salvando} onClick={() => void salvar()}>
            Salvar parâmetros
          </Button>
        )}
      </Form>
    </Card>
  );
}

function CartaoFeriados({ podeEditar }: { podeEditar: boolean }) {
  const { message } = App.useApp();
  const anoAtual = new Date().getFullYear();
  const [ano, setAno] = useState<number>(anoAtual);
  const [feriados, setFeriados] = useState<Feriado[]>([]);
  const [carregando, setCarregando] = useState(false);
  const [data, setData] = useState('');
  const [nome, setNome] = useState('');
  const [erroData, setErroData] = useState('');
  const [erroNome, setErroNome] = useState('');
  const [incluindo, setIncluindo] = useState(false);

  const carregar = useCallback(async () => {
    setCarregando(true);
    try {
      setFeriados(await api.get<Feriado[]>('/feriados', { ano }));
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível carregar os feriados.');
    } finally {
      setCarregando(false);
    }
  }, [ano, message]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  async function incluir() {
    setErroData(data ? '' : OBRIGATORIO);
    setErroNome(nome.trim() ? '' : OBRIGATORIO);
    if (!data || !nome.trim()) return;
    setIncluindo(true);
    try {
      await api.post<Feriado>('/feriados', { data, nome: nome.trim() });
      message.success('Feriado incluído.');
      setNome('');
      setData('');
      if (Number(data.slice(0, 4)) === ano) await carregar();
      else setAno(Number(data.slice(0, 4)));
    } catch (e) {
      if (e instanceof ApiErro && e.codigo === 'FERIADO_DUPLICADO') setErroData(e.message);
      else if (e instanceof ApiErro && e.erros.length > 0) {
        for (const x of e.erros) {
          if (x.campo === 'data') setErroData(x.mensagem);
          if (x.campo === 'nome') setErroNome(x.mensagem);
        }
      } else message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
    } finally {
      setIncluindo(false);
    }
  }

  async function remover(f: Feriado) {
    try {
      await api.delete(`/feriados/${f.data}`);
      message.success('Feriado removido.');
      await carregar();
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
    }
  }

  const colunas: TableColumnsType<Feriado> = [
    { title: 'Data', dataIndex: 'data', width: 130, render: (v: string) => formatarDataIso(v) },
    { title: 'Feriado', dataIndex: 'nome' },
    {
      title: 'Origem',
      width: 170,
      render: (_, f) => (f.automatico ? <Tag color="green">Automático</Tag> : <Tag color="gold">Incluído pela clínica</Tag>)
    }
  ];
  if (podeEditar) {
    colunas.push({
      title: 'Ações',
      width: 90,
      render: (_, f) =>
        f.automatico ? null : (
        <Popconfirm
          title="Remover feriado"
          description={`Remover "${f.nome}" (${formatarDataIso(f.data)})? Consultas já marcadas não mudam.`}
          okText="Remover"
          cancelText="Cancelar"
          onConfirm={() => void remover(f)}
        >
          <Button size="small" danger icon={<DeleteOutlined />} title="Remover" />
        </Popconfirm>
        )
    });
  }

  return (
    <Card title="Feriados e dias sem atendimento" extra={<Typography.Text type="secondary">Feriados nacionais, Carnaval (segunda e terça) e Corpus Christi entram sozinhos. Inclua abaixo outros dias em que a clínica fecha.</Typography.Text>}>
      <Space style={{ marginBottom: 16 }} wrap>
        <span>Ano:</span>
        <InputNumber min={2000} max={2100} value={ano} onChange={(v) => v && setAno(v)} style={{ width: 100 }} />
      </Space>

      {podeEditar && (
        <Space align="start" style={{ marginBottom: 16, display: 'flex' }} wrap>
          <div>
            <Input type="date" value={data} onChange={(e) => setData(e.target.value)} style={{ width: 170 }} status={erroData ? 'error' : undefined} />
            {erroData && <div style={{ color: '#ff4d4f', fontSize: 12 }}>{erroData}</div>}
          </div>
          <div>
            <Input
              placeholder="Nome do feriado"
              maxLength={80}
              value={nome}
              onChange={(e) => setNome(e.target.value)}
              style={{ width: 280 }}
              status={erroNome ? 'error' : undefined}
            />
            {erroNome && <div style={{ color: '#ff4d4f', fontSize: 12 }}>{erroNome}</div>}
          </div>
          <Button type="primary" icon={<PlusOutlined />} loading={incluindo} onClick={() => void incluir()}>
            Incluir feriado facultativo
          </Button>
        </Space>
      )}

      <Table<Feriado>
        rowKey="data"
        size="middle"
        loading={carregando}
        columns={colunas}
        dataSource={feriados}
        pagination={false}
        locale={{ emptyText: `Nenhum feriado cadastrado em ${ano}` }}
      />
    </Card>
  );
}

export default function ParametrosAgendaPagina() {
  const { pode } = useAuth();
  const podeEditar = pode('agenda.parametros', 'editar');
  return (
    <>
      <Typography.Title level={3} style={{ marginTop: 0 }}>
        Parâmetros da agenda
      </Typography.Title>
      <CartaoParametros podeEditar={podeEditar} />
      <CartaoFeriados podeEditar={podeEditar} />
    </>
  );
}
