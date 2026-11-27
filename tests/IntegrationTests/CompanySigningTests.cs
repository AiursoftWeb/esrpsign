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
using Aiursoft.EsrpSign.Services.Signing;
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
    public async Task DetailsPageShowsPublicMetadataRespectsPermissionsAndHandlesCorruptedEnvelope()
    {
        var fingerprint = "408648516EFF334197B121EA3ADA744B1059B23F";
        var key = new CompanySigningKey
        {
            Id = Guid.NewGuid(),
            Name = "AnduinOS Release Key",
            Fingerprint = fingerprint,
            CreatedAt = DateTime.UtcNow,
            PublicKey = [1, 2, 3, 4],
            PrivateEnvelope = [9, 8, 7, 6]
        };
        await using (var scope = Server!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.CompanySigningKeys.Add(key);
            await db.SaveChangesAsync();
        }

        using (var missing = await Http.GetAsync($"/SigningKeys/Details/{Guid.NewGuid():D}"))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        }

        // Anonymous visitor sees public metadata, formatted fingerprint, and no passphrase test input.
        using (var anonDetails = await Http.GetAsync($"/SigningKeys/Details/{key.Id:D}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, anonDetails.StatusCode);
            var html = await anonDetails.Content.ReadAsStringAsync();
            Assert.Contains(key.Id.ToString("D"), html, StringComparison.Ordinal);
            Assert.Contains(CompanySigningService.FormatFingerprint(fingerprint), html, StringComparison.Ordinal);
            Assert.Contains($"/SigningKeys/PublicKey/{key.Id:D}", html, StringComparison.Ordinal);
            Assert.DoesNotContain("verify-passphrase-input", html, StringComparison.Ordinal);
        }

        using (var anonVerify = await PostForm($"/SigningKeys/VerifyPassphrase/{key.Id:D}", new Dictionary<string, string>
        {
            ["Passphrase"] = "some-secret-passphrase-123"
        }, "/Account/Login"))
        {
            AssertRedirect(anonVerify, "/Account/Login", exact: false);
        }

        // Regular authenticated user without CanSignWithCompanyKeys cannot verify passphrases.
        await RegisterAndLoginAsync();
        using (var userDetails = await Http.GetAsync($"/SigningKeys/Details/{key.Id:D}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, userDetails.StatusCode);
            var html = await userDetails.Content.ReadAsStringAsync();
            Assert.DoesNotContain("verify-passphrase-input", html, StringComparison.Ordinal);
        }

        using (var userVerify = await PostForm($"/SigningKeys/VerifyPassphrase/{key.Id:D}", new Dictionary<string, string>
        {
            ["Passphrase"] = "some-secret-passphrase-123"
        }, "/ApiKeys/Create"))
        {
            AssertRedirect(userVerify, "/Error/Code403", exact: false);
        }

        // Admin sees the passphrase test form and gets KeyRingUnavailable on corrupted envelope without echoing passphrase.
        await LoginAsAdmin();
        using (var adminDetails = await Http.GetAsync($"/SigningKeys/Details/{key.Id:D}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, adminDetails.StatusCode);
            var html = await adminDetails.Content.ReadAsStringAsync();
            Assert.Contains("verify-passphrase-input", html, StringComparison.Ordinal);
        }

        const string probePassphrase = "corrupted-envelope-probe-987654";
        using (var verifyCorrupted = await PostForm($"/SigningKeys/VerifyPassphrase/{key.Id:D}", new Dictionary<string, string>
        {
            ["Passphrase"] = probePassphrase
        }, $"/SigningKeys/Details/{key.Id:D}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, verifyCorrupted.StatusCode);
            var html = await verifyCorrupted.Content.ReadAsStringAsync();
            Assert.Contains("Key ring mismatch.", html, StringComparison.Ordinal);
            Assert.DoesNotContain(probePassphrase, html, StringComparison.Ordinal);
        }

        using (var verifyMissing = await PostForm($"/SigningKeys/VerifyPassphrase/{Guid.NewGuid():D}", new Dictionary<string, string>
        {
            ["Passphrase"] = probePassphrase
        }, $"/SigningKeys/Details/{key.Id:D}"))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, verifyMissing.StatusCode);
        }
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
        using var excessive = new HttpRequestMessage(HttpMethod.Post, path);
        using var empty = new ByteArrayContent([]);
        excessive.Content = empty;
        excessive.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        excessive.Headers.ExpectContinue = true;
        excessive.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/mixed; boundary=parts");
        excessive.Content.Headers.ContentLength = SignFileRequest.MaxRequestBytes + 1L;
        using var oversized = await Http.SendAsync(excessive);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
    }

    [TestMethod]
    public async Task CreateVerifyPassphraseAndBrowserFileSigningWorkEndToEnd()
    {
        RequireLinuxCryptoRuntime();

        const string passphrase = "argon2-ui-passphrase-验证口令-2026";
        const string wrongPassphrase = "wrong-passphrase-should-never-echo";
        await LoginAsAdmin();

        // ConfirmPassphrase mismatch is rejected without creating a key or echoing passphrases.
        using (var mismatched = await PostForm("/SigningKeys/Create", new Dictionary<string, string>
        {
            ["Name"] = "Mismatched key",
            ["Passphrase"] = passphrase,
            ["ConfirmPassphrase"] = wrongPassphrase
        }))
        {
            Assert.AreEqual(HttpStatusCode.OK, mismatched.StatusCode);
            var html = await mismatched.Content.ReadAsStringAsync();
            Assert.Contains("does not match", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(passphrase, html, StringComparison.Ordinal);
            Assert.DoesNotContain(wrongPassphrase, html, StringComparison.Ordinal);
        }

        // Valid create redirects to Details page with KeyCreated banner.
        Guid keyId;
        using (var created = await PostForm("/SigningKeys/Create", new Dictionary<string, string>
        {
            ["Name"] = "UI Verified Release Key",
            ["Passphrase"] = passphrase,
            ["ConfirmPassphrase"] = passphrase
        }))
        {
            AssertRedirect(created, "/SigningKeys/Details/", exact: false);
            var location = created.Headers.Location!.OriginalString;
            var idSegment = location.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
            Assert.IsTrue(Guid.TryParse(idSegment, out keyId), $"Expected GUID in redirect {location}");
        }

        using (var detailsAfterCreate = await Http.GetAsync($"/SigningKeys/Details/{keyId:D}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, detailsAfterCreate.StatusCode);
            var html = await detailsAfterCreate.Content.ReadAsStringAsync();
            Assert.Contains("Signing key created.", html, StringComparison.Ordinal);
            Assert.Contains("UI Verified Release Key", html, StringComparison.Ordinal);
        }

        // Valid passphrase verification succeeds without echoing passphrase.
        using (var validVerify = await PostForm($"/SigningKeys/VerifyPassphrase/{keyId:D}", new Dictionary<string, string>
        {
            ["Passphrase"] = passphrase
        }, $"/SigningKeys/Details/{keyId:D}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, validVerify.StatusCode);
            var html = await validVerify.Content.ReadAsStringAsync();
            Assert.Contains("Passphrase verified.", html, StringComparison.Ordinal);
            Assert.DoesNotContain(passphrase, html, StringComparison.Ordinal);
        }

        // Invalid passphrase verification reports failure without echoing passphrase.
        using (var invalidVerify = await PostForm($"/SigningKeys/VerifyPassphrase/{keyId:D}", new Dictionary<string, string>
        {
            ["Passphrase"] = wrongPassphrase
        }, $"/SigningKeys/Details/{keyId:D}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, invalidVerify.StatusCode);
            var html = await invalidVerify.Content.ReadAsStringAsync();
            Assert.Contains("Incorrect passphrase.", html, StringComparison.Ordinal);
            Assert.DoesNotContain(wrongPassphrase, html, StringComparison.Ordinal);
        }

        // Browser file signing endpoint produces a gpgv-valid detached signature.
        using var files = new TestFiles();
        var artifactBytes = RandomNumberGenerator.GetBytes(32 * 1024);
        var artifactPath = Path.Combine(files.Path, "release.tar.gz");
        var signaturePath = Path.Combine(files.Path, "release.tar.gz.sig");
        var publicKeyPath = Path.Combine(files.Path, "pub.gpg");
        await File.WriteAllBytesAsync(artifactPath, artifactBytes);

        var csrfToken = await GetAntiCsrfToken($"/SigningKeys/Sign/{keyId:D}?mode=file");
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent(csrfToken), "__RequestVerificationToken");
            form.Add(new StringContent(passphrase), "Passphrase");
            form.Add(new ByteArrayContent(artifactBytes), "File", "release.tar.gz");
            using var signed = await Http.PostAsync($"/SigningKeys/SignFile/{keyId:D}", form);
            Assert.AreEqual(HttpStatusCode.OK, signed.StatusCode);
            Assert.AreEqual("application/pgp-signature", signed.Content.Headers.ContentType!.MediaType);
            Assert.AreEqual("release.tar.gz.sig", signed.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            await File.WriteAllBytesAsync(signaturePath, await signed.Content.ReadAsByteArrayAsync());
        }

        var publicKeyBytes = await Http.GetByteArrayAsync($"/SigningKeys/PublicKey/{keyId:D}");
        await File.WriteAllBytesAsync(publicKeyPath, publicKeyBytes);
        Assert.AreEqual(0, await VerifyWithGpg(files.Path, publicKeyPath, signaturePath, artifactPath));

        // Wrong passphrase on SignFile returns error without echoing passphrase.
        using (var wrongForm = new MultipartFormDataContent())
        {
            wrongForm.Add(new StringContent(csrfToken), "__RequestVerificationToken");
            wrongForm.Add(new StringContent(wrongPassphrase), "Passphrase");
            wrongForm.Add(new ByteArrayContent(artifactBytes), "File", "release.tar.gz");
            using var wrongResult = await Http.PostAsync($"/SigningKeys/SignFile/{keyId:D}", wrongForm);
            Assert.AreEqual(HttpStatusCode.OK, wrongResult.StatusCode);
            var html = await wrongResult.Content.ReadAsStringAsync();
            Assert.Contains("The key passphrase is incorrect.", html, StringComparison.Ordinal);
            Assert.DoesNotContain(wrongPassphrase, html, StringComparison.Ordinal);
        }

        // Empty file on SignFile returns validation error.
        using (var emptyForm = new MultipartFormDataContent())
        {
            emptyForm.Add(new StringContent(csrfToken), "__RequestVerificationToken");
            emptyForm.Add(new StringContent(passphrase), "Passphrase");
            emptyForm.Add(new ByteArrayContent([]), "File", "empty.bin");
            using var emptyResult = await Http.PostAsync($"/SigningKeys/SignFile/{keyId:D}", emptyForm);
            Assert.AreEqual(HttpStatusCode.OK, emptyResult.StatusCode);
            var html = await emptyResult.Content.ReadAsStringAsync();
            Assert.Contains("Select a non-empty file to sign.", html, StringComparison.Ordinal);
        }
    }

    [TestMethod]
    public async Task StreamingEndpointSignsOriginalFileBytesAndRequiresTheKeyPassphrase()
    {
        RequireLinuxCryptoRuntime();

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

    private static void RequireLinuxCryptoRuntime()
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
