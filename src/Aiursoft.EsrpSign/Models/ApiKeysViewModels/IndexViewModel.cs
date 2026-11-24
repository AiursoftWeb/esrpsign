using System.Diagnostics.CodeAnalysis;
using Aiursoft.EsrpSign.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.EsrpSign.Models.ApiKeysViewModels;

[ExcludeFromCodeCoverage]
public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "API Keys";
    }

    public IReadOnlyList<PersonalApiKey> ApiKeys { get; init; } = [];
    public string? NewKey { get; init; }
}
