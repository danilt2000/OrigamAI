using Microsoft.KernelMemory;
using Microsoft.KernelMemory.AI.Ollama;
using OrigamAI.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var pollinations = builder.Configuration.GetSection("Pollinations");
var ollamaCfg = builder.Configuration.GetSection("Ollama");
var origamCfg = builder.Configuration.GetSection("OrigamCommunity");

var openAiCfg = new OpenAIConfig
{
    APIKey = pollinations["ApiKey"] ?? "",
    Endpoint = pollinations["Endpoint"] ?? "https://text.pollinations.ai/openai",
    TextModel = pollinations["TextModel"] ?? "openai",
    TextModelMaxTokenTotal = 8000,
    EmbeddingModel = "unused-pollinations-has-no-embeddings",
    EmbeddingModelMaxTokenTotal = 1,
    MaxEmbeddingBatchSize = 1
};

var ollama = new OllamaConfig
{
    Endpoint = ollamaCfg["Endpoint"] ?? "http://localhost:11434",
    EmbeddingModel = new OllamaModelConfig(ollamaCfg["EmbeddingModel"] ?? "bge-m3", 8192)
};

var memory = new KernelMemoryBuilder()
    .WithOpenAITextGeneration(openAiCfg)
    .WithOllamaTextEmbeddingGeneration(ollama)
    .WithSimpleVectorDb(new Microsoft.KernelMemory.MemoryStorage.DevTools.SimpleVectorDbConfig
    {
        StorageType = Microsoft.KernelMemory.FileSystem.DevTools.FileSystemTypes.Disk,
        Directory = Path.Combine(builder.Environment.ContentRootPath, "_kmstore", "vectors")
    })
    .WithSimpleFileStorage(new Microsoft.KernelMemory.DocumentStorage.DevTools.SimpleFileStorageConfig
    {
        StorageType = Microsoft.KernelMemory.FileSystem.DevTools.FileSystemTypes.Disk,
        Directory = Path.Combine(builder.Environment.ContentRootPath, "_kmstore", "files")
    })
    .Build<MemoryServerless>();

builder.Services.AddSingleton<IKernelMemory>(memory);

builder.Services.AddHttpClient<PollinationsChatService>();

builder.Services.AddHttpClient<OrigamCommunityService>(client =>
{
    client.BaseAddress = new Uri(origamCfg["BaseUrl"] ?? "https://community.origam.com");
    client.DefaultRequestHeaders.Add("User-Agent", "OrigamAI-RAG/1.0");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opt =>
    {
        opt.Title = "OrigamAI RAG";
        opt.Theme = ScalarTheme.Mars;
    });
    app.MapGet("/", () => Results.Redirect("/scalar/v1"));
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
