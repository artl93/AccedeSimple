using A2A;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.Hosting;

namespace AccedeSimple.Service.Extensions;

/// <summary>
/// Provides extension methods for configuring A2A agents in a host application builder.
/// </summary>
public static class A2AAgentExtensions
{
    /// <summary>
    /// Adds an A2A agent to the host application builder by discovering it via the A2A protocol.
    /// </summary>
    /// <param name="builder">The host application builder to configure.</param>
    /// <param name="name">The name of the agent. This will also be used as the HttpClient name.</param>
    /// <param name="a2aServiceUri">The base URI of the A2A service (e.g., "http://localguide").</param>
    /// <returns>The configured hosted agent builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when builder, name, or a2aServiceUri is null.</exception>
    public static IHostedAgentBuilder AddAIAgentFromA2A(
        this IHostApplicationBuilder builder,
        string name,
        Uri a2aServiceUri)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(a2aServiceUri);

        //Force activation of the LocalGuide agent at startup.
        builder.Services.ActivateKeyedSingleton<AIAgent>("LocalGuide");

        // Register the A2A agent using the factory pattern
        return builder.AddAIAgent(name, (sp, agentName) =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(agentName);

            var cardResolver = new A2ACardResolver(a2aServiceUri, httpClient);
            var agent = cardResolver.GetAIAgentAsync().GetAwaiter().GetResult();

            return agent;
        });
    }

    /// <summary>
    /// Adds an A2A agent to the host application builder using an existing HttpClient registration.
    /// </summary>
    /// <param name="builder">The host application builder to configure.</param>
    /// <param name="name">The name of the agent.</param>
    /// <param name="httpClientName">The name of the pre-registered HttpClient to use.</param>
    /// <param name="a2aServiceUri">The base URI of the A2A service (e.g., "http://localguide").</param>
    /// <returns>The configured hosted agent builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public static IHostedAgentBuilder AddAIAgentFromA2A(
        this IHostApplicationBuilder builder,
        string name,
        string httpClientName,
        Uri a2aServiceUri)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(httpClientName);
        ArgumentNullException.ThrowIfNull(a2aServiceUri);

        //Force activation of the LocalGuide agent at startup.
        builder.Services.ActivateKeyedSingleton<AIAgent>("LocalGuide");

        // Register the A2A agent using the factory pattern with existing HttpClient
        return builder.AddAIAgent(name, (sp, agentName) =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(httpClientName);

            var cardResolver = new A2ACardResolver(a2aServiceUri, httpClient);
            return cardResolver.GetAIAgentAsync().GetAwaiter().GetResult();
        });
    }

    /// <summary>
    /// Adds an A2A agent to the host application builder with a custom HttpClient configuration.
    /// </summary>
    /// <param name="builder">The host application builder to configure.</param>
    /// <param name="name">The name of the agent. This will also be used as the HttpClient name.</param>
    /// <param name="a2aServiceUri">The base URI of the A2A service (e.g., "http://localguide").</param>
    /// <param name="configureHttpClient">An action to configure the HttpClient.</param>
    /// <returns>The configured hosted agent builder.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public static IHostedAgentBuilder AddAIAgentFromA2A(
        this IHostApplicationBuilder builder,
        string name,
        Uri a2aServiceUri,
        Action<IServiceProvider, HttpClient> configureHttpClient)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(a2aServiceUri);
        ArgumentNullException.ThrowIfNull(configureHttpClient);

        // Register the HttpClient with custom configuration
        builder.Services.AddHttpClient(name, c =>
        {
            c.BaseAddress = a2aServiceUri;
        }).ConfigureHttpClient(configureHttpClient);

        //Force activation of the LocalGuide agent at startup.
        builder.Services.ActivateKeyedSingleton<AIAgent>("LocalGuide");

        // Register the A2A agent using the factory pattern
        return builder.AddAIAgent(name, (sp, agentName) =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(agentName);

            var cardResolver = new A2ACardResolver(a2aServiceUri, httpClient);
            return cardResolver.GetAIAgentAsync().GetAwaiter().GetResult();
        });
    }
}
