using Aiursoft.EsrpSign.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.EsrpSign.Authorization;

public sealed record CurrentSigningPermission(string Permission) : IAuthorizationRequirement;

public sealed class CurrentSigningPermissionHandler(
    UserManager<User> users,
    IUserClaimsPrincipalFactory<User> principalFactory) : AuthorizationHandler<CurrentSigningPermission>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        CurrentSigningPermission requirement)
    {
        var user = await users.GetUserAsync(context.User);
        if (user is not null && !await users.IsLockedOutAsync(user))
        {
            var current = await principalFactory.CreateAsync(user);
            if (current.HasClaim(AppPermissions.Type, requirement.Permission))
            {
                context.Succeed(requirement);
            }
        }
    }
}
