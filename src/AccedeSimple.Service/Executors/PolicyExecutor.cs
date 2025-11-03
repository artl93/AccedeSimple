using AccedeSimple.Domain;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using RouteBuilder = Microsoft.Agents.AI.Workflows.RouteBuilder;

namespace AccedeSimple.Service.Executors;

internal class PolicyExecutor([FromKeyedServices("Policy")] AIAgent policyAgent) : Executor<List<ChatMessage>, TripPlanner>("ttt")
{
    public override async ValueTask<TripPlanner> HandleAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var resp = await policyAgent.RunAsync(messages, cancellationToken: cancellationToken);

        var trip = new TripPlanner();
        trip.PolicyComments.Add(resp.Text);
        trip.Conversation.AddRange(messages);

        return trip;
    }

    protected override RouteBuilder ConfigureRoutes(RouteBuilder routeBuilder)
    {
        return routeBuilder.AddHandler<ChatMessage, TripPlanner>(HandleAsync);
    }

    public async ValueTask<TripPlanner> HandleAsync(ChatMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var resp = await policyAgent.RunAsync(message, cancellationToken: cancellationToken);

        var trip = new TripPlanner();
        trip.PolicyComments.Add(resp.Text);
        trip.Conversation.Add(message);

        return trip;
    }
}