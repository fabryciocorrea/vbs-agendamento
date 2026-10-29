# Credenciamento de convênios (RN35) - instalação e testes

## Instalação
1. Extraia o zip em `VBS_Agendamento_Servidor` (sobrescreve). Não há script SQL novo (a tabela vem do script 09).
2. Reinicie o `dotnet run`.
3. `App.tsx` - import e rota (dentro do layout):
```tsx
import CredenciamentosPagina from './paginas/credenciamentos/CredenciamentosPagina';
...
<Route
  path="credenciamentos"
  element={
    <RotaProtegida tela="convenio.credenciamento">
      <CredenciamentosPagina />
    </RotaProtegida>
  }
/>
```
4. `LayoutPrincipal.tsx` - item de menu (logo abaixo de Convênios), com um ícone já importado ou `SafetyCertificateOutlined`:
```tsx
{ key: '/credenciamentos', label: 'Credenciamentos', icon: <SafetyCertificateOutlined />, tela: 'convenio.credenciamento' },
```
5. Perfis que não são Administrador: marque a tela "Credenciamento de convênios" em Perfis de acesso.

## Testes
1. Credenciamentos > Incluir: convênio TESTE, unidade Filial, especialidades (várias), situação Aprovado, início hoje, sem fim. Salvar.
2. Repetir a mesma combinação: erro "Já existe credenciamento...".
3. Aprovado sem início: erro no campo. Fim anterior ao início: erro.
4. Agenda > Nova consulta > Convênio, nessa unidade e especialidade: agenda sem pedir liberação.
5. Editar o credenciamento para "Não credenciado" (ou fim ontem): nova consulta volta a pedir a liberação do Administrador.
6. Filtros e "Somente os que valem hoje".
