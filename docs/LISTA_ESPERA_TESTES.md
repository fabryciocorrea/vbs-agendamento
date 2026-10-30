# Lista de espera – roteiro de teste

Antes: extraia o zip, reinicie o `dotnet run` e adicione a rota e o item de menu (veja o fim deste arquivo). Não há script novo no banco.

## Rota e menu (arquivos seus)

`App.tsx`, dentro da rota de layout:
```tsx
import ListaEsperaPagina from './paginas/listaespera/ListaEsperaPagina';
<Route path="lista-espera" element={<RotaProtegida tela="listaespera.cadastro"><ListaEsperaPagina /></RotaProtegida>} />
```
`LayoutPrincipal.tsx`, nos itens do menu (ícone `ClockCircleOutlined` ou outro):
```tsx
{ key: '/lista-espera', label: 'Lista de espera', icon: <ClockCircleOutlined />, tela: 'listaespera.cadastro' },
```
No perfil que vai usar (por exemplo, Recepção): marque **Lista de espera** (Visualizar, Incluir, Editar).

## Testes

1. **Incluir:** Lista de espera > Incluir. Escolha paciente e profissional (ou só especialidade). Salva com situação "Aguardando".
2. **Sem profissional e sem especialidade:** deve avisar "Informe o profissional, a especialidade ou ambos".
3. **Duplicado:** repita o mesmo paciente e a mesma fila: erro "já está na lista de espera".
4. **Horário liberado:** cancele uma consulta futura do mesmo profissional (ou da especialidade). O item passa a "Horário liberado", aparece o aviso laranja no topo e a recepção recebe a notificação.
5. **Agendar:** no item avisado, botão de calendário. A Nova consulta abre com paciente, profissional e especialidade preenchidos. Ao agendar, o item vira "Atendido".
6. **Voltar para a fila:** item "Horário liberado" > seta de desfazer. Volta a "Aguardando".
7. **Atendido / Cancelar:** botões do item. Encerrado não tem mais botões.
8. **Filtros:** situação, profissional, especialidade e busca por nome.
9. **Médico:** usuário médico só vê pacientes dele.
