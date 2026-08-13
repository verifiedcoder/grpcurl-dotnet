using CommunityToolkit.Mvvm.Input;
using GrpCurl.Net.Studio.ViewModels.Documents;
using System.Reflection;
using System.Text.RegularExpressions;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     PRD-005 re-review round 4, finding 2: the drain is only as good as its enrolment, and enrolment
///     is a convention. These are the tripwires that stop the convention rotting silently.
///     <para>
///         The history justifies them. Round 3 replaced a hand-maintained list of tasks to wait for with
///         start-time tracking and reflection, and round 4 still found two `_ = …Async(…)` starts that
///         never enrolled and a whole class of child view models the reflection could not reach. Neither
///         is visible in a diff unless someone already knows to look.
///     </para>
/// </summary>
public sealed class WorkEnrolmentTripwireTests
{
    /// <summary>
    ///     Every async command owner in the view-model layer that a document can own must either be a
    ///     document or be listed here as a child that is collected through
    ///     <see cref="IOwnsBackgroundWork" />. Anything else is shell/pane scope — see the note below.
    /// </summary>
    private static readonly HashSet<string> CollectedChildren =
    [
        nameof(StreamComposerViewModel),
        nameof(StreamRowViewModel),
        nameof(MetadataRowViewModel)
    ];

    /// <summary>
    ///     Owners outside the document tree. `DocumentsViewModel` drains documents, so these are not in
    ///     scope for PRD-005 — they belong to the shell and the panes, whose lifetime is the window's.
    ///     Listed rather than filtered by namespace so that moving one into `Documents/` trips this test.
    /// </summary>
    private static readonly HashSet<string> OutsideTheDocumentTree =
    [
        "ConnectionEditorViewModel",
        "ConnectionListItemViewModel",
        "ConnectionsPaneViewModel",
        "EnvironmentEditorViewModel",
        "EnvironmentManagerViewModel",
        "EnvironmentSwitcherViewModel",
        "MainWindowViewModel",
        "SavedRequestItemViewModel",
        "ServiceExplorerViewModel",
        "TlsProfileEditorViewModel",
        "TlsProfileManagerViewModel",
        "WorkspaceSessionViewModel"
    ];

    [Fact]
    public void Every_async_command_owner_is_a_document_a_collected_child_or_explicitly_out_of_scope()
    {
        var owners = typeof(DocumentViewModel).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(HasAsyncCommand)
            .Select(t => t.Name)
            .ToList();

        owners.ShouldNotBeEmpty("the query itself must still find something");

        var unaccounted = owners
            .Where(name => !CollectedChildren.Contains(name)
                && !OutsideTheDocumentTree.Contains(name)
                && !IsDocument(name))
            .ToList();

        unaccounted.ShouldBeEmpty(
            "a new async-command owner must be wired into its parent's CollectOwnedWork (and listed in "
            + $"CollectedChildren), or declared out of the document tree: {string.Join(", ", unaccounted)}");
    }

    /// <summary>
    ///     No document may start a task and discard it. `Track(...)` is the enrolment point; a bare
    ///     <c>_ = SomethingAsync(...)</c> bypasses the drain, which is precisely how the Settings theme
    ///     write and settings save escaped it.
    /// </summary>
    [Fact]
    public void No_document_discards_a_task()
    {
        var documents = Path.Combine(RepositoryRoot(), "Src", "GrpCurl.Net.Studio.ViewModels", "Documents");

        Directory.Exists(documents).ShouldBeTrue($"expected the documents source at {documents}");

        // `_ = Something(...)` where the callee's name ends in Async — the repo's naming convention for
        // every task-returning method. A bare call without the discard is already a build error
        // (CS4014 + warnings-as-errors), so the discard is the only way through.
        //
        // `_ = await …` is deliberately excluded: that awaits the call and discards its *value*, which
        // is not fire-and-forget and needs no enrolment.
        // The lookahead sits immediately after the "=" and swallows the whitespace itself; placed
        // after \s* instead, the engine simply backtracks the whitespace and matches anyway.
        var discard = new Regex(@"_\s*=(?!\s*await\b)\s*[^;]*?\w+Async\s*\(", RegexOptions.Compiled);

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(documents, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();

                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('*'))
                {
                    continue; // prose about the rule is not a breach of it
                }

                if (discard.IsMatch(lines[i]))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "a document must enrol the work it starts via Track(...) so shutdown can wait for it: "
            + string.Join(" | ", offenders));
    }

    private static bool HasAsyncCommand(Type type)
        => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Any(p => p.CanRead
                && p.GetIndexParameters().Length == 0
                && typeof(IAsyncRelayCommand).IsAssignableFrom(p.PropertyType));

    private static bool IsDocument(string name)
        => typeof(DocumentViewModel).Assembly.GetTypes()
            .Any(t => t.Name == name && typeof(DocumentViewModel).IsAssignableFrom(t));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GrpCurl.Net.slnx")))
        {
            directory = directory.Parent;
        }

        _ = directory.ShouldNotBeNull("could not locate the repository root from the test assembly");

        return directory.FullName;
    }
}
