import { useEffect, useMemo, useState } from 'react';
import { Alert, App, Button, Checkbox, Col, Descriptions, Divider, Form, Input, Modal, Row, Select, Space, Switch, Tag } from 'antd';
import { api, ApiErro } from '../../api/cliente';
import type { ConsentimentoPaciente, ItemApoio, Paciente, ResponsavelLegal } from '../../api/tipos';
import { useAuth } from '../../auth/AuthContext';
import CampoNumeroEndereco from '../../componentes/CampoNumeroEndereco';
import ConsentimentosModal, { estadoAtual } from './ConsentimentosModal';
import { HistoricoProfissionaisModal, TrocaProfissionalModal } from './ProfissionalResponsavelModais';
import ResponsavelModal from './ResponsavelModal';
import { celularValido, cpfValido, ehMenorDeIdade, formatarCelular, formatarCpf, formatarDataIso, formatarTelefone, hojeIso, somenteDigitos, UFS } from '../../util/formatos';

const OBRIGATORIO = 'Campo Não Preenchido';
const ufOpcoes = UFS.map((u) => ({ value: u, label: u }));

interface ValoresFormulario {
  nome: string;
  dataNascimento: string;
  estrangeiro: boolean;
  cpf?: string;
  tipoDocumentoEstrangeiro?: string;
  numeroDocumentoEstrangeiro?: string;
  rg?: string;
  escolaridade?: string;
  telefone?: string;
  email?: string;
  endereco: string;
  numero: string;
  complemento?: string;
  cidade: string;
  uf: string;
  usaConvenio: boolean;
  convenioId?: number;
  numeroCarteirinha?: string;
  validadeCarteirinha?: string;
  profissionalId?: number;
  consTratamento?: boolean;
  consContato?: boolean;
  consOrigem?: 'PRESENCIAL' | 'TELEFONE';
}

interface Props {
  aberto: boolean;
  /** Nulo para cadastrar um novo paciente. */
  paciente: Paciente | null;
  /** CPF já digitado na busca por CPF, para começar o cadastro com ele preenchido. */
  cpfInicial?: string;
  aoFechar: () => void;
  aoSalvar: () => void;
}

// Nome do campo do servidor -> nome do campo deste formulário.
function campoDoFormulario(campo: string): keyof ValoresFormulario | null {
  switch (campo) {
    case 'convenio.convenioId': return 'convenioId';
    case 'convenio.numeroCarteirinha': return 'numeroCarteirinha';
    case 'convenio.dataValidadeCarteirinha': return 'validadeCarteirinha';
    case 'profissionalResponsavelId': return 'profissionalId';
    case 'nome': case 'dataNascimento': case 'cpf': case 'tipoDocumentoEstrangeiro': case 'numeroDocumentoEstrangeiro':
    case 'rg': case 'escolaridade': case 'telefone': case 'email': case 'endereco': case 'numero': case 'complemento': case 'cidade': case 'uf':
      return campo;
    default:
      return /^consentimentos\[\d+\]\.origem$/.test(campo) ? 'consOrigem' : null;
  }
}

