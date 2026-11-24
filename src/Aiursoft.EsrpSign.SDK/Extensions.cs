using Aiursoft.AiurProtocol;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.EsrpSign.SDK;

public static class Extensions
{
    public static IServiceCollection AddEsrpSignSdk(this IServiceCollection services, string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        services.AddAiurProtocolClient();
        services.Configure<EsrpSignApiOptions>(options => options.Endpoint = endpoint);
        services.AddScoped<EsrpSignApiClient>();
        return services;
    }
}
