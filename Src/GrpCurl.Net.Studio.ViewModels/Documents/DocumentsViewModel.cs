using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     Owns the centre-zone document tabs and implements <see cref="IDocumentHost" />. Opening a
///     describe document de-dupes against an existing tab already showing that symbol unless a new
///     tab is explicitly requested (FR-051 Ctrl+click). Closing a tab selects a sensible neighbour.
/// </summary>
public sealed partial class DocumentsViewModel : ViewModelBase, IDocumentHost
{
    private readonly IDescriptorService _descriptors;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClipboardService _clipboard;
    private readonly IInvocationRunner _invocation;
    private readonly IDialogService _dialogs;
    private readonly ILauncherService _launcher;
    private readonly IRequestValidator _validator;
    private readonly ISettingsStore _settings;
    private readonly IThemeService _theme;
    private readonly IProtocService? _protoc;

    [ObservableProperty]
    private DocumentViewModel? _selectedDocument;

    public DocumentsViewModel(
        IDescriptorService descriptors,
        IUiDispatcher dispatcher,
        IClipboardService clipboard,
        IInvocationRunner invocation,
        IDialogService dialogs,
        ILauncherService launcher,
        IRequestValidator validator,
        ISettingsStore settings,
        IThemeService theme,
        IProtocService? protoc = null)
    {
        _descriptors = descriptors;
        _dispatcher = dispatcher;
        _clipboard = clipboard;
        _invocation = invocation;
        _dialogs = dialogs;
        _launcher = launcher;
        _validator = validator;
        _settings = settings;
        _theme = theme;
        _protoc = protoc;
    }

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    public void OpenDescribe(SavedConnection connection, string symbol, bool newTab = false)
    {
        if (!newTab)
        {
            var existing = Documents
                .OfType<DescribeDocumentViewModel>()
                .FirstOrDefault(d => d.Connection.Id == connection.Id && d.CurrentSymbol == symbol);

            if (existing is not null)
            {
                SelectedDocument = existing;
                return;
            }
        }

        var document = new DescribeDocumentViewModel(connection, symbol, _descriptors, _dispatcher, _clipboard, this);
        document.CloseRequested += OnDocumentCloseRequested;

        Documents.Add(document);
        SelectedDocument = document;
    }

    public void OpenInvocation(SavedConnection connection, string methodSymbol, string? initialRequestJson = null)
    {
        var document = new InvocationDocumentViewModel(
            connection, methodSymbol, initialRequestJson, _invocation, _descriptors, _dispatcher, _clipboard, _dialogs, _launcher, _validator);

        // FR-153 / FR-163: seed new tabs from the Network/General defaults (initial values only).
        var network = _settings.Current.Network;
        document.CliDialect = _settings.Current.General.CliShellDialect;

        if (!string.IsNullOrWhiteSpace(network.DefaultDeadline))
        {
            document.Deadline = network.DefaultDeadline;
        }

        if (!string.IsNullOrWhiteSpace(network.MaxMessageSize))
        {
            document.MaxMessageSize = network.MaxMessageSize;
        }

        document.CloseRequested += OnDocumentCloseRequested;

        Documents.Add(document);
        SelectedDocument = document;
    }

    public void OpenSettings()
    {
        var existing = Documents.OfType<SettingsDocumentViewModel>().FirstOrDefault();

        if (existing is not null)
        {
            SelectedDocument = existing;
            return;
        }

        var document = new SettingsDocumentViewModel(_settings, _theme, _dialogs, _protoc);
        document.CloseRequested += OnDocumentCloseRequested;

        Documents.Add(document);
        SelectedDocument = document;
    }

    private void OnDocumentCloseRequested(object? sender, EventArgs e)
    {
        if (sender is not DocumentViewModel document)
        {
            return;
        }

        document.CloseRequested -= OnDocumentCloseRequested;

        var index = Documents.IndexOf(document);
        Documents.Remove(document);

        if (SelectedDocument == document)
        {
            SelectedDocument = Documents.Count == 0
                ? null
                : Documents[Math.Min(index, Documents.Count - 1)];
        }
    }
}