export default function FormularioPaciente({ aberto, paciente, cpfInicial, aoFechar, aoSalvar }: Props) {
  const { message } = App.useApp();
  const { pode } = useAuth();
  const podeEditar = pode('paciente.cadastro', 'editar');
  const [form] = Form.useForm<ValoresFormulario>();
  const [salvando, setSalvando] = useState(false);
  const [convenios, setConvenios] = useState<ItemApoio[]>([]);
  const [profissionais, setProfissionais] = useState<ItemApoio[]>([]);
  const [avisos, setAvisos] = useState<string[]>([]);

  // Parte 2: responsável legal, troca de profissional e consentimentos.
  const [responsavel, setResponsavel] = useState<ResponsavelLegal | null>(null);
  const [cpfResponsavel, setCpfResponsavel] = useState('');
  const [erroCpfResponsavel, setErroCpfResponsavel] = useState<string | null>(null);
  const [buscandoResponsavel, setBuscandoResponsavel] = useState(false);
  const [modalResponsavel, setModalResponsavel] = useState<{ responsavel: ResponsavelLegal | null; cpf?: string; sugestao?: Paciente | null } | null>(null);
  const [profissionalAtual, setProfissionalAtual] = useState<{ id: number; nome: string } | null>(null);
  const [consentimentos, setConsentimentos] = useState<ConsentimentoPaciente[]>([]);
  const [modalTroca, setModalTroca] = useState(false);
  const [modalHistorico, setModalHistorico] = useState(false);
  const [modalConsentimentos, setModalConsentimentos] = useState(false);
  const [alterouRelacionados, setAlterouRelacionados] = useState(false);

  const estrangeiro = Form.useWatch('estrangeiro', form);
  const usaConvenio = Form.useWatch('usaConvenio', form);
  const dataNascimento = Form.useWatch('dataNascimento', form);
  const cpfDigitado = Form.useWatch('cpf', form);

  useEffect(() => {
    if (!aberto) return;
    setAvisos([]);
    setResponsavel(paciente?.responsavelLegal ?? null);
    setCpfResponsavel('');
    setErroCpfResponsavel(null);
    setProfissionalAtual(paciente ? paciente.profissionalResponsavel : null);
    setConsentimentos(paciente?.consentimentosAtuais ?? []);
    setAlterouRelacionados(false);
    api.get<ItemApoio[]>('/apoio/convenios').then(setConvenios).catch(() => message.error('Não foi possível carregar a lista de convênios.'));
    if (!paciente)
      api
        .get<ItemApoio[]>('/apoio/profissionais')
        .then(setProfissionais)
        .catch(() => message.error('Não foi possível carregar a lista de profissionais.'));
  }, [aberto, paciente, message]);

  const opcoesConvenios = useMemo(() => {
    const lista = convenios.map((c) => ({ value: c.id, label: c.nome }));
    const atual = paciente?.convenio?.convenio;
    if (atual && !lista.some((o) => o.value === atual.id)) lista.push({ value: atual.id, label: atual.nomeFantasia });
    return lista;
  }, [convenios, paciente]);

  const opcoesProfissionais = useMemo(
    () => profissionais.map((p) => ({ value: p.id, label: p.complemento ? `${p.nome} (${p.complemento})` : p.nome })),
    [profissionais]
  );

  const menor = ehMenorDeIdade(dataNascimento);

  // Ao mudar a data de nascimento, a obrigatoriedade de telefone e e-mail muda; limpa avisos antigos desses campos.
  useEffect(() => {
    if (!aberto) return;
    form.setFields([
      { name: 'telefone', errors: [] },
      { name: 'email', errors: [] }
    ]);
  }, [aberto, menor, form]);

  // E4: busca o responsável legal pelo CPF; se não existir, abre o cadastro com o CPF preenchido.
  async function buscarResponsavel() {
    const cpf = somenteDigitos(cpfResponsavel);
    setErroCpfResponsavel(null);
    if (cpf === '') return setErroCpfResponsavel(OBRIGATORIO);
    if (!cpfValido(cpf)) return setErroCpfResponsavel('CPF inválido.');
    setBuscandoResponsavel(true);
    try {
      setResponsavel(await api.post<ResponsavelLegal>('/responsaveis-legais/busca-por-cpf', { cpf }));
      setCpfResponsavel('');
    } catch (e) {
      if (e instanceof ApiErro && e.codigo === 'RESPONSAVEL_NAO_CADASTRADO') {
        // Se o CPF for de um paciente adulto já cadastrado, aproveita os dados dele para adiantar o cadastro.
        let sugestao: Paciente | null = null;
        try {
          const achado = await api.post<{ id: string; menorDeIdade: boolean }>('/pacientes/busca-por-cpf', { cpf });
          if (!achado.menorDeIdade) sugestao = await api.get<Paciente>(`/pacientes/${achado.id}`);
        } catch {
          sugestao = null;
        }
        setModalResponsavel({ responsavel: null, cpf, sugestao });
      }
      else setErroCpfResponsavel(e instanceof ApiErro ? e.message : 'Erro inesperado.');
    } finally {
      setBuscandoResponsavel(false);
    }
  }

  // A lista do paciente traz só o resumo do responsável; para editar, busca o cadastro completo.
  async function editarResponsavel() {
    if (!responsavel) return;
    try {
      const completo = await api.get<ResponsavelLegal>(`/responsaveis-legais/${responsavel.id}`);
      setModalResponsavel({ responsavel: completo });
    } catch (e) {
      message.error(e instanceof ApiErro ? e.message : 'Não foi possível abrir o cadastro do responsável.');
    }
  }

  function fechar() {
    // Troca de profissional e consentimentos valem na hora; a lista precisa recarregar mesmo sem salvar.
    if (alterouRelacionados) aoSalvar();
    else aoFechar();
  }

  async function enviar() {
    setAvisos([]);
    let v: ValoresFormulario;
    try {
      v = await form.validateFields();
    } catch {
      return;
    }
    const opcional = (t?: string) => (t && t.trim() !== '' ? t.trim() : null);
    const cpf = somenteDigitos(v.cpf);
    const corpo = {
      nome: v.nome.trim(),
      estrangeiro: v.estrangeiro,
      cpf: cpf === '' ? null : cpf,
      tipoDocumentoEstrangeiro: v.estrangeiro ? opcional(v.tipoDocumentoEstrangeiro) : null,
      numeroDocumentoEstrangeiro: v.estrangeiro ? opcional(v.numeroDocumentoEstrangeiro) : null,
      rg: opcional(v.rg),
      dataNascimento: v.dataNascimento,
      telefone: somenteDigitos(v.telefone) === '' ? null : somenteDigitos(v.telefone),
      email: opcional(v.email),
      endereco: v.endereco.trim(),
      numero: v.numero.trim(),
      complemento: opcional(v.complemento),
      cidade: v.cidade.trim(),
      uf: v.uf,
      escolaridade: opcional(v.escolaridade),
      convenio: v.usaConvenio
        ? { convenioId: v.convenioId, numeroCarteirinha: v.numeroCarteirinha?.trim(), dataValidadeCarteirinha: v.validadeCarteirinha }
        : null,
      // Na alteração, o responsável legal que já existe é mantido.
      responsavelLegalId: responsavel?.id ?? null
    };

    setSalvando(true);
    try {
      if (paciente) {
        await api.put<Paciente>(`/pacientes/${paciente.id}`, corpo);
      } else {
        const origem = v.consOrigem;
        await api.post<Paciente>('/pacientes', {
          ...corpo,
          profissionalResponsavelId: v.profissionalId,
          consentimentos: [
            { finalidade: 'TRATAMENTO_DADOS', concedido: v.consTratamento === true, origem },
            { finalidade: 'CONTATO_SMS_WHATSAPP', concedido: v.consContato === true, origem }
          ]
        });
      }
      message.success(paciente ? 'Paciente atualizado.' : 'Paciente cadastrado.');
      aoSalvar();
    } catch (e) {
      if (e instanceof ApiErro && e.codigo === 'CPF_DUPLICADO') {
        form.setFields([{ name: 'cpf', errors: [e.message] }]);
      } else if (e instanceof ApiErro && e.erros.length > 0) {
        const paraCampos: { name: keyof ValoresFormulario; errors: string[] }[] = [];
        const outros: string[] = [];
        for (const x of e.erros) {
          const nome = campoDoFormulario(x.campo);
          if (nome) paraCampos.push({ name: nome, errors: [x.mensagem] });
          else if (x.campo === 'responsavelLegalId') outros.push('Paciente menor de 18 anos exige responsável legal.');
          else outros.push(x.mensagem);
        }
        if (paraCampos.length > 0) form.setFields(paraCampos);
        if (outros.length > 0) setAvisos(outros);
      } else {
        message.error(e instanceof ApiErro ? e.message : 'Erro inesperado.');
      }
    } finally {
      setSalvando(false);
    }
  }

  return (
    <Modal
      open={aberto}
      title={paciente ? 'Editar paciente' : 'Novo paciente'}
      okText="Salvar"
      cancelText="Cancelar"
      confirmLoading={salvando}
      onOk={() => void enviar()}
      onCancel={fechar}
      maskClosable={false}
      destroyOnClose
      width={900}
      styles={{ body: { maxHeight: '70vh', overflowY: 'auto', paddingRight: 8 } }}
    >
      <Form<ValoresFormulario>
        form={form}
        layout="vertical"
        requiredMark
        preserve={false}
        initialValues={
          paciente
            ? {
                nome: paciente.nome,
                dataNascimento: paciente.dataNascimento.slice(0, 10),
                estrangeiro: paciente.estrangeiro,
                cpf: formatarCpf(paciente.cpf),
                tipoDocumentoEstrangeiro: paciente.tipoDocumentoEstrangeiro ?? '',
                numeroDocumentoEstrangeiro: paciente.numeroDocumentoEstrangeiro ?? '',
                rg: paciente.rg ?? '',
                escolaridade: paciente.escolaridade ?? '',
                telefone: formatarTelefone(paciente.telefone),
                email: paciente.email ?? '',
                endereco: paciente.endereco,
                numero: paciente.numero,
                complemento: paciente.complemento ?? undefined,
                cidade: paciente.cidade,
                uf: paciente.uf,
                usaConvenio: !!paciente.convenio,
                convenioId: paciente.convenio?.convenio.id,
                numeroCarteirinha: paciente.convenio?.numeroCarteirinha ?? '',
                validadeCarteirinha: paciente.convenio?.dataValidadeCarteirinha.slice(0, 10)
              }
            : { estrangeiro: false, usaConvenio: false, cpf: formatarCpf(cpfInicial) }
        }
      >
        {avisos.length > 0 && (
          <Alert
            type="error"
            showIcon
            style={{ marginBottom: 12 }}
            message="Confira o cadastro"
            description={
              <ul style={{ margin: 0, paddingLeft: 18 }}>
                {avisos.map((t, i) => (
                  <li key={i}>{t}</li>
                ))}
              </ul>
            }
          />
        )}

        <Divider orientation="left" plain>
          Dados pessoais
        </Divider>
        <Row gutter={16}>
          <Col xs={24} md={12}>
            <Form.Item name="nome" label="Nome completo" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={150} autoFocus />
            </Form.Item>
          </Col>
          <Col xs={24} md={6}>
            <Form.Item name="dataNascimento" label="Data de nascimento" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Input type="date" max={hojeIso()} />
            </Form.Item>
          </Col>
          <Col xs={24} md={6}>
            <Form.Item name="estrangeiro" label="Paciente estrangeiro" valuePropName="checked">
              <Switch checkedChildren="Sim" unCheckedChildren="Não" />
            </Form.Item>
          </Col>
        </Row>

        {(menor || responsavel) && (
          <>
            <Divider orientation="left" plain>
              {menor ? 'Responsável legal (obrigatório para menor de 18 anos)' : 'Responsável legal vinculado'}
            </Divider>
            {responsavel ? (
              <Alert
                type="info"
                showIcon
                style={{ marginBottom: 12 }}
                message={responsavel.nome}
                description={
                  <>
                    CPF {formatarCpf(responsavel.cpf)} · {formatarTelefone(responsavel.telefone)} · {responsavel.email}
                    {!menor && <div>O paciente é maior de idade: o vínculo pode ser removido. Vale ao salvar.</div>}
                    <div style={{ marginTop: 8 }}>
                      <Space>
                        <Button size="small" onClick={() => void editarResponsavel()}>
                          Editar dados do responsável
                        </Button>
                        <Button size="small" onClick={() => setResponsavel(null)}>
                          {menor ? 'Trocar responsável' : 'Remover vínculo'}
                        </Button>
                      </Space>
                    </div>
                  </>
                }
              />
            ) : (
              <Form.Item
                label="CPF do responsável legal"
                validateStatus={erroCpfResponsavel ? 'error' : undefined}
                help={erroCpfResponsavel ?? 'Informe o CPF para buscar. Se o responsável ainda não existir, o cadastro dele abre em seguida.'}
              >
                <Space.Compact style={{ width: '100%', maxWidth: 420 }}>
                  <Input
                    placeholder="000.000.000-00"
                    inputMode="numeric"
                    value={cpfResponsavel}
                    onChange={(e) => setCpfResponsavel(formatarCpf(e.target.value))}
                    onPressEnter={(e) => {
                      e.preventDefault();
                      void buscarResponsavel();
                    }}
                  />
                  <Button type="primary" loading={buscandoResponsavel} onClick={() => void buscarResponsavel()}>
                    Buscar
                  </Button>
                </Space.Compact>
              </Form.Item>
            )}
          </>
        )}

        <Row gutter={16}>
          <Col xs={24} md={8}>
            <Form.Item
              name="cpf"
              label="CPF"
              normalize={(v: string) => formatarCpf(v)}
              rules={[
                { required: !estrangeiro, message: OBRIGATORIO },
                { validator: (_, v: string) => (!v || cpfValido(v) ? Promise.resolve() : Promise.reject(new Error('CPF inválido.'))) }
              ]}
              extra={estrangeiro ? 'Opcional para estrangeiro com documento.' : undefined}
            >
              <Input placeholder="000.000.000-00" inputMode="numeric" />
            </Form.Item>
          </Col>
          <Col xs={24} md={8}>
            <Form.Item name="rg" label="RG">
              <Input maxLength={20} />
            </Form.Item>
          </Col>
          <Col xs={24} md={8}>
            <Form.Item name="escolaridade" label="Escolaridade">
              <Input maxLength={40} />
            </Form.Item>
          </Col>
        </Row>

        {estrangeiro && (
          <Row gutter={16}>
            <Col xs={24} md={12}>
              <Form.Item
                name="tipoDocumentoEstrangeiro"
                label="Tipo do documento"
                rules={[{ required: somenteDigitos(cpfDigitado) === '', message: OBRIGATORIO }]}
              >
                <Input maxLength={30} placeholder="Passaporte, RNE..." />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                name="numeroDocumentoEstrangeiro"
                label="Número do documento"
                rules={[{ required: somenteDigitos(cpfDigitado) === '', message: OBRIGATORIO }]}
              >
                <Input maxLength={30} />
              </Form.Item>
            </Col>
          </Row>
        )}

        <Row gutter={16}>
          <Col xs={24} md={10}>
            <Form.Item
              name="telefone"
              label={menor ? 'Celular (opcional para menor)' : 'Celular (com DDD)'}
              normalize={(v: string) => formatarCelular(v)}
              rules={[
                { required: !menor, message: OBRIGATORIO },
                {
                  validator: (_, v: string) =>
                    !v || celularValido(v) ? Promise.resolve() : Promise.reject(new Error('Informe o celular com DDD: (00) 00000-0000.'))
                }
              ]}
            >
              <Input placeholder="(00) 00000-0000" inputMode="tel" maxLength={15} />
            </Form.Item>
          </Col>
          <Col xs={24} md={14}>
            <Form.Item
              name="email"
              label={menor ? 'E-mail (opcional para menor)' : 'E-mail'}
              rules={[
                { required: !menor, message: OBRIGATORIO },
                { type: 'email', message: 'E-mail inválido.' }
              ]}
            >
              <Input maxLength={150} />
            </Form.Item>
          </Col>
        </Row>
        <Row gutter={16}>
          <Col xs={24} md={12}>
            <Form.Item name="endereco" label="Endereço" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={250} />
            </Form.Item>
          </Col>
          <Col xs={24} md={6}>
            <Form.Item name="numero" label="Número" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <CampoNumeroEndereco />
            </Form.Item>
          </Col>
          <Col xs={24} md={6}>
            <Form.Item name="complemento" label="Complemento">
              <Input maxLength={60} />
            </Form.Item>
          </Col>
          <Col xs={24} md={8}>
            <Form.Item name="cidade" label="Cidade" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
              <Input maxLength={80} />
            </Form.Item>
          </Col>
          <Col xs={24} md={4}>
            <Form.Item name="uf" label="UF" rules={[{ required: true, message: OBRIGATORIO }]}>
              <Select showSearch options={ufOpcoes} />
            </Form.Item>
          </Col>
        </Row>

        <Divider orientation="left" plain>
          Convênio
        </Divider>
        <Form.Item name="usaConvenio" label="Atendido por convênio" valuePropName="checked">
          <Switch checkedChildren="Sim" unCheckedChildren="Não" />
        </Form.Item>
        {usaConvenio && (
          <Row gutter={16}>
            <Col xs={24} md={10}>
              <Form.Item name="convenioId" label="Convênio" rules={[{ required: true, message: OBRIGATORIO }]}>
                <Select showSearch optionFilterProp="label" options={opcoesConvenios} placeholder="Selecione" />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="numeroCarteirinha" label="Número da carteirinha" rules={[{ required: true, whitespace: true, message: OBRIGATORIO }]}>
                <Input maxLength={30} />
              </Form.Item>
            </Col>
            <Col xs={24} md={6}>
              <Form.Item name="validadeCarteirinha" label="Validade" rules={[{ required: true, message: OBRIGATORIO }]}>
                <Input type="date" />
              </Form.Item>
            </Col>
          </Row>
        )}

        <Divider orientation="left" plain>
          Profissional responsável
        </Divider>
        {paciente ? (
          <Descriptions size="small" column={1}>
            <Descriptions.Item label="Profissional">
              {profissionalAtual?.nome ?? paciente.profissionalResponsavel.nome}
              <Space style={{ marginLeft: 12 }}>
                {podeEditar && (
                  <Button size="small" onClick={() => setModalTroca(true)}>
                    Trocar profissional
                  </Button>
                )}
                <Button size="small" onClick={() => setModalHistorico(true)}>
                  Ver histórico
                </Button>
              </Space>
            </Descriptions.Item>
          </Descriptions>
        ) : (
          <Form.Item name="profissionalId" label="Profissional responsável" rules={[{ required: true, message: OBRIGATORIO }]}>
            <Select showSearch optionFilterProp="label" options={opcoesProfissionais} placeholder="Selecione" />
          </Form.Item>
        )}

        <Divider orientation="left" plain>
          Consentimentos (LGPD)
        </Divider>
        {paciente && menor && (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 12 }}
            message="Paciente menor de idade: o consentimento é dado pelo responsável legal."
          />
        )}
        {paciente ? (
          <Descriptions size="small" column={1}>
            {consentimentos.map((c) => (
              <Descriptions.Item key={c.finalidade} label={c.finalidade === 'TRATAMENTO_DADOS' ? 'Tratamento de dados' : 'Contato por SMS e WhatsApp'}>
                {c.concedido ? <Tag color="green">Concedido</Tag> : <Tag>Não concedido</Tag>}
                <span style={{ marginLeft: 8, color: '#8c8c8c' }}>registrado em {formatarDataIso(c.dataRegistro)}</span>
              </Descriptions.Item>
            ))}
            <Descriptions.Item label=" ">
              <Button size="small" onClick={() => setModalConsentimentos(true)}>
                {podeEditar ? 'Registrar mudança / ver histórico' : 'Ver histórico'}
              </Button>
            </Descriptions.Item>
          </Descriptions>
        ) : (
          <>
            <Form.Item
              name="consTratamento"
              valuePropName="checked"
              rules={[
                {
                  validator: (_, v: boolean) =>
                    v ? Promise.resolve() : Promise.reject(new Error('O consentimento de tratamento de dados é obrigatório.'))
                }
              ]}
            >
              <Checkbox>
                {menor
                  ? 'O responsável legal autoriza o tratamento dos dados pessoais do menor, conforme a LGPD (obrigatório).'
                  : 'O paciente autoriza o tratamento dos seus dados pessoais (obrigatório).'}
              </Checkbox>
            </Form.Item>
            <Form.Item name="consContato" valuePropName="checked">
              <Checkbox>
                {menor ? 'O responsável legal autoriza o contato com o menor por SMS e WhatsApp.' : 'O paciente autoriza o contato por SMS e WhatsApp.'}
              </Checkbox>
            </Form.Item>
            <Row gutter={16}>
              <Col xs={24} md={10}>
                <Form.Item name="consOrigem" label="Como o consentimento foi dado" rules={[{ required: true, message: OBRIGATORIO }]}>
                  <Select
                    placeholder="Selecione"
                    options={[
                      { value: 'PRESENCIAL', label: 'Presencialmente' },
                      { value: 'TELEFONE', label: 'Por telefone' }
                    ]}
                  />
                </Form.Item>
              </Col>
            </Row>
          </>
        )}
      </Form>

      <ResponsavelModal
        aberto={modalResponsavel !== null}
        responsavel={modalResponsavel?.responsavel ?? null}
        cpfInicial={modalResponsavel?.cpf}
        sugestao={modalResponsavel?.sugestao}
        aoFechar={() => setModalResponsavel(null)}
        aoSalvar={(r) => {
          setResponsavel(r);
          setCpfResponsavel('');
          setModalResponsavel(null);
          if (paciente) setAlterouRelacionados(true);
        }}
      />
      {paciente && (
        <>
          <TrocaProfissionalModal
            aberto={modalTroca}
            pacienteId={paciente.id}
            profissionalAtualId={profissionalAtual?.id ?? paciente.profissionalResponsavel.id}
            aoFechar={() => setModalTroca(false)}
            aoTrocar={(p) => {
              setProfissionalAtual(p.profissionalResponsavel);
              setAlterouRelacionados(true);
              setModalTroca(false);
            }}
          />
          <HistoricoProfissionaisModal aberto={modalHistorico} pacienteId={paciente.id} aoFechar={() => setModalHistorico(false)} />
          <ConsentimentosModal
            aberto={modalConsentimentos}
            pacienteId={paciente.id}
            podeRegistrar={podeEditar}
            menor={menor}
            aoFechar={() => setModalConsentimentos(false)}
            aoMudar={(historico) => {
              setConsentimentos(estadoAtual(historico));
              setAlterouRelacionados(true);
            }}
          />
        </>
      )}
    </Modal>
  );
}
