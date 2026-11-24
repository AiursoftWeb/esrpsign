using System.Diagnostics.CodeAnalysis;
using Aiursoft.EsrpSign.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.EsrpSign.MySql;

[ExcludeFromCodeCoverage]

public class MySqlContext(DbContextOptions<MySqlContext> options) : TemplateDbContext(options);
