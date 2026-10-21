using System.Text.Json;

namespace CardTradeBuddy.Services;

public class OllamaLlmService(HttpClient http) : ILlmService
{
    public const string DefaultBaseUrl = "http://localhost:11434/";

    public async Task<IReadOnlyList<string>> GetVisionModelsAsync(CancellationToken cancellationToken = default)
    {
        using var tags = await GetJsonAsync("api/tags", cancellationToken);

        var result = new List<string>();
        foreach (var model in tags.RootElement.GetProperty("models").EnumerateArray())
        {
            var name = model.GetProperty("name").GetString();
            if (name is null)
                continue;

            // Ollama reports what each model can do; embedding and text-only models are left out.
            // Older Ollama versions do not report capabilities, so then the model is listed anyway.
            using var show = await PostJsonAsync("api/show", new { model = name }, cancellationToken);
            if (!show.RootElement.TryGetProperty("capabilities", out var capabilities) ||
                capabilities.EnumerateArray().Any(c => c.GetString() == "vision"))
            {
                result.Add(name);
            }
        }

        return result;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    private async Task<JsonDocument> PostJsonAsync(string url, object body, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(url, body, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {error}", null, response.StatusCode);
        }

        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
    }

    public async Task<JsonElement> AskAboutImageAsync(Stream image, string prompt, string model, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, cancellationToken);

        var request = new
        {
            model,
            stream = false,
            format = "json", // constrains the reply to valid JSON
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt,
                    images = new[] { Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length) },
                },
            },
        };

        using var response = await http.PostAsJsonAsync("api/chat", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {error}", null, response.StatusCode);
        }

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString();

        try
        {
            using var result = JsonDocument.Parse(content ?? "");
            return result.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Model '{model}' did not return valid JSON: {content}", ex);
        }
    }
}
