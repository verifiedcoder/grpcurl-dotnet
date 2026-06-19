using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace GrpCurl.Net.Studio.Theming;

/// <summary>
///     A TextMate registry that adds the vendored GraphQL grammar (SPEC-015 GQL-010) on top of the
///     bundled <see cref="RegistryOptions" />. <c>TextMateSharp.Grammars</c> ships JSON/YAML but not
///     GraphQL, so the document editor's <c>source.graphql</c> scope is served from an embedded
///     <c>graphql.tmLanguage.json</c>; every other scope, plus the theme, delegates to the bundled
///     registry so colours match the rest of the editors.
/// </summary>
internal sealed class GraphQlRegistryOptions(ThemeName theme) : IRegistryOptions
{
    public const string GraphQlScope = "source.graphql";

    private readonly RegistryOptions _inner = new(theme);

    public IRawTheme GetTheme(string scopeName) => _inner.GetTheme(scopeName);

    public IRawGrammar GetGrammar(string scopeName)
        => scopeName == GraphQlScope ? LoadGraphQlGrammar() : _inner.GetGrammar(scopeName);

    public ICollection<string> GetInjections(string scopeName) => _inner.GetInjections(scopeName);

    public IRawTheme GetDefaultTheme() => _inner.GetDefaultTheme();

    private static IRawGrammar LoadGraphQlGrammar()
    {
        var assembly = typeof(GraphQlRegistryOptions).Assembly;
        var resourceName = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith("graphql.tmLanguage.json", StringComparison.Ordinal))
                           ?? throw new InvalidOperationException("Embedded GraphQL grammar resource was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Could not open GraphQL grammar resource '{resourceName}'.");
        using var reader = new StreamReader(stream);

        return GrammarReader.ReadGrammarSync(reader);
    }
}
