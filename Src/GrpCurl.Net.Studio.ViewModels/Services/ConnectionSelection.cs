using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <inheritdoc cref="IConnectionSelection" />
public sealed class ConnectionSelection : IConnectionSelection
{
    public SavedConnection? Current { get; private set; }

    public event EventHandler? CurrentChanged;

    public void Set(SavedConnection? connection)
    {
        if (ReferenceEquals(Current, connection))
        {
            return;
        }

        Current = connection;
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }
}
