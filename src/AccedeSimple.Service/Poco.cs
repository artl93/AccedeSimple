using AccedeSimple.Domain;
using Microsoft.Extensions.AI;

namespace AccedeSimple.Domain;

public class TripPlanner()
{
    public List<TripOption> TripOptions { get; } = [];

    public List<string> PolicyComments { get; } = [];

    public List<ChatMessage> Conversation { get; } = [];
}