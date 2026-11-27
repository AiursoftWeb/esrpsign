using System.ComponentModel.DataAnnotations;

namespace Aiursoft.EsrpSign.SDK.Models;

public sealed class SignRequest
{
    public const int MaxDataBytes = 64 * 1024;

    [Required]
    [StringLength(256, MinimumLength = 12)]
    public string Passphrase { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    [MaxLength(MaxDataBytes)]
    public byte[] Data { get; set; } = [];

    [EnumDataType(typeof(SignatureFormat))]
    public SignatureFormat Format { get; set; } = SignatureFormat.DetachedBinary;
}
