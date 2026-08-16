using CommunityToolkit.Mvvm.Input;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace GrpCurl.Net.Studio.ViewModels.Documents;

/// <summary>
///     Finds the running <see cref="IAsyncRelayCommand.ExecutionTask" />s of a view model, so shutdown
///     can wait for every async command it owns (PRD-005 re-review round 3, finding 1).
///     <para>
///         Discovered rather than listed. The previous round named the commands to drain and missed
///         five kinds of work; a list is a thing to forget, and forgetting it here means shutdown
///         silently stops covering a command someone adds later. Reflection over the type's own
///         properties cannot drift.
///     </para>
/// </summary>
internal static class AsyncCommandTasks
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Cache = new();

    /// <summary>
    ///     The execution task of every async command on <paramref name="viewModel" />, including nulls
    ///     for commands that have never run (the caller filters those).
    /// </summary>
    public static List<Task?> Of(object viewModel)
    {
        var properties = Cache.GetOrAdd(viewModel.GetType(), Discover);
        var tasks = new List<Task?>(properties.Length);

        foreach (var property in properties)
        {
            // Only command-typed properties are read, and those getters are toolkit-generated: the
            // command is created on first access and nothing else happens.
            tasks.Add((property.GetValue(viewModel) as IAsyncRelayCommand)?.ExecutionTask);
        }

        return tasks;
    }

    private static PropertyInfo[] Discover(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
        => [.. type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead
                && p.GetIndexParameters().Length == 0
                && typeof(IAsyncRelayCommand).IsAssignableFrom(p.PropertyType))];
}
