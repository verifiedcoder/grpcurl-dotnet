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

    [ObservableProperty]
    private DocumentViewModel? _selectedDocument;

    public DocumentsViewModel(IDescriptorService descriptors, IUiDispatcher dispatcher, IClipboardService clipboard)
    {
        _descriptors = descriptors;
        _dispatcher = dispatcher;
        _clipboard = clipboard;
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
