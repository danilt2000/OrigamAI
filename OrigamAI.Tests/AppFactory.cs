using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OrigamAI.Services;

namespace OrigamAI.Tests;

/// <summary>
/// Spins up the real Program.cs host in-memory. Use it to resolve any registered
/// service (e.g. RagIngestService, RagAskService) and call methods directly,
/// without going through HTTP.
/// </summary>
public class AppFactory : WebApplicationFactory<Program>
{
    public IServiceScope CreateScope() => Services.CreateScope();

    public T Resolve<T>() where T : notnull
    {
        var scope = CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    public RagIngestService Ingest => Resolve<RagIngestService>();
    public RagAskService Ask => Resolve<RagAskService>();
}
