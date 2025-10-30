using AccedeSimple.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI.Hosting;
using AccedeSimple.Service;
using System.Collections.Concurrent;
using AccedeSimple.Service.Services;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using Microsoft.Agents.AI;
using Microsoft.ML.Tokenizers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("LocalGuide", c =>
    {
        c.BaseAddress = new Uri("http://localguide");
    });

// Load configuration
builder.Services.Configure<UserSettings>(builder.Configuration.GetSection("UserSettings"));

// Add state stores
builder.Services.AddSingleton<StateStore>();
builder.Services.AddKeyedSingleton<ConcurrentDictionary<string,List<ChatItem>>>("history");

// Add storage
builder.AddKeyedAzureBlobClient("uploads");

// Configure logging
builder.Services.AddLogging();

// Chat message stream for SSE
builder.Services.AddSingleton<ChatStream>();

builder.AddServiceDefaults();

builder.Services.AddMcpClient();

builder.Services
    .AddChatClient(modelName: Environment.GetEnvironmentVariable("MODEL_NAME") ?? "gpt-4o-mini")
    .UseFunctionInvocation();

builder.Services.AddEmbeddingGenerator(modelName: EmbeddingModel.NAME);

// Add SQLite vector store
builder.Services.AddSingleton<VectorStore>(sp =>
{
    var embeddingGenerator = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
    return new SqliteVectorStore("Data Source=documents.db", new()
    {
        EmbeddingGenerator = embeddingGenerator
    });
});

// Register ingestion pipeline components
builder.Services.AddSingleton<PdfPigReader>();
builder.Services.AddSingleton<Tokenizer>(sp => TiktokenTokenizer.CreateForModel("gpt-4"));

builder.Services.AddTransient<ProcessService>();
builder.Services.AddTransient<MessageService>();
builder.Services.AddSingleton<SearchService>();
builder.Services.AddSingleton<IngestionService>();

builder.AddAIAgent("Policy", (sp, name) =>
{
    return sp.GetRequiredService<IChatClient>().CreateAIAgent("""
            Process the policy inquiry.

            Only use the search results to answer the user's question.

            Do not provide any additional information or context.

            Provide a summary of the policy based on the users' input and the search results from the policy documents.
            """, name, tools: [AIFunctionFactory.Create(sp.GetRequiredService<SearchService>().SearchAsync)]);
});

builder.Services.AddTravelWorkflow();

var app = builder.Build();

// Run ingestion on startup
var ingestionService = app.Services.GetRequiredService<IngestionService>();
await ingestionService.IngestAsync(Path.Combine(AppContext.BaseDirectory, "docs"));

app.MapEndpoints();

app.Run();

public static class EmbeddingModel
{
    public const string NAME = "text-embedding-3-small";
    public const int DIMENSION = 1536;
}