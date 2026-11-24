using System.ComponentModel.DataAnnotations;
using Aiursoft.EsrpSign.SDK.Models;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.EsrpSign.Models.SigningKeysViewModels;

public sealed class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel() => PageTitle = "Company Signing Keys";

    public required IReadOnlyList<SigningKeyDto> Keys { get; init; }
}

public sealed class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel() => PageTitle = "Create Company Signing Key";

    [Required]
    [MaxLength(64)]
    [RegularExpression(@"[^\p{Cc}]+")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [StringLength(256, MinimumLength = 12)]
    public string Passphrase { get; set; } = string.Empty;
}

public sealed class SignViewModel : UiStackLayoutViewModel
{
    public SignViewModel() => PageTitle = "Sign Text";

    public SigningKeyDto? Key { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [StringLength(256, MinimumLength = 12)]
    public string Passphrase { get; set; } = string.Empty;

    [DisplayFormat(ConvertEmptyStringToNull = false)]
    [MaxLength(SignRequest.MaxDataBytes)]
    public string Text { get; set; } = string.Empty;
}
