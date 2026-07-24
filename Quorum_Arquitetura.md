# Quorum — Arquitetura e Decisões

> Documento vivo. Registra **por que** o Quorum é como é: as decisões tomadas, o que
> foi descartado e as regras que nasceram de erros reais durante a construção.
>
> Última revisão: julho de 2026 — projeto funcionalmente completo, aguardando a
> validação com créditos (Fase D).

---

## 1. O problema

Ferramentas de automação de teste guiadas por IA em geral amarram você a um provedor.
Se aquele modelo estiver fora do ar, com cota esgotada ou for caro demais para a tarefa,
você não tem alternativa — e paga o modelo mais capaz mesmo quando um barato bastaria.

O Quorum trata **a escolha da IA como parte do produto**: qual provedor, qual modelo,
quanto custa, o que fazer quando falha e, opcionalmente, quem revisa o resultado.

## 2. Fundação técnica

| Camada | Escolha | Motivo |
|---|---|---|
| Linguagem | C# / .NET 8 (LTS) | um runtime, três sistemas operacionais |
| Interface | Avalonia 11 + Fluent, MVVM | XAML multiplataforma; layout responsivo |
| MCP | `ModelContextProtocol` 1.4.x (oficial) | protocolo falado com tipos, não por texto |
| IA | `IChatClient` do `Microsoft.Extensions.AI` | tool-use uniforme entre os três provedores |
| Provedores | SDKs **oficiais**: Anthropic 12.x, OpenAI (via `Microsoft.Extensions.AI.OpenAI`), `Google.GenAI` | acompanham mudanças de API sem intermediário |
| Segredos | DPAPI no Windows; permissões de arquivo no restante | usa o que o sistema oferece — e admite quando não oferece |

### Por que não C++ em parte do sistema

Foi considerado, por causa da orquestração de várias IAs simultâneas. Não se aplica:
o trabalho local (rotear, montar JSON, atualizar a interface) leva menos de um
milissegundo, enquanto cada chamada de IA leva segundos **de espera de rede**. O app é
I/O-bound do início ao fim, e `async/await` é a ferramenta certa para isso.

O gargalo real de rodar muitas automações ao mesmo tempo é **memória** (cada agente de
tela sobe um Chromium) e **limite de requisições por minuto** do provedor. Nenhum dos
dois melhora trocando de linguagem — por isso existe um teto de tarefas simultâneas.

C++ só faria sentido para inferência local de modelos, e mesmo aí a via seria uma
biblioteca pronta consumida pelo .NET.

## 3. Estrutura

```
Quorum.Core        domínio puro (sem IO): modelos, roteador, contratos de IA,
                   extração de scripts, geração de relatório
Quorum.Providers   Claude · OpenAI · Gemini + catálogo dinâmico de modelos
Quorum.Agents      loop agentic, classificação de falhas, fallback, sessões simultâneas
Quorum.Mcp         cliente MCP e agentes de tela/banco
Quorum.Security    cofre de chaves
Quorum.App         interface Avalonia (MVVM)
Quorum.Tests       xUnit — roda sem chave, sem rede, sem Node
Quorum.McpSmokeTest   verificação de ambiente, custo zero
```

Dependências apontam sempre para dentro: `App → Agents → {Providers, Mcp, Core}`, e
`Core` não depende de ninguém. É isso que torna a suíte inteira executável de graça.

## 4. Decisões que moldaram o produto

### 4.1 Um adaptador, não três (a "Opção B")

Os três SDKs expõem `IChatClient`. A tradução entre os tipos do Quorum e essa interface
vive num **único** `ChatClientAiProvider`; cada provider concreto só constrói seu
cliente (~15 linhas).

A alternativa — falar com cada SDK diretamente — significava três traduções paralelas.
Era exatamente onde a versão anterior deste projeto acumulava divergências: o prompt de
sistema chegava a dois provedores e se perdia no terceiro.

### 4.2 Provider por modelo

Os três SDKs vinculam o modelo na construção do cliente. Em vez de contornar, a fábrica
passou a criar **um provider por modelo** — o que encaixou com o executor de fallback,
que já instanciava um provider para cada modelo da cadeia. O adaptador guarda o modelo
ao qual foi vinculado e **falha alto** se receber requisição para outro, em vez de
depender de o SDK honrar o override.

### 4.3 O roteador é uma função pura

Dada uma tarefa e as preferências, escolhe o modelo. Sem rede, sem estado mutável: cada
regra vira um teste unitário, e a lógica que decide onde seu dinheiro é gasto está
provada antes de qualquer chamada paga.

Regras: chat e leitura de página → tier rápido; automação → tier equilibrado **com
tool-use**; análise longa → maior janela de contexto. Modo economia inverte a
prioridade: o mais barato que ainda atende. Fixação manual desliga a escolha automática.

### 4.4 Só modelos que você pode usar

O roteador considera apenas provedores com chave cadastrada. Sem isso, com uma chave
Claude ele poderia eleger um modelo Gemini — e a fábrica, que detecta o provedor **pela
chave**, criaria um cliente Claude pedindo um modelo que ele não conhece. Falha na
primeira chamada, com crédito já gasto.

`AvailableProviders` distingue **nulo** (não especificado, sem restrição) de **vazio**
(verificado, não há nenhum) — a segunda situação produz a orientação de cadastrar uma
chave, em vez de um erro genérico.

