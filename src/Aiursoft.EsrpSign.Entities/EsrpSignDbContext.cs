using System.Diagnostics.CodeAnalysis;
using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.EsrpSign.Entities;

[ExcludeFromCodeCoverage]

public abstract class TemplateDbContext(DbContextOptions options) : IdentityDbContext<User>(options), ICanMigrate
{
    public DbSet<CompanySigningKey> CompanySigningKeys => Set<CompanySigningKey>();
    public DbSet<PersonalApiKey> PersonalApiKeys => Set<PersonalApiKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<CompanySigningKey>().HasIndex(key => key.Fingerprint).IsUnique();
        builder.Entity<PersonalApiKey>().HasIndex(key => key.KeyHash).IsUnique();
    }

    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();

    public virtual  Task MigrateAsync(CancellationToken cancellationToken) =>
        Database.MigrateAsync(cancellationToken);

    public virtual  Task<bool> CanConnectAsync() =>
        Database.CanConnectAsync();
}
