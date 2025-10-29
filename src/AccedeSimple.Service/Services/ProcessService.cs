#pragma warning disable
using System.Text.Json;
using AccedeSimple.Domain;
using AccedeSimple.Service.Executors;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;

namespace AccedeSimple.Service.Services;

public class ProcessService
{
    private readonly MessageService _messageService;
    private readonly IChatClient _chatClient;
    private readonly UserSettings _userSettings;
    private readonly HttpClient _httpClient;
    private readonly AIAgent _policyAgent;
    private readonly IServiceProvider _serviceProvider;
    private readonly StateStore _stateStore;
    private readonly ILogger<ProcessService> _logger;

    public ProcessService(
        MessageService messageService,
        IChatClient chatClient,
        IOptions<UserSettings> userSettings,
        IHttpClientFactory httpClientFactory,
        [FromKeyedServices("Policy")] AIAgent policyAgent,
        IServiceProvider serviceProvider,
        StateStore stateStore,
        ILogger<ProcessService> logger)
    {
        _messageService = messageService;
        _chatClient = chatClient;
        _userSettings = userSettings.Value;
        _httpClient = httpClientFactory.CreateClient("LocalGuide");
        _policyAgent = policyAgent;
        _serviceProvider = serviceProvider;
        _stateStore = stateStore;
        _logger = logger;
    }

    public async Task ActAsync(UserIntent userIntent, ChatItem userInput)
    {
        switch (userIntent)
        {
            case UserIntent.General:
                // Handle general inquiries
                var response = await _chatClient.GetResponseAsync(userInput.ToChatMessage());
                await _messageService.AddMessageAsync(new AssistantResponse(response.Text), _userSettings.UserId);
                break;

            case UserIntent.AskLocalGuide:
                // Handle local guide inquiries
                var builder = new UriBuilder(_httpClient.BaseAddress)
                {
                    Path = "attractions",
                    Query = $"query={Uri.EscapeDataString(userInput.Text)}"
                };
                var localGuideRequest = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Post, builder.Uri));
                var body = await localGuideRequest.Content.ReadAsStringAsync();
                await _messageService.AddMessageAsync(new AssistantResponse(body), _userSettings.UserId);
                break;

            case UserIntent.AskPolicyQuestions:
                // Use the policy agent for policy inquiries
                var policyResponse = await _policyAgent.RunAsync(userInput.ToChatMessage());
                await _messageService.AddMessageAsync(new AssistantResponse(policyResponse.Text), _userSettings.UserId);
                break;

            case UserIntent.StartTravelPlanning when userInput is UserMessage userMessage:
                // Start the travel workflow - await until it pauses at RequestPort
                var tripId = Guid.NewGuid().ToString();
                await RunOrResumeWorkflowAsync(
                    workflow: _serviceProvider.GetRequiredService<Microsoft.Agents.AI.Workflows.Workflow>(),
                    workflowName: "travel workflow",
                    tripId: tripId,
                    data: userMessage);
                break;

            case UserIntent.StartTripApproval when userInput is ItinerarySelectedChatItem itinerarySelected:
                // Resume the workflow from checkpoint with user's selection
                await RunOrResumeWorkflowAsync<object>(
                    workflow: _serviceProvider.GetRequiredService<Microsoft.Agents.AI.Workflows.Workflow>(),
                    workflowName: "travel workflow",
                    tripId: itinerarySelected.TripId,
                    data: itinerarySelected,
                    sessionNotFoundMessage: "Sorry, I couldn't find your trip planning session. Please start over.");
                break;

