using System.Diagnostics.CodeAnalysis;
using Aiursoft.EsrpSign.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.EsrpSign.Sqlite;

[ExcludeFromCodeCoverage]

public class SqliteContext(DbContextOptions<SqliteContext> options) : TemplateDbContext(options)
{
    public override Task<bool> CanConnectAsync()
    {
        return Task.FromResult(true);
    }
}
