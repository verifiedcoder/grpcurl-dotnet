using System.Reflection;
using GrpCurl.Net.Studio.ViewModels;

namespace GrpCurl.Net.Studio.Tests.Unit;

/// <summary>
///     Architectural guard (SPEC-030 §1): the ViewModels assembly must be runnable and
///     testable without a UI thread, so it must not reference any Avalonia assembly. This
///     turns an accidental <c>using Avalonia.Controls;</c> into a CI failure.
/// </summary>
public sealed class ViewModelsPurityTests
{
    [Fact]
    public void ViewModels_assembly_references_no_avalonia()
    {
        var assembly = typeof(ViewModelBase).Assembly;

        var avaloniaReferences = assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase))
            .ToList();

        avaloniaReferences.ShouldBeEmpty(
            "the ViewModels project must stay UI-framework-free; found: " + string.Join(", ", avaloniaReferences));
    }

    [Fact]
    public void All_view_models_derive_from_view_model_base()
    {
        // Sanity: every *ViewModel type in the assembly shares the common base, so the
        // ViewLocator's Match (which keys on ViewModelBase) applies uniformly.
        var assembly = typeof(ViewModelBase).Assembly;

        var strays = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && t.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .Where(t => !typeof(ViewModelBase).IsAssignableFrom(t))
            .Select(t => t.FullName!)
            .ToList();

        strays.ShouldBeEmpty("these *ViewModel types do not derive from ViewModelBase: " + string.Join(", ", strays));
    }
}
