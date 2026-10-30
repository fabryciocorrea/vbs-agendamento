import { Navigate, Route, Routes } from "react-router-dom";
import Login from "./paginas/Login";
import Inicio from "./paginas/Inicio";
import RotaProtegida from "./auth/RotaProtegida";
import LayoutPrincipal from "./layout/LayoutPrincipal";
import UnidadesPagina from "./paginas/unidades/UnidadesPagina";
import SalasPagina from "./paginas/salas/SalasPagina";
import EspecialidadesPagina from "./paginas/especialidades/EspecialidadesPagina";
import ConveniosPagina from "./paginas/convenios/ConveniosPagina";
import FormasPagamentoPagina from "./paginas/formaspagamento/FormasPagamentoPagina";
import ProfissionaisPagina from "./paginas/profissionais/ProfissionaisPagina";
import PacientesPagina from "./paginas/pacientes/PacientesPagina";
import PerfisPagina from "./paginas/perfis/PerfisPagina";
import UsuariosPagina from "./paginas/usuarios/UsuariosPagina";
import ColaboradoresPagina from "./paginas/colaboradores/ColaboradoresPagina";
import ParametrosAgendaPagina from "./paginas/agenda/ParametrosAgendaPagina";
import AgendaProfissionalPagina from "./paginas/agenda/AgendaProfissionalPagina";
import CredenciamentosPagina from "./paginas/credenciamentos/CredenciamentosPagina";
import FaturamentoFinanceiroPagina from "./paginas/faturamento/FaturamentoFinanceiroPagina";
import ListaEsperaPagina from "./paginas/listaespera/ListaEsperaPagina";

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route
        element={
          <RotaProtegida>
            <LayoutPrincipal />
          </RotaProtegida>
        }
      >
        <Route index element={<Inicio />} />
        <Route
          path="unidades"
          element={
            <RotaProtegida tela="unidade.cadastro">
              <UnidadesPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="salas"
          element={
            <RotaProtegida tela="sala.cadastro">
              <SalasPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="especialidades"
          element={
            <RotaProtegida tela="especialidade.cadastro">
              <EspecialidadesPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="convenios"
          element={
            <RotaProtegida tela="convenio.cadastro">
              <ConveniosPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="credenciamentos"
          element={
            <RotaProtegida tela="convenio.credenciamento">
              <CredenciamentosPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="faturamento"
          element={
            <RotaProtegida tela="faturamento.financeiro">
              <FaturamentoFinanceiroPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="formas-pagamento"
          element={
            <RotaProtegida tela="formapagamento.cadastro">
              <FormasPagamentoPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="profissionais"
          element={
            <RotaProtegida tela="profissional.cadastro">
              <ProfissionaisPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="pacientes"
          element={
            <RotaProtegida tela="paciente.cadastro">
              <PacientesPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="perfis"
          element={
            <RotaProtegida tela="perfil.cadastro">
              <PerfisPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="usuarios"
          element={
            <RotaProtegida tela="usuario.cadastro">
              <UsuariosPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="colaboradores"
          element={
            <RotaProtegida tela="colaborador.cadastro">
              <ColaboradoresPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="agenda"
          element={
            <RotaProtegida tela="agenda.profissional">
              <AgendaProfissionalPagina />
            </RotaProtegida>
          }
        />
        <Route
          path="lista-espera"
          element={
            <RotaProtegida tela="listaespera.cadastro">
              <ListaEsperaPagina />
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
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
