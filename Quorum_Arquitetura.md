# Quorum — Documento de Arquitetura (v5)

> QA · AI Test Automation. Reescrita profissional do T2M Security Manager em
> C# .NET 8 + Avalonia UI, com orquestração multi-IA nativa.
>
> Status: proposta para revisão. Nada codado ainda — este é o mapa antes da obra.

---

## 1. Objetivo da v5

Recriar o app sobre uma fundação moderna, corrigindo os problemas estruturais da
v4.2 e adicionando a capacidade que nenhum concorrente de QA tem: **múltiplas IAs
trabalhando de forma coordenada**, com um roteador que escolhe o provedor e o
modelo certos para cada tarefa.

Três metas que guiam cada decisão abaixo:

1. **Multiplataforma real** — Windows, Linux e macOS nativos (Avalonia).
2. **Um único processo** — sem a costura frágil C++ ↔ Python por stdin/stdout.
3. **Profissional de verdade** — visual limpo, arquitetura testável, pronto para
   uso real e não só demonstração.

---

## 2. Por que trocar a fundação

| Problema na v4.2 | Origem | Como a v5 resolve |
|---|---|---|
| Só roda no Windows | C++/CLI + WinForms | Avalonia (Win/Linux/macOS) |
| Layout por pixel, cada botão novo é um Tetris | WinForms posicional | XAML com layout responsivo |
| Visual "2010" | WinForms | Tema Fluent moderno, claro/escuro |
| Bugs de comunicação (marcadores CHAT_MSG, "python não encontrado") | costura por texto stdin/stdout | tudo em processo, chamadas de método |
| Instalador pesado (Python + Node + libs) | dependências externas | .NET runtime; Node só se usar MCP via npx |
| Modelo aposentado derruba o app | IDs fixos no código | registro dinâmico + fallback |

O código Python em si (a lógica dos loops de agente) é bom — ele vira a
**especificação** do que reescrever em C#, não código a portar linha a linha.

---

## 3. Stack

- **Linguagem:** C# / .NET 8 (LTS)
- **UI:** Avalonia UI 11 + tema Fluent; padrão **MVVM** (CommunityToolkit.Mvvm)
- **MCP:** `ModelContextProtocol` 1.4.x (oficial, Microsoft + Anthropic) —
  `StdioClientTransport` sobe Playwright / DBHub / MongoDB via npx, igual hoje
- **IA:** SDKs nativos —
  - `Anthropic.SDK` (Claude)
  - `OpenAI` (GPT)
  - Gemini via `Microsoft.Extensions.AI` ou REST direto
  - camada comum: `Microsoft.Extensions.AI` para abstrair tool-use entre provedores
- **Banco de teste local:** `Microsoft.Data.Sqlite`; Oracle via `Oracle.ManagedDataAccess.Core`
- **Segurança:** DPAPI no Windows; no Linux/macOS, libsecret/Keychain (abstração própria)
- **Instalador:** por plataforma — MSIX/Inno (Win), AppImage/deb (Linux), dmg (macOS)

---

## 4. Estrutura em camadas (solução .NET)

```
Quorum.sln
├── Quorum.App          (Avalonia — Views + ViewModels, MVVM)
├── Quorum.Core         (domínio: contratos, modelos, roteador — SEM UI, SEM IO)
├── Quorum.Providers     (Claude / OpenAI / Gemini — implementam IAiProvider)
├── Quorum.Mcp          (cliente MCP: sobe servidores, expõe ferramentas)
├── Quorum.Agents       (loops agentic: Tela, API, Banco — orquestram IA + MCP)
├── Quorum.Security     (cofre de credenciais multiplataforma)
└── Quorum.Tests        (xUnit — testa Core e Agents sem gastar créditos, com mocks)
```

Regra de dependência: `App → Agents → {Providers, Mcp, Core}`; `Core` não depende
de ninguém. Isso é o que torna o roteador e os agentes **testáveis sem chamar IA
de verdade** (mocka-se `IAiProvider`) — economia direta de créditos na Fase 2.

---

## 5. O coração: orquestração multi-IA

### 5.1 Registro de modelos

Um `ModelRegistry` mantém, para cada modelo, metadados que o roteador usa:

```
ModelInfo {
  Provider        (Claude | OpenAI | Gemini)
  Id              (ex.: claude-haiku-4-5-20251001)
  SupportsTools   (bool)
  ContextWindow   (tokens)
  CostIn, CostOut ($/milhão de tokens)
  Tier            (Rápido | Equilibrado | Potente)
}
```

A lista é **carregada do provedor** (como a v4.2 já faz com o botão ⟳ Buscar) e
cacheada. Modelo aposentado some da lista sozinho — o bug que já te mordeu duas
vezes deixa de existir por construção.

### 5.2 Roteador por regras (camada 1 — começamos por aqui)

Uma função pura em `Quorum.Core`:

```
IAiProvider Rotear(Tarefa t, Preferencias p):
  - chat casual, sem ferramentas      → Tier Rápido   (Haiku / Flash / mini)
  - automação com tool-use            → Tier Equilibrado, SupportsTools=true
  - análise de relatório/log longo    → maior ContextWindow disponível
  - respeita override do usuário (se ele fixou um modelo, usa esse)
  - respeita orçamento (se p.ModoEconomico, nunca sobe de tier sem necessidade)
```

