using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.EsrpSign.Entities;

[ExcludeFromCodeCoverage]
public class CompanySigningKey
{
    public Guid Id { get; set; }

    [MaxLength(64)]
    public required string Name { get; set; }

    [MaxLength(40)]
    public required string Fingerprint { get; set; }

    public DateTime CreatedAt { get; set; }

    public required byte[] PublicKey { get; set; }

    public required byte[] PrivateEnvelope { get; set; }
}
