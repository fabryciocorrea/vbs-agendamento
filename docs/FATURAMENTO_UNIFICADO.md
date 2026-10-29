# Faturamento unificado (uma tela, três abas)

A tela **Faturamento** concentra todo o processo:
- **Resumo financeiro:** totais das consultas concluídas (particular recebido/sem pagamento; convênio autorizado/pendente/negado) e exportação CSV.
- **Convênio (guias):** lista das consultas por convênio, registro da guia (Autorizada com número, Negada com motivo, Pendente).
- **Particular (pagamentos):** acompanhamento das consultas particulares e da situação do pagamento (confirmado pela recepção depois do check-in).

## Instalação
1. Extrair o zip sobrescrevendo os arquivos e reiniciar `dotnet run` (cadastra a tela "Faturamento (resumo, convênio e particular)").
2. `App.tsx`: remover a rota `faturamento-guias` e o import de `FaturamentoGuiasPagina` (agora é uma aba). Manter só a rota `faturamento`.
3. `LayoutPrincipal.tsx`: remover o item "Faturamento de guias". Manter "Faturamento" (tela `faturamento.financeiro`).
4. SSMS: executar `banco/25_UNIFICAR_TELA_FATURAMENTO_V1_3.sql` (depois de reiniciar o servidor). Copia as liberações da tela antiga para a nova e remove a antiga do catálogo.
5. Perfis de acesso: o perfil do faturamento precisa de Visualizar, Editar (registrar guia) e Exportar na tela "Faturamento (resumo, convênio e particular)".

## Roteiro
1. Abrir Faturamento: aparecem 3 abas.
2. Aba Convênio: registrar uma guia como Autorizada; voltar ao Resumo e conferir os valores.
3. Aba Particular: filtrar Pendentes/Confirmados e conferir o pagamento confirmado na recepção.
4. Perfil sem a ação Editar: não aparece o botão de registrar guia.
