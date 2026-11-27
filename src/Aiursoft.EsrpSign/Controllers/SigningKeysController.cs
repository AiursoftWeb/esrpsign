using System.Security.Cryptography;
using System.Text;
using Aiursoft.EsrpSign.Authorization;
using Aiursoft.EsrpSign.Entities;
using Aiursoft.EsrpSign.Models.SigningKeysViewModels;
using Aiursoft.EsrpSign.SDK.Models;
using Aiursoft.EsrpSign.Services;
using Aiursoft.EsrpSign.Services.Signing;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace Aiursoft.EsrpSign.Controllers;

public sealed class SigningKeysController(
    CompanySigningService signing,
    UserManager<User> users,
    IAuthorizationService authorization,
    IStringLocalizer<SigningKeysController> localizer) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    [RenderInNavBar(
        NavGroupName = "Features", NavGroupOrder = 1,
        CascadedLinksGroupName = "Signing", CascadedLinksIcon = "key-round", CascadedLinksOrder = 2,
        LinkText = "Company Signing Keys", LinkOrder = 1)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var keys = await signing.ListAsync(cancellationToken);
        return this.StackView(new IndexViewModel
        {
            Keys = keys,
            CanCreate = await HasPermissionAsync(AppPermissionNames.CanCreateCompanySigningKeys),
            CanSign = await HasPermissionAsync(AppPermissionNames.CanSignWithCompanyKeys)
        });
    }

    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var key = await signing.FindAsync(id, cancellationToken);
        if (key is null)
        {
            return NotFound();
        }

        return this.StackView(await BuildDetailsModelAsync(key, keyCreated: TempData["KeyCreated"] is true));
    }

    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanSignWithCompanyKeys)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(CompanySigningService.CryptoLimiter)]
    [RequestSizeLimit(12 * 1024)]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> VerifyPassphrase(Guid id, VerifyPassphraseInputModel input,
        CancellationToken cancellationToken)
    {
        var key = await signing.FindAsync(id, cancellationToken);
        if (key is null)
        {
            return NotFound();
        }

        ValidatePassphrase(input.Passphrase);
        var model = await BuildDetailsModelAsync(key);
        if (!ModelState.IsValid)
        {
            return this.StackView(model, nameof(Details));
        }

        var outcome = await signing.VerifyPassphraseAsync(id, input.Passphrase, users.GetUserId(User)!, cancellationToken);
        if (outcome == PassphraseVerificationOutcome.NotFound)
        {
            return NotFound();
        }

        model.VerificationOutcome = outcome;
        return this.StackView(model, nameof(Details));
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> PublicKey(Guid id, CancellationToken cancellationToken)
    {
        var key = await signing.GetPublicKeyAsync(id, cancellationToken);
        return key is null ? NotFound() : File(key, "application/pgp-keys", $"esrpsign-{id:D}.gpg");
    }

    [HttpGet]
    [Authorize(Policy = AppPermissionNames.CanCreateCompanySigningKeys)]
    public IActionResult Create() => this.StackView(new CreateViewModel());

    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanCreateCompanySigningKeys)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(CompanySigningService.CryptoLimiter)]
    [RequestSizeLimit(12 * 1024)]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Create(CreateViewModel model, CancellationToken cancellationToken)
    {
        ValidatePassphrase(model.Passphrase);
        if (!ModelState.IsValid)
        {
            model.Passphrase = string.Empty;
            model.ConfirmPassphrase = string.Empty;
            return this.StackView(model);
        }

        var created = await signing.CreateAsync(model.Name, model.Passphrase, users.GetUserId(User)!, cancellationToken);
        TempData["KeyCreated"] = true;
        return RedirectToAction(nameof(Details), new { id = created.Id });
    }

    [HttpGet]
    [Authorize(Policy = AppPermissionNames.CanSignWithCompanyKeys)]
    public async Task<IActionResult> Sign(Guid id, string? mode, CancellationToken cancellationToken)
    {
        var key = await signing.FindAsync(id, cancellationToken);
        if (key is null)
        {
            return NotFound();
        }

        var normalizedMode = string.Equals(mode, "file", StringComparison.OrdinalIgnoreCase) ? "file" : "text";
        return this.StackView(new SignViewModel { Key = key, Mode = normalizedMode });
    }

    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanSignWithCompanyKeys)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(CompanySigningService.CryptoLimiter)]
    [RequestSizeLimit(256 * 1024)]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Sign(Guid id, SignViewModel model, CancellationToken cancellationToken)
    {
        model.Key = await signing.FindAsync(id, cancellationToken);
        model.Mode = "text";
        if (model.Key is null)
        {
            return NotFound();
        }

        ValidatePassphrase(model.Passphrase);
        var data = Encoding.UTF8.GetBytes(model.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (data.Length is < 1 or > SignRequest.MaxDataBytes)
        {
            ModelState.AddModelError(nameof(model.Text), localizer["Enter between 1 and 65,536 bytes of text."]);
        }

        if (!ModelState.IsValid)
        {
            model.Passphrase = string.Empty;
            return this.StackView(model);
        }

        try
        {
            var signature = await signing.SignAsync(id, model.Passphrase, data, users.GetUserId(User)!, cancellationToken);
            return signature is null ? NotFound() : File(signature, "application/pgp-signature", "message.txt.sig");
        }
        catch (AuthenticationTagMismatchException)
        {
            model.Passphrase = string.Empty;
            ModelState.AddModelError(nameof(model.Passphrase), localizer["The key passphrase is incorrect."]);
            return this.StackView(model);
        }
    }

    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanSignWithCompanyKeys)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(CompanySigningService.CryptoLimiter)]
    [RequestSizeLimit(SignFileRequest.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = SignFileRequest.MaxRequestBytes)]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> SignFile(Guid id, SignFileInputModel input, CancellationToken cancellationToken)
    {
        var key = await signing.FindAsync(id, cancellationToken);
        if (key is null)
        {
            return NotFound();
        }

        var model = new SignViewModel { Key = key, Mode = "file" };
        ValidatePassphrase(input.Passphrase);
        if (input.File is null || input.File.Length == 0)
        {
            ModelState.AddModelError(nameof(input.File), localizer["Select a non-empty file to sign."]);
        }
        else if (input.File.Length > SignFileRequest.MaxFileBytes)
        {
            ModelState.AddModelError(nameof(input.File), localizer["The file must not exceed 64 MiB."]);
        }

        if (!ModelState.IsValid)
        {
            return this.StackView(model, nameof(Sign));
        }

        try
        {
            await using var stream = input.File!.OpenReadStream();
            var signature = await signing.SignAsync(id, input.Passphrase, stream, users.GetUserId(User)!, cancellationToken);
            if (signature is null)
            {
                return NotFound();
            }

            var downloadName = BuildSignatureFileName(input.File.FileName);
            return File(signature, "application/pgp-signature", downloadName);
        }
        catch (AuthenticationTagMismatchException)
        {
            ModelState.AddModelError(nameof(input.Passphrase), localizer["The key passphrase is incorrect."]);
            return this.StackView(model, nameof(Sign));
        }
    }

    private async Task<DetailsViewModel> BuildDetailsModelAsync(SigningKeyDto key, bool keyCreated = false) =>
        new()
        {
            Key = key,
            CanSign = await HasPermissionAsync(AppPermissionNames.CanSignWithCompanyKeys),
            KeyCreated = keyCreated,
            PublicOrigin = $"{Request.Scheme}://{Request.Host}"
        };

    private async Task<bool> HasPermissionAsync(string policy) =>
        User.Identity?.IsAuthenticated == true &&
        (await authorization.AuthorizeAsync(User, policy)).Succeeded;

    private void ValidatePassphrase(string passphrase)
    {
        if (!PrivateKeys.IsValidPassphrase(passphrase))
        {
            ModelState.AddModelError(nameof(CreateViewModel.Passphrase),
                localizer["Use at least 12 characters and no more than 256 UTF-8 bytes."]);
        }
    }

    private static string BuildSignatureFileName(string originalFileName)
    {
        var baseName = Path.GetFileName(originalFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return "file.sig";
        }

        var safe = new StringBuilder(baseName.Length);
        foreach (var ch in baseName)
        {
            if (char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')
            {
                safe.Append(ch);
            }
            else
            {
                safe.Append('_');
            }
        }

        var sanitized = safe.ToString().Trim('.');
        return string.IsNullOrEmpty(sanitized) ? "file.sig" : $"{sanitized}.sig";
    }
}