            default:
                await _messageService.AddMessageAsync(new AssistantResponse("Unknown intent. Please clarify your request."), _userSettings.UserId);
                break;
        }
    }

    /// <summary>
    /// Run or resume a workflow. Determines start vs resume based on whether checkpoint exists for the tripId.
    /// </summary>
    /// <typeparam name="TInput">The data type - workflow input type when starting (e.g., UserMessage), response type when resuming (e.g., object, TripRequestResult)</typeparam>
    /// <param name="workflowKey">Optional key for keyed workflow service. If null, uses default workflow.</param>
    /// <param name="tripId">The trip ID - used as RunId when starting, or to lookup checkpoint when resuming</param>
    /// <param name="data">Data for the workflow - input when starting, response when resuming</param>
    private async Task RunOrResumeWorkflowAsync<TInput>(
        Workflow workflow,
        string workflowName,
        string tripId,
        TInput? data = default,
        string? sessionNotFoundMessage = null) where TInput : notnull
    {
        var checkpointManager = CheckpointManager.Default;

        // Check if checkpoint exists to determine if we're resuming or starting
        var checkpointInfo = _stateStore.GetAs<CheckpointInfo>($"checkpoint-info:{tripId}");

        if (checkpointInfo != null)
        {
            // Checkpoint exists - we're resuming
            _logger.LogInformation("Resuming {WorkflowName} for trip {TripId}", workflowName, tripId);

            // Get the stored ExternalRequest
            var storedRequest = _stateStore.GetAs<ExternalRequest>($"pending-request:{tripId}");
            if (storedRequest == null)
            {
                _logger.LogError("No pending request found for trip {TripId}", tripId);
                await _messageService.AddMessageAsync(new AssistantResponse(sessionNotFoundMessage ?? "Session not found."), _userSettings.UserId);
                return;
            }

            await using var checkpointedRun = await InProcessExecution.ResumeStreamAsync(workflow, checkpointInfo, checkpointManager, checkpointInfo.RunId);
            await ProcessWorkflowEventsAsync(checkpointedRun, tripId, data);
        }
        else
        {
            // No checkpoint - we're starting new
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data), "Data is required when starting a new workflow");
            }

            _logger.LogInformation("Starting {WorkflowName} with tripId/RunId {TripId}", workflowName, tripId);

            // Use tripId as RunId for the workflow
            await using var checkpointedRun = await InProcessExecution.StreamAsync(workflow, data, checkpointManager, tripId);
            await ProcessWorkflowEventsAsync(checkpointedRun, null, null);
        }
    }

    private async Task ProcessWorkflowEventsAsync(
        Checkpointed<StreamingRun> checkpointedRun,
        string? tripId,
        object? response)
    {
        CheckpointInfo? lastCheckpointInfo = null;
 

        if(response != null)
        {
            var pendingRequest = _stateStore.GetAs<ExternalRequest>($"pending-request:{tripId}");
            if (pendingRequest != null && tripId != null && response != null)
            {
                _logger.LogInformation("Workflow in PendingRequests state at start, sending response");
                var externalResponse = pendingRequest.CreateResponse(response);
                await checkpointedRun.Run.SendResponseAsync(externalResponse);
                _stateStore.Delete($"pending-request:{tripId}");
            }
        }

        // Process events
        await foreach (var evt in checkpointedRun.Run.WatchStreamAsync())
        {
            switch (evt)
            {
                case RequestInfoEvent requestInfoEvt:
                    // Hit a RequestPort - handle it but continue processing events until idle
                    _logger.LogInformation("Workflow paused at RequestPort {PortId}", requestInfoEvt.Request.PortInfo.PortId);
                    await HandleRequestInfoAndPauseAsync(requestInfoEvt, lastCheckpointInfo, tripId);
                    break;

                case WorkflowOutputEvent outputEvt:
                    _logger.LogInformation("Workflow completed with output");
                    if (tripId != null)
                    {
                        await CleanupWorkflowStateAsync(tripId);
                    }
                    return;

                case WorkflowErrorEvent errorEvt:
                    var exception = errorEvt.Data as Exception;
                    _logger.LogError(exception, "Workflow error");
                    await _messageService.AddMessageAsync(
                        new AssistantResponse($"An error occurred: {exception?.Message ?? "Unknown error"}"),
                        _userSettings.UserId);
                    if (tripId != null)
                    {
                        await CleanupWorkflowStateAsync(tripId);
                    }
                    return;
            }

            lastCheckpointInfo = checkpointedRun.LastCheckpoint;
            // After processing each event, check if the workflow is idle
            var status = await checkpointedRun.Run.GetStatusAsync();
            _logger.LogInformation("Current workflow status: {Status}", status);
            if (status == RunStatus.Idle || (status == RunStatus.PendingRequests))
            {
                _stateStore.Set($"checkpoint-info:{lastCheckpointInfo.RunId}", lastCheckpointInfo);
                _logger.LogInformation("Workflow idle or pending data, exiting event loop");
                return;
            }
        }

        if (tripId != null)
        {
            _logger.LogWarning("Resume workflow event stream ended without WorkflowOutputEvent or RequestInfoEvent");
        }
    }

    /// <summary>
    /// Resume travel workflow with admin approval decision
    /// </summary>
    public async Task ResumeWorkflowWithApprovalAsync(string tripId, TripRequestResult approvalResult)
    {
        await RunOrResumeWorkflowAsync<object>(
            workflow: _serviceProvider.GetRequiredService<Microsoft.Agents.AI.Workflows.Workflow>(),
            workflowName: "travel workflow",
            tripId: tripId,
            data: approvalResult,
            sessionNotFoundMessage: "Sorry, I couldn't find your trip planning session. Please start over.");
    }

    /// <summary>
    /// Handle a RequestInfoEvent by storing CheckpointInfo metadata and sending message to user
    /// </summary>
    /// <param name="requestInfoEvt">The RequestInfoEvent from the workflow</param>
    /// <param name="checkpointInfo">CheckpointInfo metadata (contains RunId and CheckpointId)</param>
    /// <param name="tripId">The trip ID from the previous pause (for subsequent pauses in same workflow)</param>
    private async Task HandleRequestInfoAndPauseAsync(RequestInfoEvent requestInfoEvt, CheckpointInfo? checkpointInfo, string? tripId = null)
    {
        var request = requestInfoEvt.Request;

        if (checkpointInfo == null)
        {
            _logger.LogError("No CheckpointInfo available when pausing at RequestPort {PortId}", request.PortInfo.PortId);
            return;
        }

        switch (request.PortInfo.PortId)
        {
            case "UserSelection" when request.DataAs<List<TripOption>>() is { } tripOptions:
                // User needs to select a trip option
                // Create the message with RunId as its Id - this ensures consistent workflow identity
                var candidateMessage = new CandidateItineraryChatItem("Here are trips matching your requirements.", tripOptions)
                {
                    Id = checkpointInfo.RunId
                };
                // Store the ExternalRequest so we can respond when user resumes
                _stateStore.Set($"pending-request:{checkpointInfo.RunId}", request);

                // Send trip options to user
                await _messageService.AddMessageAsync(candidateMessage, _userSettings.UserId);
                break;

            case "AdminApproval" when request.DataAs<TripRequest>() is { } tripRequest:

                // Store the ExternalRequest so we can respond when admin approves/rejects
                _stateStore.Set($"pending-request:{checkpointInfo.RunId}", request);

                // Store trip request in global StateStore list for admin page to display pending approvals
                var existingRequests = _stateStore.GetAs<List<TripRequest>>("trip-requests") ?? new List<TripRequest>();
                existingRequests.Add(tripRequest);
                _stateStore.Set("trip-requests", existingRequests);

                // Send approval request message
                await _messageService.AddMessageAsync(
                    new AssistantResponse($"Trip request created. Awaiting admin approval for trip {tripRequest.TripId}."),
                    _userSettings.UserId);
                break;

            default:
                _logger.LogWarning("Unknown request port: {PortId}", request.PortInfo.PortId);
                break;
        }
    }

    /// <summary>
    /// Cleanup common workflow checkpoint state after completion
    /// </summary>
    /// <param name="tripId">The trip ID (workflow identifier)</param>
    private async Task CleanupWorkflowStateAsync(string? tripId)
    {
        if (tripId == null)
        {
            return;
        }

        // Remove common checkpoint data
        _stateStore.Delete($"checkpoint-info:{tripId}");
        _stateStore.Delete("trip-requests");

        await Task.CompletedTask;
    }
}
#pragma warning restore