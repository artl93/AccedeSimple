using System.Text.Json;
using Microsoft.Extensions.AI;
using AccedeSimple.Domain;
using ModelContextProtocol.Client;
using AccedeSimple.Service.Services;
using Microsoft.Extensions.Options;
using Microsoft.Agents.AI.Workflows;

namespace AccedeSimple.Service.Executors;

public class TravelPlanningExecutor(
    ILogger<TravelPlanningExecutor> logger,
    IChatClient chatClient,
    IMcpClient mcpClient,
    MessageService messageService,
    IOptions<UserSettings> userSettings) : Executor<UserMessage, List<TripOption>>("TravelPlanningExecutor")
{
    private readonly IChatClient _chatClient = chatClient;
    private readonly ILogger<TravelPlanningExecutor> _logger = logger;
    private readonly IMcpClient _mcpClient = mcpClient;
    private readonly MessageService _messageService = messageService;
    private readonly UserSettings _userSettings = userSettings.Value;

    public override async ValueTask<List<TripOption>> HandleAsync(
        UserMessage userInput,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        // Generate new trip parameters
        var tripParameterPrompt =
            $"""
            You are a travel assistant. Your task is to generate trip parameters based on the user input.

            The user has provided the following information:

            {userInput.Text}

            Today's date is: {DateTime.Now.ToString()}

            Generate trip parameters
            """;

        var res = await _chatClient.GetResponseAsync<TripParameters>(tripParameterPrompt, cancellationToken: cancellationToken);

        res.TryGetResult(out var tripParameters);

        List<ChatMessage> messages = [
            new ChatMessage(ChatRole.User,
                $"""
                You are a travel planning assistant. Generate trip options based on the provided parameters.

                {JsonSerializer.Serialize(tripParameters)}

                Consider factors like cost, convenience, and preferences. Each option should include:
                - Flight details (departure/arrival times, airline, price)
                - Hotel options (location, check-in/out dates, price)
                - Car rental options if requested

                Ensure that there is a variety of options to choose from, including different airlines, hotels, and car rental companies.

                Generate at least 3 different trip options with a detailed breakdown of each option.

                Ensure that dates are formatted correctly.
                """)
        ];

        var tools = await _mcpClient.ListToolsAsync();

        await _messageService.AddMessageAsync(new TripRequestUpdated("Planning your trip..."), _userSettings.UserId);

        var response = await _chatClient.GetResponseAsync<List<TripOption>>(
            messages,
            new ChatOptions
            {
                Temperature = 0.7f,
                Tools = [.. tools]
            },
            cancellationToken: cancellationToken);

        response.TryGetResult(out var result);

        var options = result ?? [];

        //TODO: I would like to flow the options through output types instead of using state
        //like this if we could. Otherwise use constants or some id.
        await context.QueueStateUpdateAsync("trip-options", options, "travel", cancellationToken);

        return options;
    }

    protected override Microsoft.Agents.AI.Workflows.RouteBuilder ConfigureRoutes(Microsoft.Agents.AI.Workflows.RouteBuilder routeBuilder)
    {
        return routeBuilder.AddHandler<TripPlanner, TripPlanner>(HandleAsync);
    }

    public async ValueTask<TripPlanner> HandleAsync(TripPlanner trip, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Generate new trip parameters
        var tripParameterPrompt =
            $"""
            You are a travel assistant. Your task is to generate trip parameters based on the user input.

            The user has provided the following information:

            {string.Join(Environment.NewLine, trip.Conversation.Select(msg => msg.ToString()))}

            Today's date is: {DateTime.Now.ToString()}

            Take any policy comments into consideration:

            {string.Join(Environment.NewLine, trip.PolicyComments)}

            Generate trip parameters
            """;

        var res = await _chatClient.GetResponseAsync<TripParameters>(tripParameterPrompt, cancellationToken: cancellationToken);

        res.TryGetResult(out var tripParameters);

        List<ChatMessage> messages = [
            new ChatMessage(ChatRole.User,
                $"""
                You are a travel planning assistant. Generate trip options based on the provided parameters.

                {JsonSerializer.Serialize(tripParameters)}

                Consider factors like cost, convenience, and preferences. Each option should include:
                - Flight details (departure/arrival times, airline, price)
                - Hotel options (location, check-in/out dates, price)
                - Car rental options if requested

                Ensure that there is a variety of options to choose from, including different airlines, hotels, and car rental companies.

                Generate at least 3 different trip options with a detailed breakdown of each option.

                Ensure that dates are formatted correctly.
                """)
        ];

        var tools = await _mcpClient.ListToolsAsync();

        await _messageService.AddMessageAsync(new TripRequestUpdated("Planning your trip..."), _userSettings.UserId);

        var response = await _chatClient.GetResponseAsync<List<TripOption>>(
            messages,
            new ChatOptions
            {
                Temperature = 0.7f,
                Tools = [.. tools]
            },
            cancellationToken: cancellationToken);

        response.TryGetResult(out var result);

        var options = result ?? [];

        trip.TripOptions.AddRange(options);

        return trip;
    }
}
