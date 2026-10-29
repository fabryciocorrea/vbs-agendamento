# Agenda e Consultas - Parte 1: roteiro de instalação e testes

## 1. Instalar
1. Extraia o zip **na pasta `VBS_Agendamento_Servidor`**, sobrescrevendo os arquivos.
2. Não há script SQL novo nesta parte.
3. Pare o `dotnet run` e rode de novo (`dotnet run` em `src\VBS.Agendamento.Api`). O `npm run dev` pode ficar aberto.

## 2. Ajustes manuais (seus arquivos de menu e rotas não foram sobrescritos)

### `web/src/App.tsx`
Junto dos outros `import`:
```tsx
import ParametrosAgendaPagina from './paginas/agenda/ParametrosAgendaPagina';
import AgendaProfissionalPagina from './paginas/agenda/AgendaProfissionalPagina';
```
Dentro de `<Routes>`, junto das outras rotas:
```tsx
<Route
  path="agenda"
  element={
    <RotaProtegida tela="agenda.profissional">
      <AgendaProfissionalPagina />
    </RotaProtegida>
  }
/>
<Route
  path="parametros-agenda"
  element={
    <RotaProtegida tela="agenda.parametros">
      <ParametrosAgendaPagina />
    </RotaProtegida>
  }
/>
```

### `web/src/layout/LayoutPrincipal.tsx`
No `import` dos ícones, acrescente `CalendarOutlined` e `SettingOutlined`. No vetor `ITENS`, na posição que preferir:
```tsx
{ key: '/agenda', label: 'Agenda', icon: <CalendarOutlined />, tela: 'agenda.profissional' },
{ key: '/parametros-agenda', label: 'Parâmetros da agenda', icon: <SettingOutlined />, tela: 'agenda.parametros' },
```
Obs.: o menu usa `pathname.startsWith(key)`; `/agenda` e `/parametros-agenda` não se confundem.

## 3. Pré-requisitos dos testes (use dados fictícios)
- Um profissional ativo com **horários de atendimento** na unidade (tela Profissionais, grade de horários) e, de preferência, um intervalo.
- Uma sala ativa na unidade.
- Um paciente ativo (e um paciente com convênio e carteirinha válida, para o teste 7).
- O seu usuário precisa das liberações: `agenda.profissional` (visualizar), `consulta.agendamento` (visualizar e incluir), `agenda.parametros` (visualizar e editar), `apoio.listas` (visualizar). O Administrador tem tudo.

## 4. Testes

**Parâmetros da agenda**
1. Abra *Parâmetros da agenda*. Devem aparecer duração 45 e antecedência 48.
2. Troque a duração para 30, deixe o limite de encaixes em 1 e salve. Mensagem "Parâmetros gravados".
3. Informe só o início do recesso e salve: erro no campo do fim. Informe fim anterior ao início: erro. Deixe os dois vazios e salve: ok.
4. Em *Feriados nacionais* escolha o ano, inclua um feriado novo (ex.: 2026-11-20) e tente repetir a data: erro "Já existe um feriado cadastrado nesta data". Remova o feriado incluído.

**Agenda**
5. Abra *Agenda*, escolha o profissional. Na visão semana aparecem os dias com "Atende hh:mm-hh:mm (unidade)", intervalos e feriados em vermelho. Alterne Dia/Semana e use Anterior/Hoje/Próximo.

**Agendar**
6. *Nova consulta*, particular, presencial: escolha paciente, profissional (unidade e especialidade carregam sozinhas se houver uma só), sala, valor, forma de pagamento, data e um horário. Os horários seguem a duração do parâmetro (30 min) e não mostram intervalos, feriados nem horários passados. Agende: a consulta aparece na agenda; clique nela para ver os detalhes.
7. Convênio: escolha o paciente com convênio e tipo *Convênio*. Se o convênio não tem credenciamento aprovado na unidade, aparece a janela de liberação (só o Administrador consegue; outro perfil recebe a recusa). Com a justificativa (10+ caracteres) a consulta é gravada e os detalhes mostram "Credenciamento liberado". *O cadastro de credenciamento de convênios é a próxima fatia.*
8. Tente agendar o mesmo horário de novo (abra duas telas ou use encaixe): "Horário indisponível".
9. Online: o aviso informa que o link não é gerado ainda; a consulta é gravada.
10. Encaixe: marque *Encaixe* e informe um horário fora do expediente: grava. Com limite de encaixes 1, o segundo encaixe do mesmo dia é recusado ("limite de encaixes").
11. Data em feriado/recesso ou horário no intervalo (com encaixe, intervalo é permitido, feriado não): recusado.
12. Paciente com carteirinha vencida + convênio: "Carteirinha vencida em dd/mm/aaaa. Atualize o cadastro do paciente."
13. Profissional diferente do responsável do paciente: a consulta é gravada e mostra o aviso informativo.
14. Entre com um usuário de perfil médico: só a própria agenda abre e o valor não aparece.

## 5. Decisões desta parte (diga se quer mudar)
- **Encaixe**: exige a liberação *Editar* de `consulta.agendamento` (ou ser Administrador).
- **Liberação de credenciamento**: somente o Administrador.
- **Consulta online**: sem link automático (aviso `LINK_ONLINE_NAO_GERADO`).
- No convênio a forma de pagamento é localizada pelo servidor (a tela não envia).
- As listas de apoio de unidades e especialidades passam a aceitar `profissionalId`.
