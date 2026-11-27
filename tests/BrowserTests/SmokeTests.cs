using static Microsoft.Playwright.Assertions;

namespace Aiursoft.EsrpSign.Tests.BrowserTests;

[TestClass]
public class SmokeTests : BrowserTestBase
{
    [TestMethod]
    public async Task HomeAndDashboardRenderWithLocalAssets()
    {
        await NavigateAsync("/");
        await Expect(Page.Locator("#main-slogan")).ToBeVisibleAsync();

        const string assetRoot = "/node_modules/@aiursoft/uistack/dist/";
        var css = Page.WaitForResponseAsync(response => new Uri(response.Url).AbsolutePath == assetRoot + "css/app.css");
        var js = Page.WaitForResponseAsync(response => new Uri(response.Url).AbsolutePath == assetRoot + "js/app.js");
        await NavigateAsync("/Dashboard/Index");
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Heading, new() { Name = "Aiursoft ESRP Sign", Level = 1 })).ToBeVisibleAsync();
        foreach (var response in await Task.WhenAll(css, js))
        {
            Assert.IsTrue(response.Ok, $"{response.Url}: HTTP {response.Status}");
        }
    }

    [TestMethod]
    public async Task LocalLoginGrantsAccessToProtectedPage()
    {
        await NavigateAsync("/Manage/Index");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/Login\\?", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        var form = Page.Locator("form").Filter(new() { Has = Page.Locator("#EmailOrUserName") });
        await form.Locator("#EmailOrUserName").FillAsync("admin@default.com");
        await form.Locator("#Password").FillAsync("Admin@123456!");
        await form.Locator("button[type=submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Manage/Index(?:\\?.*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        await NavigateAsync("/Manage/Index");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Manage/Index$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    [TestMethod]
    public async Task SigningKeysAndCreatePassphraseToggleWorkInBrowser()
    {
        var uiScript = Page.WaitForResponseAsync(response => new Uri(response.Url).AbsolutePath == "/scripts/esrp-ui.js");
        await NavigateAsync("/SigningKeys");
        var uiScriptResponse = await uiScript;
        Assert.IsTrue(uiScriptResponse.Ok, $"/scripts/esrp-ui.js: HTTP {uiScriptResponse.Status}");
        await Expect(Page.GetByRole(Microsoft.Playwright.AriaRole.Heading, new() { Name = "Company Signing Keys", Level = 1 })).ToBeVisibleAsync();

        await NavigateAsync("/Account/Login");
        var loginForm = Page.Locator("form").Filter(new() { Has = Page.Locator("#EmailOrUserName") });
        await loginForm.Locator("#EmailOrUserName").FillAsync("admin@default.com");
        await loginForm.Locator("#Password").FillAsync("Admin@123456!");
        await loginForm.Locator("button[type=submit]").ClickAsync();
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        await NavigateAsync("/SigningKeys/Create");
        var passphraseInput = Page.Locator("#create-passphrase-input");
        var toggleButton = Page.Locator("[data-toggle-password='create-passphrase-input']");
        await Expect(passphraseInput).ToHaveAttributeAsync("type", "password");
        await toggleButton.ClickAsync();
        await Expect(passphraseInput).ToHaveAttributeAsync("type", "text");
        await toggleButton.ClickAsync();
        await Expect(passphraseInput).ToHaveAttributeAsync("type", "password");
    }

    private async Task NavigateAsync(string path)
    {
        var response = await Page.GotoAsync(path);
        Assert.IsNotNull(response);
        Assert.IsTrue(response.Ok, $"{path}: HTTP {response.Status}");
    }
}
