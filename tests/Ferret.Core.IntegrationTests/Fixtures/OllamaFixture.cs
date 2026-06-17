using System.Net.Http.Headers;
using Testcontainers.Ollama;
using Xunit;

namespace Ferret.Core.IntegrationTests.Fixtures;

public sealed class OllamaFixture : IAsyncLifetime
{
    public const string Model = "nomic-embed-text";
    public const int Dimensions = 768;

    private readonly OllamaContainer _container = new OllamaBuilder("ollama/ollama:latest")
        .WithCleanUp(true)
        .Build();

    /// <summary>Base address for the OpenAI-compatible endpoint (provider posts to /v1/embeddings).</summary>
    public string BaseAddress => _container.GetBaseAddress();

    public HttpClient CreateHttpClient()
    {
        var http = new HttpClient { BaseAddress = new Uri(BaseAddress) };
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    private bool _started;

    public async Task InitializeAsync()
    {
        // Only consumed by Ferret.Dev.IntegrationTests (excluded from the solution / CI). Running that
        // project is the opt-in: it pulls the ~hundreds-of-MB image and starts the container.
        await _container.StartAsync();
        _started = true;
        var exec = await _container.ExecAsync(new[] { "ollama", "pull", Model });
        if (exec.ExitCode != 0)
            throw new InvalidOperationException($"ollama pull {Model} failed: {exec.Stderr}");
    }

    public async Task DisposeAsync()
    {
        if (_started) await _container.DisposeAsync();
    }
}

[CollectionDefinition("ollama")]
public sealed class OllamaCollection : ICollectionFixture<OllamaFixture> { }
