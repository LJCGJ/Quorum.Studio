using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Quorum.App.Services;

/// <summary>
/// Salva conteudo em disco pedindo o local ao usuario.
///
/// O seletor de arquivos do Avalonia exige uma janela; para os ViewModels nao
/// precisarem conhecer a interface grafica, a janela e registrada uma vez aqui.
/// </summary>
public sealed class FileSaveService
{
    private Window? _janela;

    /// <summary>Registra a janela principal (chamado uma vez, na inicializacao).</summary>
    public void Attach(Window janela) => _janela = janela;

    /// <summary>
    /// Pergunta onde salvar e grava o conteudo. Devolve o caminho, ou nulo se o
    /// usuario cancelou.
    /// </summary>
    /// <param name="conteudo">Texto a gravar.</param>
    /// <param name="nomeSugerido">Nome inicial, ja com extensao.</param>
    /// <param name="descricaoTipo">Descricao do tipo, ex.: "Script Python".</param>
    /// <param name="extensao">Extensao com ponto, ex.: ".py".</param>
    public async Task<string?> SaveAsync(
        string conteudo, string nomeSugerido, string descricaoTipo, string extensao)
    {
        if (_janela is null) return null;

        var destino = await _janela.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Salvar",
                SuggestedFileName = nomeSugerido,
                DefaultExtension = extensao.TrimStart('.'),
                FileTypeChoices = new List<FilePickerFileType>
                {
                    new(descricaoTipo) { Patterns = new[] { "*" + extensao } },
                    new("Todos os arquivos") { Patterns = new[] { "*" } }
                }
            });

        if (destino is null) return null;

        // O caminho local pode nao existir em plataformas com armazenamento
        // virtual; nesse caso grava pelo fluxo que o proprio seletor oferece.
        var caminho = destino.TryGetLocalPath();
        if (caminho is not null)
        {
            await File.WriteAllTextAsync(caminho, conteudo).ConfigureAwait(false);
            return caminho;
        }

        await using var fluxo = await destino.OpenWriteAsync().ConfigureAwait(false);
        await using var escritor = new StreamWriter(fluxo);
        await escritor.WriteAsync(conteudo).ConfigureAwait(false);
        return destino.Name;
    }

    /// <summary>Nome de arquivo seguro, com data e hora para nao sobrescrever.</summary>
    public static string SugerirNome(string prefixo, string extensao)
    {
        var limpo = string.Join("_", prefixo.Split(Path.GetInvalidFileNameChars(),
            StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return $"{limpo}_{DateTime.Now:yyyyMMdd_HHmmss}{extensao}";
    }
}
