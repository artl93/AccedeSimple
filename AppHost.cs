#:sdk Aspire.AppHost.Sdk@9.5.1
#:package Aspire.Hosting.AppHost@9.5.1
#:package Aspire.Hosting.Azure.CognitiveServices@9.5.1
#:package Aspire.Hosting.Azure.Storage@9.5.1
#:package Aspire.Hosting.NodeJS@9.5.1
#:package Aspire.Hosting.Python@9.5.1
#:package CommunityToolkit.Aspire.Hosting.NodeJS.Extensions@9.5.1
#:project src/AccedeSimple.MCPServer/AccedeSimple.MCPServer.csproj
#:project src/AccedeSimple.Service/AccedeSimple.Service.csproj
#:property UserSecretsId=413a2201-5af2-4940-90a4-d0cc6cd5c244

#pragma warning disable
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Define parameters for Azure OpenAI
var azureOpenAIResource = builder.AddParameterFromConfiguration("AzureOpenAIResourceName", "AzureOpenAI:ResourceName");
var azureOpenAIResourceGroup = builder.AddParameterFromConfiguration("AzureOpenAIResourceGroup","AzureOpenAI:ResourceGroup");
var azureOpenAIEndpoint = builder.AddParameterFromConfiguration("AzureOpenAIEndpoint", "AzureOpenAI:Endpoint");
var modelName = "gpt-4.1";


// Configure Azure Services
var azureStorage = builder.AddAzureStorage("storage");
var openai =
    builder.AddAzureOpenAI("openai")
        .AsExisting(azureOpenAIResource, azureOpenAIResourceGroup);

if (builder.Environment.IsDevelopment())
{
    azureStorage.RunAsEmulator(c => {
        c.WithDataBindMount();
        c.WithLifetime(ContainerLifetime.Persistent);
    });
}

// Configure projects
var mcpServer =
    builder.AddProject<Projects.AccedeSimple_MCPServer>("mcpserver")
        .WithReference(openai)
        .WithEnvironment("MODEL_NAME", modelName)
        .WaitFor(openai);


var pythonApp =
    builder.AddPythonApp("localguide", "src/localguide", "main.py")
        .WithHttpEndpoint(env: "PORT", port: 8000, isProxied: false)
        .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAIEndpoint)
        .WithEnvironment("MODEL_NAME", modelName)
        .WithOtlpExporter()
        .WaitFor(openai);

var azureSubscriptionId = builder.AddParameterFromConfiguration("AzureSubscriptionId", "Azure:SubscriptionId");
var azureResourceGroup = builder.AddParameterFromConfiguration("AzureResourceGroup", "Azure:ResourceGroup");
var azureAIFoundryProject = builder.AddParameterFromConfiguration("AzureAIFoundryProject", "AzureAIFoundry:Project");

var backend =
    builder
        .AddProject<Projects.AccedeSimple_Service>("backend")
        .WithReference(openai)
        .WithReference(mcpServer)
        .WithReference(pythonApp)
        .WithReference(azureStorage.AddBlobs("uploads"))
        .WithEnvironment("MODEL_NAME", modelName)
        .WithEnvironment("AZURE_SUBSCRIPTION_ID", azureSubscriptionId)
        .WithEnvironment("AZURE_RESOURCE_GROUP", azureOpenAIResourceGroup)
        .WithEnvironment("AZURE_AI_FOUNDRY_PROJECT", azureAIFoundryProject)
        .WaitFor(openai);

builder.AddNpmApp("webui", "src/webui")
    .WithNpmPackageInstallation()
    .WithHttpEndpoint(env: "PORT", port: 35_369, isProxied: false)
    .WithEnvironment("BACKEND_URL", backend.GetEndpoint("http"))
    .WithExternalHttpEndpoints()
    .WithOtlpExporter()
    .WaitFor(backend)
    .PublishAsDockerFile();

builder.Build().Run();
#pragma warning restore
