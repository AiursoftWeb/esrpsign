using Aiursoft.EsrpSign.Authorization;
using Aiursoft.EsrpSign.Entities;
using Aiursoft.EsrpSign.Models.DashboardViewModels;
using Aiursoft.EsrpSign.Services;
using Aiursoft.EsrpSign.Services.Signing;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.EsrpSign.Controllers;

[LimitPerMin]
public class DashboardController(
    CompanySigningService signing,
    TemplateDbContext db,
    UserManager<User> users,
    IAuthorizationService authorization) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "home",
        CascadedLinksOrder = 1,
        LinkText = "Index",
        LinkOrder = 1)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var allKeys = await signing.ListAsync(cancellationToken);
        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        var canCreate = isAuthenticated &&
            (await authorization.AuthorizeAsync(User, AppPermissionNames.CanCreateCompanySigningKeys)).Succeeded;
        var canSign = isAuthenticated &&
            (await authorization.AuthorizeAsync(User, AppPermissionNames.CanSignWithCompanyKeys)).Succeeded;
        var activeApiKeys = 0;
        if (isAuthenticated)
        {
            var userId = users.GetUserId(User);
            if (!string.IsNullOrEmpty(userId))
            {
                var now = DateTime.UtcNow;
                activeApiKeys = await db.PersonalApiKeys.AsNoTracking()
                    .CountAsync(k => k.UserId == userId && k.ExpirationTime > now, cancellationToken);
            }
        }

        return this.StackView(new IndexViewModel
        {
            RecentKeys = allKeys.Take(5).ToList(),
            TotalSigningKeys = allKeys.Count,
            ActiveApiKeys = activeApiKeys,
            CanCreateKeys = canCreate,
            CanSignKeys = canSign
        });
    }
}
