using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.EsrpSign.Authorization;
using Aiursoft.EsrpSign.Entities;
using Aiursoft.EsrpSign.SDK;
using Aiursoft.EsrpSign.SDK.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.EsrpSign.Tests.IntegrationTests;

[TestClass]
public sealed class CompanySigningTests : TestBase
{
    private const string ApiPath = "/api/v1/signing-keys";

    [TestMethod]
    public async Task PublicCatalogAndDownloadsAreAnonymousAndOnlyExposePublicMaterial()
    {
        var publicKey = new byte[] { 7, 8, 9 };
        var key = new CompanySigningKey
        {
            Id = Guid.NewGuid(),
            Name = "Release verification",
            Fingerprint = new string('F', 40),
            CreatedAt = DateTime.UtcNow,
            PublicKey = publicKey,
            PrivateEnvelope = [1, 2, 3]
        };
        await using (var scope = Server!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.CompanySigningKeys.Add(key);
            await db.SaveChangesAsync();
        }

        using var services = new ServiceCollection().AddLogging()
            .AddEsrpSignSdk(Http.BaseAddress!.ToString()).BuildServiceProvider();
        await using var sdkScope = services.CreateAsyncScope();
        var sdk = sdkScope.ServiceProvider.GetRequiredService<EsrpSignApiClient>();
        var listing = await sdk.ListKeysAsync();
        Assert.AreEqual(Code.ResultShown, listing.Code);
        Assert.AreEqual(key.Id, listing.Items!.Single().Id);
        var response = await sdk.GetPublicKeyAsync(key.Id);
        CollectionAssert.AreEqual(publicKey, response.PublicKey);

        using var download = await Http.GetAsync($"/SigningKeys/PublicKey/{key.Id:D}");
        Assert.AreEqual(HttpStatusCode.OK, download.StatusCode);
        Assert.AreEqual("application/pgp-keys", download.Content.Headers.ContentType!.MediaType);
        CollectionAssert.AreEqual(publicKey, await download.Content.ReadAsByteArrayAsync());
        var json = await Http.GetStringAsync(ApiPath);
        Assert.DoesNotContain(nameof(CompanySigningKey.PrivateEnvelope), json, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task PersonalApiKeyInheritsCurrentPermissionsAndRevocationIsImmediate()
    {
        var (email, _) = await RegisterAndLoginAsync();
        var token = await IssueApiKey();
        var signPath = $"{ApiPath}/{Guid.NewGuid():D}/sign";
        var sign = new SignRequest { Data = [1], Passphrase = "long test password" };
        var malformedCreate = new CreateSigningKeyRequest();

        using (var loginOnly = await Http.PostAsJsonAsync(signPath, sign))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, loginOnly.StatusCode);
        }
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using (var denied = await Http.PostAsJsonAsync(signPath, sign))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        await using var scope = Server!.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var owner = (await users.FindByEmailAsync(email))!;
        var role = new IdentityRole("Release signers");
        Assert.IsTrue((await roles.CreateAsync(role)).Succeeded);
        Assert.IsTrue((await users.AddToRoleAsync(owner, role.Name!)).Succeeded);

        var signClaim = new Claim(AppPermissions.Type, AppPermissionNames.CanSignWithCompanyKeys);
        var createClaim = new Claim(AppPermissions.Type, AppPermissionNames.CanCreateCompanySigningKeys);
        Assert.IsTrue((await roles.AddClaimAsync(role, signClaim)).Succeeded);
        using (var signAllowed = await Http.PostAsJsonAsync(signPath, sign))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, signAllowed.StatusCode);
        }
        using (var createDenied = await Http.PostAsJsonAsync(ApiPath, malformedCreate))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, createDenied.StatusCode);
        }

        Assert.IsTrue((await roles.RemoveClaimAsync(role, signClaim)).Succeeded);
        Assert.IsTrue((await roles.AddClaimAsync(role, createClaim)).Succeeded);
        using (var signDenied = await Http.PostAsJsonAsync(signPath, sign))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, signDenied.StatusCode);
        }
        using (var createAllowed = await Http.PostAsJsonAsync(ApiPath, malformedCreate))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, createAllowed.StatusCode);
        }

        var key = await db.PersonalApiKeys.SingleAsync();
        Assert.AreEqual(owner.Id, key.UserId);
        Assert.AreEqual(Hash(token), key.KeyHash);
        Assert.AreNotEqual(token, key.KeyHash);
        using (var laterPage = await Http.GetAsync("/ApiKeys"))
        {
            Assert.DoesNotContain(token, await laterPage.Content.ReadAsStringAsync());
        }

        using (var revoked = await PostForm($"/ApiKeys/Revoke/{key.Id}", [], "/ApiKeys"))
        {
            Assert.AreEqual(HttpStatusCode.Found, revoked.StatusCode);
        }
        using var after = await Http.PostAsJsonAsync(ApiPath, malformedCreate);
        Assert.AreEqual(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    private async Task<string> IssueApiKey()
    {
        using var issued = await PostForm("/ApiKeys/Create", new Dictionary<string, string>
        {
            ["Name"] = "Integration key",
            ["ExpirationDays"] = "7"
        });
        Assert.AreEqual(HttpStatusCode.Found, issued.StatusCode);
        using var shown = await Http.GetAsync("/ApiKeys");
        shown.EnsureSuccessStatusCode();
        var match = Regex.Match(await shown.Content.ReadAsStringAsync(), @"esrp_[A-F0-9]{64}");
        Assert.IsTrue(match.Success);
        return match.Value;
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
