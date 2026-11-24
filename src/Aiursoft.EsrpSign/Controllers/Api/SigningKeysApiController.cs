using System.Security.Cryptography;
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

namespace Aiursoft.EsrpSign.Controllers.Api;

[Route("api/v1/signing-keys")]
[ApiExceptionHandler(PassthroughRemoteErrors = false, PassthroughAiurServerException = false)]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class SigningKeysApiController(CompanySigningService signing, UserManager<User> users) : ControllerBase
{
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

        try
        {
            var signature = await signing.SignAsync(id, request.Passphrase, request.Data, users.GetUserId(User)!, cancellationToken);
            return signature is null
                ? this.Protocol(Code.NotFound, "Signing key not found.")
                : this.Protocol(new SignResponse { Code = Code.JobDone, Message = "Data signed.", Signature = signature });
        }
        catch (AuthenticationTagMismatchException)
        {
            return this.Protocol(Code.WrongKey, "The key passphrase is incorrect.");
        }
    }
}
