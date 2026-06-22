using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace GrpCurl.Net.Studio.ViewModels;

/// <summary>One palette entry: a labelled action (a command, a connection, or a saved request).</summary>
public sealed record PaletteItem(string Title, string Category, Func<Task> InvokeAsync);

/// <summary>
///     The command palette (Ctrl+K, SPEC-020): a fuzzy-filtered list of commands and navigation targets,
///     hosted modally. Returns the chosen <see cref="PaletteItem" /> (the shell runs its action once the
///     palette has closed) or <see langword="null" /> on cancel.
/// </summary>
public sealed partial class CommandPaletteViewModel : DialogViewModel<PaletteItem?>
{
    public override string Title => "Command palette";

    private readonly IReadOnlyList<PaletteItem> _all;

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial PaletteItem? SelectedItem { get; set; }

    public CommandPaletteViewModel(IReadOnlyList<PaletteItem> items)
    {
        _all = items;
        Items = [];
        ApplyFilter();
    }

    public ObservableCollection<PaletteItem> Items { get; }

    public bool HasNoMatches => Items.Count == 0;

    partial void OnQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = Query.Trim();

        Items.Clear();

        foreach (var item in _all.Where(i => Matches(i, query)))
        {
            Items.Add(item);
        }

        SelectedItem = Items.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoMatches));
    }

    private static bool Matches(PaletteItem item, string query)
        => query.Length == 0
           || FuzzyMatch(item.Title, query)
           || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>Subsequence match: every query character appears in order within the text (case-insensitive).</summary>
    private static bool FuzzyMatch(string text, string query)
    {
        var at = 0;

        foreach (var ch in query)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            at = text.IndexOf(ch.ToString(), at, StringComparison.OrdinalIgnoreCase);

            if (at < 0)
            {
                return false;
            }

            at++;
        }

        return true;
    }

    [RelayCommand]
    private void MoveDown() => Move(1);

    [RelayCommand]
    private void MoveUp() => Move(-1);

    private void Move(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var index = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
        SelectedItem = Items[Math.Clamp(index + delta, 0, Items.Count - 1)];
    }

    [RelayCommand]
    private void Accept() => Close(SelectedItem);

    [RelayCommand]
    private void Cancel() => Close(null);
}
