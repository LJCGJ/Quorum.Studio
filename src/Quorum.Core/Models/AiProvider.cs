namespace Quorum.Core.Models;

/// <summary>
/// Provedores de IA suportados. O roteamento e a deteccao por prefixo de chave
/// usam este enum como identidade estavel do provedor.
/// </summary>
public enum AiProvider
{
    Claude,
    OpenAI,
    Gemini
}
