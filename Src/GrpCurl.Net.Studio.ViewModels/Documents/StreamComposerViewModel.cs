using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Studio.ViewModels.Models.Invocation;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Collections.ObjectModel;
using System.Threading.Channels;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>A row in the composer's sent-message queue (FR-082). <paramref name="Json" /> is the full body (FR-165).</summary>
public sealed record SentMessageRow(int Index, DateTimeOffset Timestamp, string Preview, string Json);

/// <summary>
///     The client/duplex request composer (FR-082/083). Owns a <see cref="Channel{T}" /> of request
///     JSON that the runner consumes as the request stream: <see cref="SendCommand" /> enqueues a
///     message and logs it, <see cref="CompleteSendingCommand" /> closes the request stream, and
///     <see cref="LoadBatchCommand" /> enqueues a whole array/concatenated batch. Validation reuses
///     the advisory <see cref="IRequestValidator" /> (same as FR-063); never blocks Send.
/// </summary>
public sealed partial class StreamComposerViewModel : ViewModelBase
{
    private readonly SavedConnection _connection;
    private readonly string _methodSymbol;
    private readonly IRequestValidator _validator;
    private readonly IUiDispatcher _dispatcher;
    private readonly IFilePickerService? _filePicker;
    private readonly Func<string, Task<string>> _readFile;

    private Channel<string>? _channel;
    private CancellationTokenSource? _validationCts;

    [ObservableProperty]
    public partial string MessageJson { get; set; } = "{}";

    /// <summary>
    ///     P3 fix: mirrors the parent tab's "allow unknown fields" option <em>live</em>. The composer used
    ///     to capture this once at construction, so toggling it afterwards left composer validation using
    ///     the stale value while the actual send used the new one. The parent updates this and validation
    ///     re-runs under the current option.
    /// </summary>
    [ObservableProperty]
    public partial bool AllowUnknownFields { get; set; }

    [ObservableProperty]
    public partial bool ClearAfterSend { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(CompleteSendingCommand), nameof(LoadBatchCommand))]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(CompleteSendingCommand), nameof(LoadBatchCommand))]
    public partial bool SendingComplete { get; set; }

    public StreamComposerViewModel(
        SavedConnection connection,
        string methodSymbol,
        bool allowUnknownFields,
        IRequestValidator validator,
        IUiDispatcher dispatcher,
        IFilePickerService? filePicker,
        Func<string, Task<string>>? readFile = null)
    {
        _connection = connection;
        _methodSymbol = methodSymbol;
        AllowUnknownFields = allowUnknownFields; // set the backing field directly: no validation before Begin()
        _validator = validator;
        _dispatcher = dispatcher;
        _filePicker = filePicker;
        _readFile = readFile ?? (path => File.ReadAllTextAsync(path));
    }

    public ObservableCollection<SentMessageRow> SentQueue { get; } = [];
    public ObservableCollection<ValidationProblem> Problems { get; } = [];

    /// <summary>True while the compose draft has advisory validation problems to show (P3 fix).</summary>
    public bool HasProblems => Problems.Count > 0;

    /// <summary>FR-165: the full JSON of every message sent this session, for copy-as-CLI reproduction.</summary>
    public IReadOnlyList<string> SentMessages => SentQueue.Select(r => r.Json).ToList();

    /// <summary>True while the request stream is open and accepting sends.</summary>
    public bool CanSend => IsActive && !SendingComplete;

    internal TimeSpan ValidationDebounce { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Opens a fresh request channel and returns its reader stream for the runner to consume.</summary>
    public IAsyncEnumerable<string> Begin()
    {
        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        SentQueue.Clear();
        SendingComplete = false;
        IsActive = true;
        return _channel.Reader.ReadAllAsync();
    }

    /// <summary>Marks the stream finished (server closed / cancelled); disables sending.</summary>
    public void End()
    {
        IsActive = false;
        _ = (_channel?.Writer.TryComplete());
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        Enqueue(MessageJson);

        if (ClearAfterSend)
        {
            MessageJson = string.Empty;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void CompleteSending()
    {
        _ = (_channel?.Writer.TryComplete());
        SendingComplete = true;
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task LoadBatch()
    {
        if (_filePicker is null)
        {
            return;
        }

        var path = await _filePicker.OpenFileAsync("Load batch", [".json", ".txt"]);

        if (path is null)
        {
            return;
        }

        var content = await _readFile(path).ConfigureAwait(false);

        await _dispatcher.InvokeAsync(() =>
        {
            foreach (var message in JsonMessageSplitter.Split(content))
            {
                Enqueue(message);
            }
        });
    }

    private void Enqueue(string json)
    {
        if (_channel is null || !_channel.Writer.TryWrite(json))
        {
            return;
        }

        SentQueue.Add(new SentMessageRow(SentQueue.Count, DateTimeOffset.Now, Preview(json), json));
    }

    // FR-082: advisory validation, debounced, never blocks Send.
    partial void OnMessageJsonChanged(string value) => RestartValidation();

    // Re-validate the current draft when the parent toggles "allow unknown fields" so the feedback
    // matches the option the send will actually use.
    partial void OnAllowUnknownFieldsChanged(bool value) => RestartValidation();

    private void RestartValidation()
    {
        _validationCts?.Cancel();
        _validationCts?.Dispose();
        var cts = new CancellationTokenSource();
        _validationCts = cts;
        _ = DebouncedValidateAsync(cts.Token);
    }

    private async Task DebouncedValidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (ValidationDebounce > TimeSpan.Zero)
            {
                await Task.Delay(ValidationDebounce, cancellationToken).ConfigureAwait(false);
            }

            await RunValidationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit.
        }
    }

    internal async Task RunValidationAsync(CancellationToken cancellationToken = default)
    {
        var problems = await _validator
            .ValidateAsync(_connection, _methodSymbol, MessageJson, AllowUnknownFields, cancellationToken)
            .ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            Problems.Clear();

            foreach (var problem in problems)
            {
                Problems.Add(problem);
            }

            OnPropertyChanged(nameof(HasProblems));
        });
    }

    private static string Preview(string json)
    {
        var line = json.ReplaceLineEndings(" ").Trim();
        return line.Length <= 100 ? line : line[..100] + "…";
    }
}
