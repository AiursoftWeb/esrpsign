using Aiursoft.EsrpSign.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.EsrpSign.InMemory;

public class InMemoryContext(DbContextOptions<InMemoryContext> options) : TemplateDbContext(options)
{
    public override Task MigrateAsync(CancellationToken cancellationToken)
    {
        return Database.EnsureCreatedAsync(cancellationToken);
    }

    public override Task<bool> CanConnectAsync()
    {
        return Task.FromResult(true);
    }
}
