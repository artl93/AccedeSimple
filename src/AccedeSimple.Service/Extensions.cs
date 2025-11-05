#pragma warning disable
using Azure.Identity;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using AccedeSimple.Service.Executors;
using AccedeSimple.Service.Services;
using Microsoft.SemanticKernel;
using AccedeSimple.Domain;
using ModelContextProtocol.Client;
using Microsoft.Agents.AI.Workflows;
using AccedeSimple.Service;

public static class Extensions
{

    public static IServiceCollection AddTravelWorkflow(
        this IServiceCollection services)
    {
        // Register executors
        services.AddTransient<TravelPlanningExecutor>();
        services.AddTransient<TripRequestCreationExecutor>();
        services.AddTransient<ApprovalResponseExecutor>();

        // Build the main travel workflow with RequestPorts for human-in-the-loop
        // This creates a unified workflow with checkpointing support
        services.AddTransient<Microsoft.Agents.AI.Workflows.Workflow>(sp =>
        {
            // Get all executors
            var travelPlanning = sp.GetRequiredService<TravelPlanningExecutor>();
            var tripRequestCreation = sp.GetRequiredService<TripRequestCreationExecutor>();
            var approvalResponse = sp.GetRequiredService<ApprovalResponseExecutor>();

            // Create RequestPorts for human-in-the-loop interactions
            // UserSelectionPort: sends trip options to user, waits for their selection
            var userSelectionPort = RequestPort.Create<List<TripOption>, ItinerarySelectedChatItem>("UserSelection");

            // AdminApprovalPort: sends trip request to admin, waits for approval decision
            var adminApprovalPort = RequestPort.Create<TripRequest, TripRequestResult>("AdminApproval");

            // Build workflow: TravelPlanning → UserSelectionPort → TripRequestCreation → AdminApprovalPort → ApprovalResponse
            var travelWorkflow = new WorkflowBuilder(travelPlanning)
                .AddEdge(travelPlanning, userSelectionPort)
                .AddEdge(userSelectionPort, tripRequestCreation)
                .AddEdge(tripRequestCreation, adminApprovalPort)
                .AddEdge(adminApprovalPort, approvalResponse)
                .WithOutputFrom(approvalResponse)
                .Build();

            return travelWorkflow;
        });

        return services;
    }


    public static IServiceCollection AddMcpClient(this IServiceCollection services)
    {
        services.AddTransient<McpClient>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            McpClientOptions mcpClientOptions = new()
            {
                ClientInfo = new (){
                    Name = "AspNetCoreSseClient",
                    Version = "1.0.0"
                }
            };

            var serviceName = "mcpserver";
            var name = $"services__{serviceName}__http__0";
            var baseUrl = Environment.GetEnvironmentVariable(name);

            var clientTransport = new HttpClientTransport(new HttpClientTransportOptions {
                Endpoint = new Uri(baseUrl!)
            }, loggerFactory);

            // Not ideal pattern but should be enough to get it working.
            var mcpClient = McpClient.CreateAsync(clientTransport, mcpClientOptions, loggerFactory).GetAwaiter().GetResult();

            return mcpClient;
        });

        return services;        
    }
}
#pragma warning restore