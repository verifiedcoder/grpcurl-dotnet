using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using GrpCurl.Net.Studio.Theming;
using GrpCurl.Net.Studio.ViewModels.Documents;
using System.Collections.Specialized;
using System.ComponentModel;
using TextMateSharp.Grammars;

namespace GrpCurl.Net.Studio.Views.Documents;

/// <summary>
///     View for a GraphQL operation tab (SPEC-015 E4.1). Hosts three AvaloniaEdit editors — the GraphQL
///     document (plain text in this PR; the GraphQL TextMate grammar lands with the editor-conveniences
///     work), the variables JSON, and the read-only response envelope (JSON-highlighted) — and keeps
///     them in sync with the view model. Syntax/configuration problems drive squiggles on the document
///     via the shared <see cref="SquiggleRenderer" />, mirroring the invocation tab.
/// </summary>
public sealed partial class GraphQlDocumentView : UserControl
{
    private readonly SquiggleRenderer _squiggles = new();
    private readonly TextEditor? _documentEditor;
    private readonly TextEditor? _variablesEditor;
    private readonly TextEditor? _responseEditor;
    private readonly TextEditor? _mappingEditor;
    private GraphQlDocumentViewModel? _viewModel;
    private bool _syncingDocument;
    private bool _syncingVariables;
    private bool _syncingMapping;

