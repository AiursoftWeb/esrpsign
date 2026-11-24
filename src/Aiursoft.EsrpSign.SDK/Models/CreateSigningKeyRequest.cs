using System.ComponentModel.DataAnnotations;

namespace Aiursoft.EsrpSign.SDK.Models;

public sealed class CreateSigningKeyRequest
{
    [Required]
    [MaxLength(64)]
    [RegularExpression(@"[^\p{Cc}]+")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(256, MinimumLength = 12)]
    public string Passphrase { get; set; } = string.Empty;
}
