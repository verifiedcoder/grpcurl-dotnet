using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     Base for a centre-zone document tab (SPEC-020 §1.2). E1.3 ships the describe document;
///     E1.4 invocation tabs reuse this host. The tab header binds <see cref="Title" /> and the
///     close button binds <see cref="CloseCommand" />; closing raises <see cref="CloseRequested" />
///     for the owning <c>DocumentsViewModel</c> to remove it.
/// </summary>
public abstract partial class DocumentViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>The tab-header text; a derived tab may append a dirty marker (e.g. saved-request divergence, FR-002).</summary>
    public virtual string DisplayTitle => Title;

    /// <summary>
    ///     The connection this tab targets, or null for connection-less tabs (e.g. settings). Used by the
    ///     shell to detect when an open tab uses an insecure TLS profile (SEC-014).
    /// </summary>
    public virtual SavedConnection? TabConnection => null;

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));

    /// <summary>Raised when the document asks to be closed (its tab's × button).</summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