### 4.5 Catálogo vindo do provedor

Modelos são aposentados sem aviso. A lista é buscada por HTTP direto em cada provedor;
modelos conhecidos mantêm a tabela de preços, e os novos entram marcados como **preço
desconhecido** — nunca como custo zero, que faria o modo economia elegê-los como os mais
baratos do catálogo.

### 4.6 A revisão é opcional, sob demanda e avisa o custo

O botão só aparece **depois** do resultado: você lê o relatório e decide se aquele caso
específico merece a segunda opinião. A tarja diz quem revisaria e que consome tokens.

O revisor nunca é o modelo que escreveu (um modelo tende a concordar com o próprio
texto) e prefere **outro provedor**; se só houver um, a interface avisa que a opinião é
menos independente. A revisão **não usa ferramentas** — é leitura crítica, uma chamada
só, com teto de dois passos — e seus tokens são contabilizados à parte.

### 4.7 Persistência de chaves é opt-in e honesta

Desligada por padrão. No Windows as chaves são cifradas com DPAPI no escopo da conta;
em Linux e macOS não há equivalente sem dependências nativas, então o arquivo fica com
permissão `0600` **sem cifra** — e a interface mostra o texto correspondente ao sistema
em uso. Desmarcar a opção **apaga** o que estava guardado: "não quero mais que lembre"
é um pedido para remover, não para congelar.

## 5. Regras que nasceram de erros

Cada uma destas custou um bug real durante a construção. Estão no código como
comentário, e aqui como regra.

**Falha operacional ≠ bug.** Rede, cota, chave recusada e timeout encerram a tarefa com
razão legível e disparam o fallback. Já `NullReferenceException`, `ArgumentException` e
falhas fatais **sobem** — mascarar um defeito nosso como "todos os modelos falharam"
esconde o problema e queima crédito tentando.

**Cada agente traduz as exceções do seu domínio.** O classificador genérico é a última
linha, não a primeira. Bibliotecas reais usam `InvalidOperationException` para condições
operacionais (o ADO.NET a lança para "conexão fechada"), e é o executor concreto quem
sabe disso. No MCP, argumento inválido vira erro **para a IA corrigir**, porque foi ela
quem montou os parâmetros.

**Timeout de rede não é cancelamento do usuário.** `TaskCanceledException` herda de
`OperationCanceledException`; sem o filtro `when (ct.IsCancellationRequested)`, um
timeout do provedor era tratado como "o usuário parou" e o fallback nunca disparava.

**Falha marcada, não descrita.** Um erro sinalizado pelo protocolo MCP marca o resultado
como erro de verdade, e não só o menciona no texto — assim a IA sabe que a ferramenta
falhou em vez de precisar deduzir.

**Truncar em silêncio induz a conclusão errada.** Resultado cortado leva marcador
explícito, senão a IA conclui que a consulta retornou só aquelas linhas.

**Status HTTP 4xx/5xx não é falha da ferramenta.** A requisição funcionou; a resposta é
justamente o objeto do teste. Quem julga se o 404 era esperado é a IA.

**Validar sempre em Debug e Release.** Código sob `#if DEBUG` nunca é compilado em
Release, e o build Debug incremental não revalida XAML — um erro de binding passou em
Debug e só apareceu em Release.

**Teste não fixa formato dependente de cultura.** Um `assert` esperando `"1.234"` quebra
em máquina com separador diferente.

## 6. Como a suíte roda sem gastar nada

| O que seria pago/externo | Substituto no teste |
|---|---|
| Chamada a provedor de IA | provider roteirizado (respostas programadas) |
| Requisição HTTP | `HttpMessageHandler` falso |
| Servidor MCP (Node) | `IMcpSession` falsa |
| Cofre de chaves | pasta temporária, apagada ao fim |

O CI compila e roda tudo no Ubuntu, Windows e macOS a cada push. A interface é compilada
nos três, mas não executada — não há display nos runners; a verificação visual é local.

## 7. Roadmap

**Concluído**
- Fundação: solução, roteador, registro de modelos, CI
- Provedores: Claude, OpenAI e Gemini sobre um adaptador único
- Agentes: loop agentic, classificação de falhas, fallback, sessões simultâneas
- MCP: cliente, agentes de tela e banco, smoke test de ambiente
- Interface: Chat, Automação, Modelos, Configurações
- Saídas: extração de scripts e relatório HTML
- Segurança: cofre de chaves opt-in
- Quorum de revisão: segunda IA sob demanda

**Pendente**
- **Fase D — validação com créditos.** Roteiro: smoke test do MCP (custo zero) → teste
  de API contra endpoint público (o caminho mais barato que exercita a cadeia inteira) →
  SQLite local → navegador. Só depois, automações contra sistemas reais.
- Instaladores por plataforma (MSIX/Inno, AppImage/deb, dmg)
- Assinatura digital do executável no Windows

**Considerado e adiado**
- Classificar a tarefa com uma IA antes de rotear: gasta token e é difícil de depurar;
  as regras atuais resolvem sem custo.
- Correr vários modelos pela mesma resposta e ficar com o primeiro: paga três, descarta
  dois. O paralelismo que vale é o de tarefas independentes, que já existe.
