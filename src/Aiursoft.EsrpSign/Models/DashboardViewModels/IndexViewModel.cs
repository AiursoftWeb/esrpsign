using Aiursoft.EsrpSign.SDK.Models;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.EsrpSign.Models.DashboardViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Dashboard";
    }

    public IReadOnlyList<SigningKeyDto> RecentKeys { get; init; } = [];

    public int TotalSigningKeys { get; init; }

    public int ActiveApiKeys { get; init; }

    public bool CanCreateKeys { get; init; }

    public bool CanSignKeys { get; init; }
}
