using Microsoft.Extensions.AI;
using AccedeSimple.Domain;
using Microsoft.Extensions.Options;
using Microsoft.Agents.AI.Workflows;

namespace AccedeSimple.Service.Executors;

public class TripRequestCreationExecutor(
    ILogger<TripRequestCreationExecutor> logger) : Executor<ItinerarySelectedChatItem, TripRequest>("TripRequestCreationExecutor")
{
    private readonly ILogger<TripRequestCreationExecutor> _logger = logger;

    public override async ValueTask<TripRequest> HandleAsync(
        ItinerarySelectedChatItem userInput,
        IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        // Read trip options from workflow state
        var options = await context.ReadStateAsync<List<TripOption>>("trip-options", "travel", cancellationToken);

        if (options == null)
        {
            _logger.LogError("Trip options not found in workflow state");
            throw new InvalidOperationException($"Trip options not found for trip {userInput.TripId}");
        }

        // Find the selected option
        var selectedOption = options.FirstOrDefault(o => o.OptionId == userInput.OptionId);

        if (selectedOption == null)
        {
            _logger.LogError("Selected option {OptionId} not found in trip options", userInput.OptionId);
            throw new InvalidOperationException($"Selected trip option {userInput.OptionId} not found");
        }

        var tripId = userInput.TripId;

        // Create trip request directly with the selected option
        var tripRequest = new TripRequest(
            TripId: tripId,
            TripOption: selectedOption,
            AdditionalNotes: null
        );

        return tripRequest;
    }
}