Por ser função pura, cada regra vira um teste unitário. Nada de IA decidindo
ainda — barato, previsível, fácil de depurar.

### 5.3 Fallback e resiliência

O que você aprendeu no Gemini (ResourceExhausted, MALFORMED) vira política de
primeira classe:

```
- provedor falha / estoura cota → tenta o próximo do mesmo tier (outra IA)
- retry com backoff em erro transitório
- se todos falham, mensagem clara com a causa real (não "token não encontrado")
```

Um relatório pode nascer de **duas IAs**: Haiku roda a automação, e se você pedir
"revise criticamente este resultado", um modelo potente de outro provedor faz a
segunda leitura. Esse é o "quorum" do nome.

### 5.4 Camada 2 (futuro, só se valer a pena)

Um modelo barato classifica a tarefa antes de rotear ("isto é chat, scan, ou
automação de banco?"). **Não** entra no MVP — gasta token e é difícil de depurar.
Fica documentado como evolução.

---

## 6. Os agentes (portados do Python)

Cada modo vira uma classe em `Quorum.Agents` implementando `IAgent`:

| Agente | Servidor/driver | Equivalente v4.2 |
|---|---|---|
| `ScreenAgent`   | Playwright MCP (npx) | executar() |
| `ApiAgent`      | HttpClient nativo    | executar_api() |
| `SqlAgent`      | DBHub MCP (npx)      | executar_banco() |
| `OracleAgent`   | Oracle.ManagedDataAccess | executar_oracle() |
| `MongoAgent`    | MongoDB MCP (npx)    | executar_mongo() |
| `DomScanAgent`  | HttpClient + AngleSharp | extrair_contexto_dom() |
| `TokenAgent`    | Playwright/Selenium  | get_token.py |

O loop agentic (IA pede ferramenta → executa → devolve resultado → repete até
concluir) fica **um só**, genérico, em `Quorum.Agents`. Hoje ele está triplicado
no Python (um por provedor); em C# com `Microsoft.Extensions.AI` o tool-use é
uniforme, então some a duplicação — e some junto o bug do "Gemini sem system
prompt", porque a montagem da chamada passa a ser única.

Todas as correções da v4.2 já nascem embutidas aqui (guardrails de custo, limite
de histórico, limite de passos, somente-leitura no banco por padrão).

---

## 7. Interface (Avalonia, MVVM)

Telas principais:

- **Janela principal** — barra lateral de navegação (não mais botões soltos):
  Chat · Automação · Scripts · Relatórios · Configurações
- **Chat** — conversa + seletor de modo (Chat / Scan / Automação) como segmented
  control moderno; indicador da IA ativa; status "pensando" não-bloqueante
- **Automação** — formulários de API / Banco / Tela como painéis limpos, com a
  validação visual que a v4.2 já tem (borda vermelha + mensagem)
- **Configurações** — pastas, modelo, limites, tema; mais a nova aba de
  **roteamento** (economia vs capacidade, override de modelo por modo)
- **Tema claro/escuro** — nativo do Avalonia, sem o AplicarTemaRecursivo manual

Async de verdade (`async/await`) em vez de BackgroundWorker — a UI nunca congela
e o código fica linear de ler.

---

## 8. Segurança

- Credenciais nunca em texto: `ISecretVault` com implementação por SO
  (DPAPI / libsecret / Keychain)
- Banco somente-leitura por padrão (mantido)
- Chave e prompt nunca em argv (mantido — mas agora é chamada de método, some o
  risco de vazar na lista de processos por natureza)
- Nada sai da máquina exceto para o provedor de IA cuja chave o usuário forneceu

---

## 9. Roadmap de implementação (uma etapa por commit, seu padrão)

Fase A — Fundação
1. Solução .NET + estrutura de projetos + CI (build nos 3 SOs)
2. `Quorum.Core`: modelos, `ModelRegistry`, roteador + testes
3. `Quorum.Providers`: Claude, depois OpenAI, depois Gemini (com testes mockados)

Fase B — Agentes (sem gastar crédito: testes com mock)
4. Loop agentic genérico + `ApiAgent` (o mais simples, sem MCP externo)
5. `DomScanAgent` e `TokenAgent`
6. `Quorum.Mcp` + `ScreenAgent` (Playwright)
7. `SqlAgent`, `OracleAgent`, `MongoAgent`

Fase C — Interface
8. Shell Avalonia + navegação + tema
9. Chat + modos
10. Formulários de automação + Configurações + aba de roteamento

Fase D — Empacotar e validar
11. Cofre de credenciais por SO
12. Instaladores (Win/Linux/macOS)
13. **Validação com créditos** (só aqui entra dinheiro — fim do projeto, como combinado)

---

## 10. Decisões em aberto (para você bater o martelo)

1. **Nome da solução/repo:** `Quorum` puro, ou `QuorumQA` / `Quorum.Studio`?
2. **Namespace raiz:** sugiro `Quorum.*` (ver estrutura acima) — confirma?
3. **Gemini:** SDK do Google para .NET é mais imaturo que os outros; tudo bem
   começar Gemini via REST direto (mais controle) e migrar depois?
4. **Licença:** manter GPL-3.0 (como a v4.2) ou revisar agora que é projeto novo?
5. **Compatibilidade de dados:** importar as chaves/config da v4.2 do usuário na
   primeira execução, ou começar limpo?
```
```
