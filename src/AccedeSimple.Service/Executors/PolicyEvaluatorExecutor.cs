using AccedeSimple.Domain;
using AccedeSimple.Service.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Options;

namespace AccedeSimple.Service.Executors;

internal class PolicyEvaluatorExecutor(MessageService messageService, IOptions<UserSettings> userSettings, StateStore stateStore) : Executor<TripPlanner, TripPlanner>("PolicyEvaluatorExecutor")
{
    record EvaluationResult(bool IsCompliant, string Comments);

    public override async ValueTask<TripPlanner> HandleAsync(TripPlanner trip, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // For simplicity, we just append a fixed policy evaluation comment.
        trip.PolicyComments.Add("All trip options comply with company travel policies.");

        var evalResult = new EvaluationResult(true, "All trip options comply with company travel policies.");

        //if (evalResult.IsCompliant)
        {
            var tripId = Guid.NewGuid().ToString();
            var candidateMessage = new CandidateItineraryChatItem("Here are trips matching your requirements.", trip.TripOptions)
            {
                Id = tripId
            };
            
            // Store the trip options in StateStore so we can retrieve them when user selects
            stateStore.Set($"trip-options:{tripId}", trip.TripOptions);

            // Send trip options to user
            await messageService.AddMessageAsync(candidateMessage, userSettings.Value.UserId);
        }

        return await Task.FromResult(trip);
    }
}