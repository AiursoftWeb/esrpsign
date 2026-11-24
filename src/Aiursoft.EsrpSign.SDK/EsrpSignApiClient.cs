using Aiursoft.AiurProtocol;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Services;
using Aiursoft.EsrpSign.SDK.Models;
using Microsoft.Extensions.Options;

namespace Aiursoft.EsrpSign.SDK;

public sealed class EsrpSignApiClient(AiurProtocolClient http, IOptions<EsrpSignApiOptions> options)
{
    private AiurApiEndpoint Endpoint(string resource = "") => new(
        options.Value.Endpoint.TrimEnd('/'), "/api/v1/signing-keys" + resource, new { });

    public Task<AiurCollection<SigningKeyDto>> ListKeysAsync() =>
        http.Get<AiurCollection<SigningKeyDto>>(Endpoint());

    public Task<PublicKeyResponse> GetPublicKeyAsync(Guid id) =>
        http.Get<PublicKeyResponse>(Endpoint($"/{id:D}/public-key"));

    public Task<SigningKeyResponse> CreateKeyAsync(string apiKey, CreateSigningKeyRequest request) =>
        http.Post<SigningKeyResponse>(Endpoint(), new AiurApiPayload(request), BodyFormat.HttpJsonBody,
            autoRetry: false, headers: AuthorizationHeaders(apiKey));

    public Task<SignResponse> SignAsync(string apiKey, Guid id, SignRequest request) =>
        http.Post<SignResponse>(Endpoint($"/{id:D}/sign"), new AiurApiPayload(request),
            BodyFormat.HttpJsonBody, autoRetry: false, headers: AuthorizationHeaders(apiKey));

    private static Dictionary<string, string> AuthorizationHeaders(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {apiKey}" };
    }
}
