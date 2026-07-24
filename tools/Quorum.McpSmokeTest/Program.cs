using Quorum.Mcp;

// =============================================================================
// Smoke test do MCP — verifica se esta maquina consegue subir e conversar com um
// servidor MCP de verdade. NAO usa IA nem chave de API: custo zero.
//
// Rode com:  dotnet run --project tools/Quorum.McpSmokeTest
//
// Serve para responder "o ambiente esta pronto?" antes de gastar creditos numa
// automacao real, e para diagnosticar rapido quando algo para de funcionar.
// =============================================================================

Console.WriteLine("Quorum — smoke test do MCP");
Console.WriteLine("==========================\n");

var spec = new McpServerSpec(
    McpServerSpec.NpxCommand,
    new[] { "-y", "@modelcontextprotocol/server-everything" },
    "Servidor de referencia do protocolo");

Console.WriteLine($"1. Subindo servidor: {spec.Command} {string.Join(' ', spec.Arguments)}");
Console.WriteLine("   (a primeira execucao baixa o pacote e pode demorar)\n");

using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

try
{
    await using var sessao = await StdioMcpSession.StartAsync(spec, ct: cts.Token);
    Console.WriteLine("   OK — handshake concluido.\n");

    Console.WriteLine("2. Listando ferramentas anunciadas");
    var tools = await sessao.ListToolsAsync(cts.Token);
    Console.WriteLine($"   OK — {tools.Count} ferramentas.");
    foreach (var t in tools.Take(3))
        Console.WriteLine($"     · {t.Name}");
    Console.WriteLine();

    Console.WriteLine("3. Executando uma ferramenta de verdade (echo)");
    var r = await sessao.CallToolAsync("echo", """{"message":"quorum vivo"}""", cts.Token);
    Console.WriteLine($"   OK — resposta: {r.Text}");
    Console.WriteLine($"        marcada como erro pelo protocolo? {r.IsError}\n");

    Console.WriteLine("Ambiente pronto: este computador consegue rodar automacoes via MCP.");
    return 0;
}
catch (McpStartupException ex)
{
    Console.WriteLine($"   FALHOU — {ex.Message}");
    return 1;
}
catch (OperationCanceledException)
{
    Console.WriteLine("   FALHOU — tempo esgotado. Verifique a conexao com a internet.");
    return 1;
}
catch (Exception ex)
{
    Console.WriteLine($"   FALHOU — {ex.GetType().Name}: {ex.Message}");
    return 1;
}