    public GraphQlDocumentView()
    {
        InitializeComponent();

        _documentEditor = this.FindControl<TextEditor>("DocumentEditor");
        _variablesEditor = this.FindControl<TextEditor>("VariablesEditor");
        _responseEditor = this.FindControl<TextEditor>("ResponseEditor");
        _mappingEditor = this.FindControl<TextEditor>("MappingEditor");

        InstallGraphQlGrammar(_documentEditor);
        InstallGrammar(_variablesEditor, "json");
        InstallGrammar(_responseEditor, "json");
        InstallGrammar(_mappingEditor, "yaml");

        if (_documentEditor is not null)
        {
            _documentEditor.TextChanged += OnDocumentEditorTextChanged;
            _documentEditor.TextArea.TextView.BackgroundRenderers.Add(_squiggles);
            _documentEditor.Options.EnableHyperlinks = false;

            // GQL-015: Ctrl+/ toggles a '#' line comment over the selection (or current line).
            _documentEditor.AddHandler(KeyDownEvent, OnDocumentEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        if (_variablesEditor is not null)
        {
            _variablesEditor.TextChanged += OnVariablesEditorTextChanged;
        }

        if (_mappingEditor is not null)
        {
            _mappingEditor.TextChanged += OnMappingEditorTextChanged;
        }

        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private static void InstallGrammar(TextEditor? editor, string languageId)
    {
        if (editor is null)
        {
            return;
        }

        var isDark = (editor.ActualThemeVariant ?? ThemeVariant.Default) == ThemeVariant.Dark;
        var registry = new RegistryOptions(isDark ? ThemeName.DarkPlus : ThemeName.LightPlus);
        var installation = editor.InstallTextMate(registry);
        installation.SetGrammar(registry.GetScopeByLanguageId(languageId));
    }

    // GQL-010: highlight the GraphQL document via the vendored grammar (the bundled package has none).
    private static void InstallGraphQlGrammar(TextEditor? editor)
    {
        if (editor is null)
        {
            return;
        }

        var isDark = (editor.ActualThemeVariant ?? ThemeVariant.Default) == ThemeVariant.Dark;
        var registry = new GraphQlRegistryOptions(isDark ? ThemeName.DarkPlus : ThemeName.LightPlus);
        var installation = editor.InstallTextMate(registry);
        installation.SetGrammar(GraphQlRegistryOptions.GraphQlScope);
    }

    // GQL-015: Ctrl+/ comment toggle. Comments every selected line that isn't yet commented; if all
    // selected lines already start with '#', uncomments instead.
    private void OnDocumentEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Oem2 || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || _documentEditor?.Document is not { } document)
        {
            return;
        }

        e.Handled = true;

        var area = _documentEditor.TextArea;
        var firstLine = document.GetLineByOffset(area.Selection.IsEmpty ? _documentEditor.CaretOffset : area.Selection.SurroundingSegment.Offset);
        var lastLine = document.GetLineByOffset(area.Selection.IsEmpty ? _documentEditor.CaretOffset : area.Selection.SurroundingSegment.EndOffset);

        var lines = new List<DocumentLine>();
        for (var line = firstLine; line is not null && line.LineNumber <= lastLine.LineNumber; line = line.NextLine)
        {
            lines.Add(line);
        }

        var allCommented = lines.TrueForAll(l => document.GetText(l).TrimStart().StartsWith('#'));

        document.BeginUpdate();
        try
        {
            // Edit bottom-up so earlier offsets stay valid as we insert/remove characters.
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                var line = lines[i];
                var text = document.GetText(line);

                if (allCommented)
                {
                    var hash = text.IndexOf('#');
                    if (hash >= 0)
                    {
                        var removeCount = hash + 1 < text.Length && text[hash + 1] == ' ' ? 2 : 1;
                        document.Remove(line.Offset + hash, removeCount);
                    }
                }
                else if (text.Trim().Length > 0)
                {
                    document.Insert(line.Offset, "# ");
                }
            }
        }
        finally
        {
            document.EndUpdate();
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Problems.CollectionChanged -= OnProblemsChanged;
        }

        _viewModel = DataContext as GraphQlDocumentViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.Problems.CollectionChanged += OnProblemsChanged;
            SetDocumentText(_viewModel.Document);
            SetVariablesText(_viewModel.VariablesJson);
            SetMappingText(_viewModel.MappingText);
            SetResponseText(_viewModel.ResponseJson);
            RefreshMarkers();
        }
    }

    private void OnProblemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshMarkers();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(GraphQlDocumentViewModel.Document):
                SetDocumentText(_viewModel.Document);
                break;
            case nameof(GraphQlDocumentViewModel.VariablesJson):
                SetVariablesText(_viewModel.VariablesJson);
                break;
            case nameof(GraphQlDocumentViewModel.MappingText):
                SetMappingText(_viewModel.MappingText);
                break;
            case nameof(GraphQlDocumentViewModel.ResponseJson):
                SetResponseText(_viewModel.ResponseJson);
                break;
        }
    }

    private void OnDocumentEditorTextChanged(object? sender, EventArgs e)
    {
        if (!_syncingDocument && _viewModel is not null && _documentEditor is not null)
        {
            _viewModel.Document = _documentEditor.Text;
        }
    }

    private void OnVariablesEditorTextChanged(object? sender, EventArgs e)
    {
        if (!_syncingVariables && _viewModel is not null && _variablesEditor is not null)
        {
            _viewModel.VariablesJson = _variablesEditor.Text;
        }
    }

    private void OnMappingEditorTextChanged(object? sender, EventArgs e)
    {
        if (!_syncingMapping && _viewModel is not null && _mappingEditor is not null)
        {
            _viewModel.MappingText = _mappingEditor.Text;
        }
    }

    private void SetDocumentText(string text)
    {
        if (_documentEditor is null || _documentEditor.Text == text)
        {
            return;
        }

        _syncingDocument = true;
        _documentEditor.Text = text;
        _syncingDocument = false;
    }

    private void SetVariablesText(string text)
    {
        if (_variablesEditor is null || _variablesEditor.Text == text)
        {
            return;
        }

        _syncingVariables = true;
        _variablesEditor.Text = text;
        _syncingVariables = false;
    }

    private void SetMappingText(string text)
    {
        if (_mappingEditor is null || _mappingEditor.Text == text)
        {
            return;
        }

        _syncingMapping = true;
        _mappingEditor.Text = text;
        _syncingMapping = false;
    }

    private void SetResponseText(string? text)
    {
        if (_responseEditor is not null)
        {
            _responseEditor.Text = text ?? string.Empty;
        }
    }

    private void RefreshMarkers()
    {
        if (_documentEditor?.Document is not { } document || _viewModel is null)
        {
            _squiggles.SetMarkers([]);
            _documentEditor?.TextArea.TextView.InvalidateVisual();
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
                // Position drifted past a concurrent edit; the Problems strip still shows it.
            }
        }

        _squiggles.SetMarkers(markers);
        _documentEditor.TextArea.TextView.InvalidateVisual();
    }
}
