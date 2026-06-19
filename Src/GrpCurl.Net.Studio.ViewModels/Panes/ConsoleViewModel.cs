using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Services;
using System.Collections.ObjectModel;

namespace GrpCurl.Net.Studio.ViewModels.Panes;

/// <summary>
///     Bottom console: an activity log. Descriptor-load warnings mirror here as plain lines (FR-046);
///     completed calls append structured rows carrying their total and phase breakdown (FR-114).
///     Selecting a call row routes its breakdown to the shared <see cref="IInspector" />.
/// </summary>
public sealed partial class ConsoleViewModel : ViewModelBase
{
    private readonly IInspector? _inspector;

    [ObservableProperty]
    public partial ConsoleCallRowViewModel? SelectedCall { get; set; }

    /// <summary>FR-003: set when activity arrives while the console is collapsed; cleared when it's shown.</summary>
    [ObservableProperty]
    public partial bool HasUnread { get; set; }

    private bool _isActive = true;

    public ConsoleViewModel(IInspector? inspector = null) => _inspector = inspector;

    public static string Header => "Console";

    /// <summary>Plain log lines (descriptor warnings, FR-046).</summary>
    public ObservableCollection<string> Messages { get; } = [];

    /// <summary>Completed-call rows (FR-114).</summary>
    public ObservableCollection<ConsoleCallRowViewModel> Calls { get; } = [];

    public bool HasActivity => Calls.Count > 0;

    /// <summary>Appends one line to the activity log.</summary>
    public void Append(string message) => Messages.Add(message);

    /// <summary>FR-004/FR-114: records an operation as a selectable row with its inline total.</summary>
    public void AppendCall(ConsoleCallActivity activity)
    {
        Calls.Add(new ConsoleCallRowViewModel(activity));
        OnPropertyChanged(nameof(HasActivity));

        if (!_isActive)
        {
            HasUnread = true; // FR-003: new activity while collapsed shows an unread indicator
        }
    }

    /// <summary>FR-003: the shell reports whether the console is visible; becoming visible clears the unread mark.</summary>
    public void SetActive(bool active)
    {
        _isActive = active;

        if (active)
        {
            HasUnread = false;
        }
    }

    partial void OnSelectedCallChanged(ConsoleCallRowViewModel? value)
    {
        if (value is not null)
        {
            _inspector?.ShowCallTiming(value.Timing);
        }
    }
}
