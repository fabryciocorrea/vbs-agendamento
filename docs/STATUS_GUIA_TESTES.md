# Status da consulta e guia do convênio — instalação e testes

## 1. Instalação
1. Extrair o zip em `VBS_Agendamento_Servidor` sobrescrevendo os arquivos.
2. No SSMS, executar `banco/23_ATALHO_STATUS_E_GUIA_CONVENIO_V1_3.sql` (banco VBSAGENDA).
3. Reiniciar o servidor (`dotnet run`).

## 2. App.tsx (manual)
```tsx
import FaturamentoGuiasPagina from './paginas/faturamento/FaturamentoGuiasPagina';
// dentro da rota de layout:
<Route path="faturamento-guias" element={<RotaProtegida tela="faturamento.guias"><FaturamentoGuiasPagina /></RotaProtegida>} />
```

## 3. LayoutPrincipal.tsx (manual)
```tsx
{ key: '/faturamento-guias', label: 'Faturamento de guias', icon: <FileDoneOutlined />, tela: 'faturamento.guias' },
```
(importar `FileDoneOutlined` de `@ant-design/icons`)

## 4. Permissão
Em Perfis de acesso, liberar a tela "Faturamento de guias de convênio" (visualizar/editar) para o perfil do faturamento. O Administrador já tem.

## 5. Roteiro de testes
1. Consulta passada, status Agendada/Confirmada: abrir na agenda → botões Concluído, Não concluído, Remarcado, Cancelado.
2. Concluído → confirma → status Concluída.
3. Não concluído → motivo obrigatório → status Falta.
4. Remarcado → nova data/horário + motivo → cria nova consulta, a antiga fica Remarcada, o detalhe abre a nova.
5. Cancelado → paciente/clínica + motivo.
6. Consulta futura: Concluído e Não concluído não aparecem (erro 409 se forçado).
7. Convênio: detalhe mostra "Guia do convênio: Aguardando o faturamento"; check-in não é bloqueado.
8. Faturamento de guias: filtro padrão Pendente → Registrar → Autorizada (número obrigatório) → detalhe mostra "Autorizada nº ...". Negada exige observação.
9. Remarcar consulta de convênio com guia autorizada: a guia vai para a nova consulta.

## Observações
- Concluir consulta particular pelo atalho não exige confirmação de pagamento.
- A remarcação não tem tela de liberação administrativa de credenciamento.
