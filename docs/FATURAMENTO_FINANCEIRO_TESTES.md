# Faturamento (resumo financeiro) - instalação e testes

## Instalação
1. Extrair o zip em `VBS_Agendamento_Servidor` sobrescrevendo os arquivos. Não há script SQL.
2. Reiniciar `dotnet run` (a tela nova "Faturamento financeiro (resumo e exportação)" é cadastrada ao iniciar).
3. App.tsx (manual):
```tsx
import FaturamentoFinanceiroPagina from './paginas/faturamento/FaturamentoFinanceiroPagina';
<Route path="faturamento" element={<RotaProtegida tela="faturamento.financeiro"><FaturamentoFinanceiroPagina /></RotaProtegida>} />
```
4. LayoutPrincipal.tsx (manual):
```tsx
{ key: '/faturamento', label: 'Faturamento', icon: <DollarOutlined />, tela: 'faturamento.financeiro' },
```
(importar `DollarOutlined` de `@ant-design/icons`)
5. Em Perfis de acesso, liberar "Faturamento financeiro (resumo e exportação)" (Visualizar e Exportar) ao perfil do faturamento. O Administrador já tem.

## Regras
- Só consultas CONCLUÍDAS, pela data da consulta (Brasília), período de no máximo 1 ano, filtro opcional por unidade.
- Particular: recebido = pagamento confirmado na recepção; o restante aparece como "sem pagamento confirmado".
- Convênio: autorizada, pendente ou negada conforme a guia (tela Faturamento de guias).
- Exportar CSV exige a ação Exportar; abre no Excel (UTF-8, ponto e vírgula). Visualização e exportação ficam no log.

## Roteiro
1. Concluir uma consulta particular com pagamento confirmado e outra de convênio com guia pendente.
2. Abrir Faturamento: período do mês atual; conferir os cartões.
3. Autorizar a guia na tela de guias e conferir que o valor passa de "pendente" para "autorizada".
4. Exportar CSV e abrir no Excel.
5. Perfil sem a ação Exportar: o botão não aparece.
