using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using GrpCurl.Net.Studio.ViewModels.Documents;
using TextMateSharp.Grammars;

namespace GrpCurl.Net.Studio.Views.Documents;

public sealed partial class InvocationDocumentView : UserControl
{
    private TextEditor? _requestEditor;
    private TextEditor? _responseEditor;
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
        }

        DataContextChanged += OnDataContextChanged;
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
        }

        _viewModel = DataContext as InvocationDocumentViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SetRequestText(_viewModel.RequestJson);
            SetResponseText(_viewModel.ResponseJson);
        }
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
        if (_responseEditor is not null)
        {
            _responseEditor.Text = text ?? string.Empty;
        }
    }
}
