using System.Net.Http.Headers;
using System.Text;
using Aiursoft.AiurProtocol.Exceptions;
using Newtonsoft.Json;
using Aiursoft.AiurProtocol;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Services;
using Aiursoft.EsrpSign.SDK.Models;
using Microsoft.Extensions.Options;

namespace Aiursoft.EsrpSign.SDK;

public sealed class EsrpSignApiClient(AiurProtocolClient http, IOptions<EsrpSignApiOptions> options)
{
    private static readonly HttpClient FileClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromMinutes(4)
    };

    private AiurApiEndpoint Endpoint(string resource = "", object? param = null) => new(
        options.Value.Endpoint.TrimEnd('/'), "/api/v1/signing-keys" + resource, param ?? new { });

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

    public Task<SignResponse> SignFileAsync(string apiKey, Guid id, string passphrase, string filePath,
        CancellationToken cancellationToken = default) =>
        SignFileAsync(apiKey, id, passphrase, filePath, SignatureFormat.DetachedBinary, cancellationToken);

    public async Task<SignResponse> SignFileAsync(string apiKey, Guid id, string passphrase, string filePath,
        SignatureFormat format, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported signature format.");
        if (passphrase.Length < 12 || Encoding.UTF8.GetByteCount(passphrase) > SignFileRequest.MaxPassphraseBytes)
            throw new ArgumentException("Provide a passphrase of at least 12 characters and at most 256 UTF-8 bytes.",
                nameof(passphrase));

        var uri = new Uri(Endpoint($"/{id:D}/sign-file",
            format == SignatureFormat.DetachedBinary ? null : new { format = format.ToString() }).ToString());
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            throw new InvalidOperationException("File signing requires an HTTPS endpoint.");

        await using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length is < 1 or > SignFileRequest.MaxFileBytes)
            throw new ArgumentException("The file must be between 1 byte and 64 MiB.", nameof(filePath));

        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        var upload = new MultipartContent("mixed");
        request.Content = upload;
        upload.Add(new StringContent(passphrase, Encoding.UTF8, "text/plain"));
        var file = new StreamContent(input);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        upload.Add(file);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.ExpectContinue = true;

        using var response = await FileClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var result = JsonConvert.DeserializeObject<AiurResponse>(json, ProtocolSettings.JsonSettings);
            if (result?.Code == Code.JobDone && response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<SignResponse>(json, ProtocolSettings.JsonSettings)!;
            if (result?.Code == Code.InvalidInput)
                throw new AiurBadApiInputException(
                    JsonConvert.DeserializeObject<AiurCollection<string>>(json, ProtocolSettings.JsonSettings)!);
            if (result is not null)
                throw new AiurUnexpectedServerResponseException(result);
        }

        throw new AiurHttpResponseException(response.StatusCode,
            $"Unexpected file signing response (HTTP {(int)response.StatusCode}).");
    }

    private static Dictionary<string, string> AuthorizationHeaders(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return new Dictionary<string, string> { ["Authorization"] = $"Bearer {apiKey}" };
    }
}
