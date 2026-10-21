using System.Text.Json;

namespace CardTradeBuddy.Services;

public interface ILlmService
{
    /// <summary>Names of the installed models that can read images.</summary>
    Task<IReadOnlyList<string>> GetVisionModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an image and a prompt to the given model and returns the model's answer as JSON.
    /// </summary>
    Task<JsonElement> AskAboutImageAsync(Stream image, string prompt, string model, CancellationToken cancellationToken = default);
}
