using System.Diagnostics;
using Aiursoft.AiurProtocol.Exceptions;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
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

    [TestMethod]
    public async Task FileSigningRejectsUnauthenticatedAndOversizedRequests()
    {
        var path = $"{ApiPath}/{Guid.NewGuid():D}/sign-file";
        using var anonymousBody = new MultipartContent("mixed");
        using var anonymous = await Http.PostAsync(path, anonymousBody);
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await LoginAsAdmin();
        var token = await IssueApiKey();
        using var excessive = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent([])
        };
        excessive.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        excessive.Headers.ExpectContinue = true;
        excessive.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/mixed; boundary=parts");
        excessive.Content.Headers.ContentLength = SignFileRequest.MaxRequestBytes + 1L;
        using var oversized = await Http.SendAsync(excessive);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    [TestMethod]
    public async Task StreamingEndpointSignsOriginalFileBytesAndRequiresTheKeyPassphrase()
    {
        var programs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (!OperatingSystem.IsLinux() ||
            !programs.Any(directory => File.Exists(Path.Combine(directory, "gpg"))) ||
            !programs.Any(directory => File.Exists(Path.Combine(directory, "gpgv"))) ||
            !NativeLibrary.TryLoad("libargon2.so.1", typeof(CompanySigningTests).Assembly, null, out var library))
            Assert.Inconclusive("End-to-end signing requires Linux, GnuPG, gpgv and libargon2.");
        else
            NativeLibrary.Free(library);

        const string passphrase = "test recovery 密钥口令";
        using var files = new TestFiles();
        var contentPath = Path.Combine(files.Path, "recovery.img");
        var signaturePath = contentPath + ".sig";
        var publicKeyPath = Path.Combine(files.Path, "recovery.gpg");
        var original = RandomNumberGenerator.GetBytes(128 * 1024);
        await File.WriteAllBytesAsync(contentPath, original);

        using var services = new ServiceCollection().AddLogging()
            .AddEsrpSignSdk(Http.BaseAddress!.ToString()).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var sdk = scope.ServiceProvider.GetRequiredService<EsrpSignApiClient>();

        await LoginAsAdmin();
        var token = await IssueApiKey();
        var created = await sdk.CreateKeyAsync(token,
            new CreateSigningKeyRequest { Name = "Recovery testing", Passphrase = passphrase });
        var keyId = created.Key.Id;
        var signed = await sdk.SignFileAsync(token, keyId, passphrase, contentPath);
        Assert.AreEqual(Code.JobDone, signed.Code);
        await File.WriteAllBytesAsync(signaturePath, signed.Signature);
        await File.WriteAllBytesAsync(publicKeyPath, (await sdk.GetPublicKeyAsync(keyId)).PublicKey);
        Assert.AreEqual(0, await VerifyWithGpg(files.Path, publicKeyPath, signaturePath, contentPath));

        original[0] ^= 1;
        await File.WriteAllBytesAsync(contentPath, original);
        Assert.AreNotEqual(0, await VerifyWithGpg(files.Path, publicKeyPath, signaturePath, contentPath));

        var failure = await Assert.ThrowsExactlyAsync<AiurUnexpectedServerResponseException>(() =>
            sdk.SignFileAsync(token, keyId, "different password", contentPath));
        Assert.AreEqual(Code.WrongKey, failure.Response.Code);
    }

    private static async Task<int> VerifyWithGpg(string home, string publicKey, string signature, string data)
    {
        using var verifier = Process.Start(new ProcessStartInfo("gpgv")
        {
            ArgumentList = { "--homedir", home, "--keyring", publicKey, signature, data },
            UseShellExecute = false
        })!;
        await verifier.WaitForExitAsync();
        return verifier.ExitCode;
    }

    private sealed class TestFiles : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("esrp-sign-file-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
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
