using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
using GrpCurl.Net.Studio.ViewModels.Documents;
using TextMateSharp.Grammars;

namespace GrpCurl.Net.Studio.Views.Documents;

public sealed partial class InvocationDocumentView : UserControl
{
    private readonly SquiggleRenderer _squiggles = new();
    private TextEditor? _requestEditor;
    private TextEditor? _responseEditor;
    private FoldingManager? _responseFolding;
    private InvocationDocumentViewModel? _viewModel;
    private bool _syncingRequest;

    public InvocationDocumentView()
    {
        InitializeComponent();

        _requestEditor = this.FindControl<TextEditor>("RequestEditor");
        _responseEditor = this.FindControl<TextEditor>("ResponseEditor");

        InstallJsonGrammar(_requestEditor);
        InstallJsonGrammar(_responseEditor);

        if (_requestEditor is not null)
        {
            _requestEditor.TextChanged += OnRequestEditorTextChanged;
            _requestEditor.TextArea.TextView.BackgroundRenderers.Add(_squiggles);
        }

        ApplyIndentation(_requestEditor);
        ApplyIndentation(_responseEditor);

        // FR-074: search (Ctrl+F) + collapse/expand folding on the response viewer.
        if (_responseEditor is not null)
        {
            SearchPanel.Install(_responseEditor);
            _responseFolding = FoldingManager.Install(_responseEditor.TextArea);
        }

        DataContextChanged += OnDataContextChanged;
    }

    // FR-152: pick up the configured indentation width (font/size flow via DynamicResource).
    private static void ApplyIndentation(TextEditor? editor)
    {
        if (editor is not null
            && Avalonia.Application.Current?.Resources["Editor.IndentationSize"] is int indent and > 0)
        {
            editor.Options.IndentationSize = indent;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private static void InstallJsonGrammar(TextEditor? editor)
    {
        if (editor is null)
        {
            return;
        }

        // Theme follows the app variant; JSON is the only grammar needed for E1.4 (proto-text → E2.3).
        var isDark = (editor.ActualThemeVariant ?? ThemeVariant.Default) == ThemeVariant.Dark;
        var registry = new RegistryOptions(isDark ? ThemeName.DarkPlus : ThemeName.LightPlus);
        var installation = editor.InstallTextMate(registry);
        installation.SetGrammar(registry.GetScopeByLanguageId("json"));
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Problems.CollectionChanged -= OnProblemsChanged;
        }

        _viewModel = DataContext as InvocationDocumentViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Problems.CollectionChanged += OnProblemsChanged;
            SetRequestText(_viewModel.RequestJson);
            SetResponseText(_viewModel.ResponseJson);
            RefreshMarkers();
        }
    }

    private void OnProblemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshMarkers();

    private void RefreshMarkers()
    {
        if (_requestEditor?.Document is not { } document || _viewModel is null)
        {
            _squiggles.SetMarkers([]);
            _requestEditor?.TextArea.TextView.InvalidateVisual();
            return;
        }

        var markers = new List<(int, int)>();

        foreach (var problem in _viewModel.Problems)
        {
            if (problem.Line is not { } line)
            {
                continue;
            }

            try
            {
                var documentLine = document.GetLineByNumber(Math.Clamp(line, 1, document.LineCount));
                var column = Math.Clamp(problem.Column ?? 1, 1, documentLine.Length + 1);
                var offset = documentLine.Offset + column - 1;
                var length = Math.Max(1, documentLine.EndOffset - offset);
                markers.Add((offset, length));
            }
            catch (ArgumentOutOfRangeException)
            {
                // Position drifted past a concurrent edit; skip the marker (the strip still shows it).
            }
        }

        _squiggles.SetMarkers(markers);
        _requestEditor.TextArea.TextView.InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InvocationDocumentViewModel.RequestJson) && _viewModel is not null)
        {
            SetRequestText(_viewModel.RequestJson);
        }
        else if (e.PropertyName == nameof(InvocationDocumentViewModel.ResponseJson) && _viewModel is not null)
        {
            SetResponseText(_viewModel.ResponseJson);
        }
    }

    private void OnRequestEditorTextChanged(object? sender, EventArgs e)
    {
        if (_syncingRequest || _viewModel is null || _requestEditor is null)
        {
            return;
        }

        _viewModel.RequestJson = _requestEditor.Text;
    }

    private void SetRequestText(string text)
    {
        if (_requestEditor is null || _requestEditor.Text == text)
        {
            return;
        }

        _syncingRequest = true;
        _requestEditor.Text = text;
        _syncingRequest = false;
    }

    private void SetResponseText(string? text)
    {
        if (_responseEditor is null)
        {
            return;
        }

        _responseEditor.Text = text ?? string.Empty;

        // FR-074: rebuild the collapse/expand regions for the new body.
        if (_responseFolding is not null)
        {
            _responseFolding.UpdateFoldings(CreateBraceFoldings(_responseEditor.Document), firstErrorOffset: -1);
        }
    }

    /// <summary>FR-074: a minimal JSON brace/bracket folding strategy for multi-line { … } / [ … ] regions.</summary>
    private static IEnumerable<NewFolding> CreateBraceFoldings(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var openings = new Stack<int>();
        var inString = false;

        for (var offset = 0; offset < document.TextLength; offset++)
        {
            var c = document.GetCharAt(offset);

            if (inString)
            {
                if (c == '\\')
                {
                    offset++; // skip the escaped character
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{' or '[':
                    openings.Push(offset);
                    break;
                case '}' or ']' when openings.Count > 0:
                    var start = openings.Pop();
                    if (document.GetLineByOffset(start).LineNumber != document.GetLineByOffset(offset).LineNumber)
                    {
                        foldings.Add(new NewFolding(start, offset + 1));
                    }

                    break;
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset)); // UpdateFoldings requires start-offset order
        return foldings;
    }
}
