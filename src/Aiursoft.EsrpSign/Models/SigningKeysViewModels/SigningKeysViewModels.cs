using System.ComponentModel.DataAnnotations;
using Aiursoft.EsrpSign.SDK.Models;
using Aiursoft.EsrpSign.Services.Signing;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.EsrpSign.Models.SigningKeysViewModels;

public sealed class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel() => PageTitle = "Company Signing Keys";

    public required IReadOnlyList<SigningKeyDto> Keys { get; init; }

    public bool CanCreate { get; init; }

    public bool CanSign { get; init; }
}

public sealed class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel() => PageTitle = "Signing Key Details";

    public required SigningKeyDto Key { get; init; }

    public bool CanSign { get; init; }

    public bool KeyCreated { get; init; }

    public PassphraseVerificationOutcome? VerificationOutcome { get; set; }

    public string PublicOrigin { get; init; } = string.Empty;

    [DataType(DataType.Password)]
    public string Passphrase { get; set; } = string.Empty;
}

public sealed class VerifyPassphraseInputModel
{
    [Required]
    [DataType(DataType.Password)]
    [StringLength(256, MinimumLength = 12)]
    public string Passphrase { get; set; } = string.Empty;
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

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Passphrase), ErrorMessage = "The confirmation passphrase does not match.")]
    public string ConfirmPassphrase { get; set; } = string.Empty;
}

public sealed class SignViewModel : UiStackLayoutViewModel
{
    public SignViewModel() => PageTitle = "Sign Data";

    public SigningKeyDto? Key { get; set; }

    public string Mode { get; set; } = "text";

    [Required]
    [DataType(DataType.Password)]
    [StringLength(256, MinimumLength = 12)]
    public string Passphrase { get; set; } = string.Empty;

    [DisplayFormat(ConvertEmptyStringToNull = false)]
    [MaxLength(SignRequest.MaxDataBytes)]
    public string Text { get; set; } = string.Empty;
}

public sealed class SignFileInputModel
{
    [Required]
    [DataType(DataType.Password)]
    [StringLength(256, MinimumLength = 12)]
    public string Passphrase { get; set; } = string.Empty;

    public IFormFile? File { get; set; }
}
