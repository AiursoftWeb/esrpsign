using System.Security.Cryptography;
using System.Text;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.AiurProtocol.Server;
using Aiursoft.AiurProtocol.Server.Attributes;
using Aiursoft.EsrpSign.Authorization;
using Aiursoft.EsrpSign.Entities;
using Aiursoft.EsrpSign.SDK.Models;
using Aiursoft.EsrpSign.Services.Authentication;
using Aiursoft.EsrpSign.Services.Signing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Aiursoft.EsrpSign.Controllers.Api;

[Route("api/v1/signing-keys")]
[ApiExceptionHandler(PassthroughRemoteErrors = false, PassthroughAiurServerException = false)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class SigningKeysApiController(CompanySigningService signing, UserManager<User> users) : ControllerBase
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        this.Protocol(Code.ResultShown, "Company signing keys.", await signing.ListAsync(cancellationToken));

    [HttpGet("{id:guid}/public-key")]
    [AllowAnonymous]
    public async Task<IActionResult> PublicKey(Guid id, CancellationToken cancellationToken)
    {
        var publicKey = await signing.GetPublicKeyAsync(id, cancellationToken);
        return publicKey is null
            ? this.Protocol(Code.NotFound, "Signing key not found.")
            : this.Protocol(new PublicKeyResponse
            {
                Code = Code.ResultShown,
                Message = "Public key.",
                PublicKey = publicKey
            });
    }

    [HttpPost]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName,
        Policy = AppPermissionNames.CanCreateCompanySigningKeys)]
    [EnableRateLimiting(CompanySigningService.CryptoLimiter)]
    [RequestSizeLimit(4 * 1024)]
    public async Task<IActionResult> Create([FromBody] CreateSigningKeyRequest? request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request is null || !PrivateKeys.IsValidPassphrase(request.Passphrase))
        {
            return this.Protocol(Code.InvalidInput, "Provide a key name and a passphrase of 12 or more characters, up to 256 UTF-8 bytes.");
        }

        var key = await signing.CreateAsync(request.Name, request.Passphrase, users.GetUserId(User)!, cancellationToken);
        return this.Protocol(new SigningKeyResponse { Code = Code.JobDone, Message = "Company signing key created.", Key = key });
    }

    [HttpPost("{id:guid}/sign")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName,
        Policy = AppPermissionNames.CanSignWithCompanyKeys)]
    [EnableRateLimiting(CompanySigningService.CryptoLimiter)]
    [RequestSizeLimit(100 * 1024)]
    public async Task<IActionResult> Sign(Guid id, [FromBody] SignRequest? request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || request is null || !PrivateKeys.IsValidPassphrase(request.Passphrase))
        {
            return this.Protocol(Code.InvalidInput, "Provide the key passphrase and 1–65,536 bytes of data.");
        }

        using var data = new MemoryStream(request.Data, writable: false);
        return this.Protocol(await SignCore(id, request.Passphrase, data, cancellationToken));
    }

    [HttpPost("{id:guid}/sign-file")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthenticationHandler.SchemeName,
        Policy = AppPermissionNames.CanSignWithCompanyKeys)]
    [EnableRateLimiting(CompanySigningService.CryptoLimiter)]
    [Consumes("multipart/mixed")]
    [RequestSizeLimit(SignFileRequest.MaxRequestBytes)]
    public async Task<IActionResult> SignFile(Guid id, CancellationToken cancellationToken)
    {
        if (Request.ContentLength > SignFileRequest.MaxRequestBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType) ||
            HeaderUtilities.RemoveQuotes(contentType.Boundary).Value is not { Length: > 0 and <= 70 } boundary)
            return this.Protocol(Code.InvalidInput, "Provide a multipart boundary.");

        try
        {
            var reader = new MultipartReader(boundary, Request.Body)
            {
                HeadersLengthLimit = 1024,
                BodyLengthLimit = SignFileRequest.MaxPassphraseBytes
            };
            var passphrasePart = await reader.ReadNextSectionAsync(cancellationToken);
            var passphrase = passphrasePart is not null &&
                MediaTypeHeaderValue.TryParse(passphrasePart.ContentType, out var passphraseType) &&
                passphraseType.MediaType.Equals("text/plain", StringComparison.OrdinalIgnoreCase) &&
                (!passphraseType.Charset.HasValue || passphraseType.Charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
                ? await ReadPassphrase(passphrasePart.Body, cancellationToken)
                : null;
            if (!PrivateKeys.IsValidPassphrase(passphrase))
                return this.Protocol(Code.InvalidInput, "Provide the UTF-8 key passphrase as the first part.");

            reader.BodyLengthLimit = SignFileRequest.MaxFileBytes;
            var file = await reader.ReadNextSectionAsync(cancellationToken);
            if (file?.ContentType?.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase) != true)
                return this.Protocol(Code.InvalidInput, "Provide the file as the second part.");

            var result = await SignCore(id, passphrase!, file.Body, cancellationToken);
            // Only a complete binary part may produce a successful signature.
            if (result is SignResponse &&
                (file.Body.Length == 0 || await reader.ReadNextSectionAsync(cancellationToken) is not null))
                return this.Protocol(Code.InvalidInput, "Provide exactly one non-empty file.");

            return this.Protocol(result);
        }
        catch (InvalidDataException)
        {
            return this.Protocol(Code.InvalidInput, "The signing request is malformed or exceeds the file size limit.");
        }
        catch (DecoderFallbackException)
        {
            return this.Protocol(Code.InvalidInput, "The key passphrase must be UTF-8.");
        }
        catch (BadHttpRequestException ex)
        {
            return StatusCode(ex.StatusCode);
        }
    }

    private static async Task<string> ReadPassphrase(Stream body, CancellationToken cancellationToken)
    {
        var bytes = new byte[SignFileRequest.MaxPassphraseBytes + 1];
        try
        {
            var length = await body.ReadAtLeastAsync(bytes, bytes.Length, false, cancellationToken);
            return StrictUtf8.GetString(bytes.AsSpan(0, length));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private async Task<AiurResponse> SignCore(Guid id, string passphrase, Stream data, CancellationToken cancellationToken)
    {
        try
        {
            var signature = await signing.SignAsync(id, passphrase, data, users.GetUserId(User)!, cancellationToken);
            return signature is null
                ? new AiurResponse { Code = Code.NotFound, Message = "Signing key not found." }
                : new SignResponse { Code = Code.JobDone, Message = "Data signed.", Signature = signature };
        }
        catch (AuthenticationTagMismatchException)
        {
            return new AiurResponse { Code = Code.WrongKey, Message = "The key passphrase is incorrect." };
        }
    }
}
