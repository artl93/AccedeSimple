#pragma warning disable
using AccedeSimple.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.SemanticKernel;
using AccedeSimple.Service;
using System.Collections.Concurrent;
using AccedeSimple.Service.Services;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.SqliteVec;
using Microsoft.Agents.AI;
using AccedeSimple.Service.Extensions;

var builder = WebApplication.CreateBuilder(args);

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

var kernel = builder.Services.AddKernel();

kernel.Services
    .AddChatClient(modelName: Environment.GetEnvironmentVariable("MODEL_NAME") ?? "gpt-4o-mini")
    .UseFunctionInvocation();

kernel.Services.AddEmbeddingGenerator(modelName: "text-embedding-3-small");
kernel.Services.AddSqliteCollection<int, Document>("Documents", "Data Source=documents.db");
kernel.Services.AddTransient<ProcessService>();
kernel.Services.AddTransient<MessageService>();
kernel.Services.AddSingleton<SearchService>();

builder.AddAIAgent("Policy", (sp, name) =>
{
    return sp.GetRequiredService<IChatClient>().CreateAIAgent("""
            Process the policy inquiry.

            Only use the search results to answer the user's question.

            Do not provide any additional information or context.

            Provide a summary of the policy based on the users' input and the search results from the policy documents.
            """, name, tools: [AIFunctionFactory.Create(sp.GetRequiredService<SearchService>().SearchAsync)]);
});

// Register LocalGuide Python agent via A2A protocol
builder.AddAIAgentFromA2A("LocalGuide", new Uri("http://localguide"));

builder.Services.AddTravelWorkflow();

var app = builder.Build();

var k = app.Services.GetRequiredService<Kernel>();
var collection = k.GetRequiredService<VectorStoreCollection<int, Document>>();
var IngestionService = new IngestionService(collection);
await IngestionService.IngestAsync(Path.Combine(AppContext.BaseDirectory, "docs"));

app.MapEndpoints();

app.Run();

public class UserSettings
{
    public string UserId { get; set; }
    public string AdminUserId { get; set; }

}