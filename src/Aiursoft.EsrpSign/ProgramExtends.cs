using Aiursoft.CSTools.Tools;
using Aiursoft.EsrpSign.Authorization;
using Aiursoft.EsrpSign.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Aiursoft.EsrpSign.Services;
using Aiursoft.EsrpSign.Services.FileStorage;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.EsrpSign;

[ExcludeFromCodeCoverage]
public static class ProgramExtends
{
    [ExcludeFromCodeCoverage]
    private static async Task<bool> ShouldSeedAsync(TemplateDbContext dbContext)
    {
        var haveUsers = await dbContext.Users.AnyAsync();
        var haveRoles = await dbContext.Roles.AnyAsync();
        return !haveUsers && !haveRoles;
    }

    [ExcludeFromCodeCoverage]
    public static Task<IHost> CopyAvatarFileAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var storageService = services.GetRequiredService<StorageService>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        var avatarFilePath = Path.Combine(host.Services.GetRequiredService<IHostEnvironment>().ContentRootPath,
            "wwwroot", "images", "default-avatar.jpg");
        var physicalPath = storageService.GetFilePhysicalPath(User.DefaultAvatarPath);
        if (!File.Exists(avatarFilePath))
        {
            logger.LogWarning("Avatar file does not exist. Skip copying.");
            return Task.FromResult(host);
        }

        if (File.Exists(physicalPath))
        {
            logger.LogInformation("Avatar file already exists. Skip copying.");
            return Task.FromResult(host);
        }

        if (!Directory.Exists(Path.GetDirectoryName(physicalPath)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        }

        File.Copy(avatarFilePath, physicalPath);
        logger.LogInformation("Avatar file copied to {Path}", physicalPath);
        return Task.FromResult(host);
    }

    [ExcludeFromCodeCoverage]
    public static async Task<IHost> SeedAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<TemplateDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        
        var settingsService = services.GetRequiredService<GlobalSettingsService>();
        await settingsService.SeedSettingsAsync();

        var shouldSeed = await ShouldSeedAsync(db);
        if (!shouldSeed)
        {
            logger.LogInformation("Do not need to seed the database. There are already users or roles present.");
            return host;
        }

        var configuration = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var initialPassword = configuration["AppSettings:Local:InitialAdminPassword"];
        if (string.IsNullOrWhiteSpace(initialPassword) && (environment.IsDevelopment() || EntryExtends.IsInUnitTests()))
        {
            initialPassword = "Admin@123456!";
        }
        if (initialPassword is null || initialPassword.Length < 12 ||
            (initialPassword == "Admin@123456!" && !environment.IsDevelopment() && !EntryExtends.IsInUnitTests()))
        {
            throw new InvalidOperationException("Set AppSettings__Local__InitialAdminPassword to a unique password of at least 12 characters before starting with an empty database.");
        }

        logger.LogInformation("Seeding the database with initial data...");
        var userManager = services.GetRequiredService<UserManager<User>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        var admin = new User
        {
            UserName = "admin",
            DisplayName = "Super Administrator",
            Email = "admin@default.com",
        };
        foreach (var validator in userManager.PasswordValidators)
        {
            if (!(await validator.ValidateAsync(userManager, admin, initialPassword)).Succeeded)
            {
                throw new InvalidOperationException("The configured initial administrator password does not meet the account password policy.");
            }
        }

        var role = await roleManager.FindByNameAsync("Administrators");
        if (role == null)
        {
            role = new IdentityRole("Administrators");
            await roleManager.CreateAsync(role);
        }

        var existingClaims = await roleManager.GetClaimsAsync(role);
        var existingClaimValues = existingClaims
            .Where(c => c.Type == AppPermissions.Type)
            .Select(c => c.Value)
            .ToHashSet();

        foreach (var permission in AppPermissions.GetAllPermissions())
        {
            if (!existingClaimValues.Contains(permission.Key))
            {
                var claim = new Claim(AppPermissions.Type, permission.Key);
                await roleManager.AddClaimAsync(role, claim);
            }
        }

        if (!await db.Users.AnyAsync(u => u.UserName == "admin"))
        {
            if (!(await userManager.CreateAsync(admin, initialPassword)).Succeeded ||
                !(await userManager.AddToRoleAsync(admin, "Administrators")).Succeeded)
            {
                throw new InvalidOperationException("Could not initialize the administrator account.");
            }
        }

        return host;
    }
}
