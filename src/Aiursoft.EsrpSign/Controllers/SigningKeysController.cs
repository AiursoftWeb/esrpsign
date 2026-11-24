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
    IStringLocalizer<SigningKeysController> localizer) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    [RenderInNavBar(
        NavGroupName = "Features", NavGroupOrder = 1,
        CascadedLinksGroupName = "Signing", CascadedLinksIcon = "key-round", CascadedLinksOrder = 2,
        LinkText = "Company Signing Keys", LinkOrder = 1)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        this.StackView(new IndexViewModel { Keys = await signing.ListAsync(cancellationToken) });

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
            return this.StackView(model);
        }

        await signing.CreateAsync(model.Name, model.Passphrase, users.GetUserId(User)!, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = AppPermissionNames.CanSignWithCompanyKeys)]
    public async Task<IActionResult> Sign(Guid id, CancellationToken cancellationToken)
    {
        var key = await signing.FindAsync(id, cancellationToken);
        return key is null ? NotFound() : this.StackView(new SignViewModel { Key = key });
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

    private void ValidatePassphrase(string passphrase)
    {
        if (!PrivateKeys.IsValidPassphrase(passphrase))
        {
            ModelState.AddModelError(nameof(CreateViewModel.Passphrase),
                localizer["Use at least 12 characters and no more than 256 UTF-8 bytes."]);
        }
    }
}
