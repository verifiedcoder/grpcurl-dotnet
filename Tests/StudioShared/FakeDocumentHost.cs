using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

/// <summary>Records describe-tab open requests so explorer/navigation tests can assert routing.</summary>
public sealed class FakeDocumentHost : IDocumentHost
{
    public List<(SavedConnection Connection, string Symbol, bool NewTab)> Opened { get; } = [];

    public List<(SavedConnection Connection, string Symbol, string? InitialJson)> Invocations { get; } = [];

    public (SavedConnection Connection, string Symbol, bool NewTab)? Last
        => Opened.Count == 0 ? null : Opened[^1];

    public (SavedConnection Connection, string Symbol, string? InitialJson)? LastInvocation
        => Invocations.Count == 0 ? null : Invocations[^1];

    public void OpenDescribe(SavedConnection connection, string symbol, bool newTab = false)
        => Opened.Add((connection, symbol, newTab));

    public void OpenInvocation(SavedConnection connection, string methodSymbol, string? initialRequestJson = null)
        => Invocations.Add((connection, methodSymbol, initialRequestJson));

    public int SettingsOpened { get; private set; }

    public void OpenSettings() => SettingsOpened++;

    public int HistoryOpened { get; private set; }

    public void OpenHistory() => HistoryOpened++;
}
