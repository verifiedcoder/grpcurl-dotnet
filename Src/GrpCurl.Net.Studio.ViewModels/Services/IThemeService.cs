using System.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Models;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     The single source of truth for the live theme (ADR-006 / FR-151). Both the View menu and the
///     Settings screen drive it; the app-layer <c>ThemeManager</c> observes <see cref="Current" /> and
///     maps it onto Avalonia's theme variant. Persisted via <see cref="ISettingsStore" />.
/// </summary>
public interface IThemeService : INotifyPropertyChanged
{
    AppTheme Current { get; }

    Task SetAsync(AppTheme theme, CancellationToken cancellationToken = default);
}
