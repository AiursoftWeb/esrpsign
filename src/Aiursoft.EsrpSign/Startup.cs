using Microsoft.AspNetCore.RateLimiting;
using Aiursoft.AiurProtocol.Models;
using Aiursoft.EsrpSign.Authorization;
using Aiursoft.EsrpSign.Services.Signing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Aiursoft.CSTools.Tools;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Canon.ScheduledTasks;
using Aiursoft.DbTools.Switchable;
using Aiursoft.Scanner;
using Aiursoft.EsrpSign.Configuration;
using Aiursoft.WebTools.Abstractions.Models;
using Aiursoft.EsrpSign.InMemory;
using Aiursoft.EsrpSign.MySql;
using Aiursoft.EsrpSign.Services.Authentication;
using Aiursoft.EsrpSign.Services.BackgroundJobs;
using Aiursoft.EsrpSign.Sqlite;
using Aiursoft.UiStack.Layout;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc.Razor;
using Aiursoft.ClickhouseLoggerProvider;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.EsrpSign;

[ExcludeFromCodeCoverage]
public class Startup : IWebStartup
{
    public void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        // AppSettings.
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));

        // Relational database
        var (connectionString, dbType, _) = configuration.GetDbSettings();
        // Permission and token revocations must be visible across instances on the next request.
        services.AddSwitchableRelationalDatabase(
            dbType: EntryExtends.IsInUnitTests() ? "InMemory" : dbType,
            connectionString: connectionString,
            supportedDbs:
            [
                new MySqlSupportedDb(allowCache: false, splitQuery: false),
                new SqliteSupportedDb(allowCache: false, splitQuery: true),
                new InMemorySupportedDb()
            ]);

        services.AddLogging(builder =>
        {
            builder.AddClickhouse(options => configuration.GetSection("Logging:Clickhouse").Bind(options));
        });

        // Authentication and Authorization
        services.AddTemplateAuth(configuration);
        services.AddScoped<IAuthorizationHandler, CurrentSigningPermissionHandler>();
        services.AddAuthorization(options =>
        {
            foreach (var permission in new[] { AppPermissionNames.CanCreateCompanySigningKeys, AppPermissionNames.CanSignWithCompanyKeys })
            {
                options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser()
                    .AddRequirements(new CurrentSigningPermission(permission)));
            }
        });

        // Keep the key ring alongside app state, outside the served Workspace and Vault folders.
        var storage = configuration["Storage:Path"] ?? "/tmp/data";
        var keyRing = configuration["Signing:DataProtectionKeyPath"];
        services.AddDataProtection().SetApplicationName("Aiursoft.EsrpSign")
            .PersistKeysToFileSystem(new DirectoryInfo(string.IsNullOrWhiteSpace(keyRing)
                ? Path.Combine(storage, "DataProtection")
                : keyRing));
        services.AddSingleton<PrivateKeys>();
        services.AddScoped<CompanySigningService>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddConcurrencyLimiter(CompanySigningService.CryptoLimiter, limiter =>
            {
                limiter.PermitLimit = 2;
                limiter.QueueLimit = 0;
            });
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
                {
                    await context.HttpContext.Response.WriteAsJsonAsync(new AiurResponse
                    {
                        Code = Code.TooManyRequests,
                        Message = "The signing service is busy. Try again shortly."
                    }, cancellationToken);
                }
            };
        });

        // Services
        services.AddMemoryCache();
        services.AddHttpClient();
        services.AddAssemblyDependencies(typeof(Startup).Assembly);
        services.AddSingleton<NavigationState<Startup>>();

        // Background job infrastructure
        services.AddTaskQueueEngine();
        services.AddScheduledTaskEngine();

        // Background jobs
        services.RegisterBackgroundJob<DummyJob>();
        var orphanAvatarCleanupJob = services.RegisterBackgroundJob<OrphanAvatarCleanupJob>();

        // Scheduled tasks (attach a schedule to any registered background job)
        services.RegisterScheduledTask(
            registration: orphanAvatarCleanupJob,
            period:     TimeSpan.FromHours(6),
            startDelay: TimeSpan.FromMinutes(5));

        // Controllers and localization
        services.AddControllersWithViews()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
                options.SerializerSettings.ContractResolver = new DefaultContractResolver();
            })
            .AddApplicationPart(typeof(Startup).Assembly)
            .AddApplicationPart(typeof(UiStackLayoutViewModel).Assembly)
            .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
            .AddDataAnnotationsLocalization();
    }

    public void Configure(WebApplication app)
    {
        app.UseExceptionHandler("/Error/Code500");
        app.UseStatusCodePagesWithReExecute("/Error/Code{0}");
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapDefaultControllerRoute();
    }
}
