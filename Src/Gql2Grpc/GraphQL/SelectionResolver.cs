using GraphQLParser.AST;
using System.Text.Json.Nodes;

namespace Gql2Grpc.GraphQL;

/// <summary>
///     Expands a <see cref="GraphQLSelectionSet" /> to a <see cref="ResolvedSelection" /> tree:
///     fragment spreads and inline fragments are inlined, <c>@include</c>/<c>@skip</c> directives are
///     evaluated against coerced variables, aliases become the <c>ResponseKey</c>, and variable
///     references in arguments are substituted. Downstream layers operate only on the resolved tree.
/// </summary>
/// <remarks>
///     Constructs a resolver that uses <paramref name="fragments" /> for spread expansion and
///     <paramref name="variables" /> to substitute <c>$var</c> references and evaluate
///     <c>@include</c>/<c>@skip</c> directives.
/// </remarks>
public sealed class SelectionResolver(
    IReadOnlyDictionary<string, GraphQLFragmentDefinition> fragments,
    IReadOnlyDictionary<string, JsonNode?> variables)
{
    /// <summary>
    ///     Expands a top-level selection set into a flat <see cref="ResolvedSelection" /> tree with
    ///     fragments inlined and directives applied.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown if a fragment is referenced but not defined, a fragment cycle is detected, two
    ///     selections share a response key but different field names, or a directive's <c>if</c>
    ///     argument doesn't resolve to a Boolean.
    /// </exception>
    public IReadOnlyList<ResolvedSelection> Resolve(GraphQLSelectionSet selectionSet)
    {
        var buffer = new List<ResolvedSelection>();
        var seenResponseKeys = new Dictionary<string, ResolvedSelection>(StringComparer.Ordinal);
        ResolveInto(selectionSet, buffer, seenResponseKeys, new HashSet<string>(StringComparer.Ordinal));

        return buffer;
    }

    private void ResolveInto(
        GraphQLSelectionSet selectionSet,
        List<ResolvedSelection> buffer,
        Dictionary<string, ResolvedSelection> seenResponseKeys,
        HashSet<string> fragmentCycleGuard)
    {
        foreach (var selection in selectionSet.Selections.Where(IncludeSelection))
        {
            switch (selection)
            {
                case GraphQLField field:

                    ResolveField(field, buffer, seenResponseKeys);

                    break;

                case GraphQLFragmentSpread spread:

                    ResolveSpread(spread, buffer, seenResponseKeys, fragmentCycleGuard);

                    break;

                case GraphQLInlineFragment inline:

                    // Type condition is advisory for gRPC (no runtime type discriminator).
                    ResolveInto(inline.SelectionSet, buffer, seenResponseKeys, fragmentCycleGuard);

                    break;
            }
        }
    }

    private void ResolveField(
        GraphQLField field,
        List<ResolvedSelection> buffer,
        Dictionary<string, ResolvedSelection> seenResponseKeys)
    {
        var name = field.Name.StringValue;
        var responseKey = field.Alias?.Name.StringValue ?? name;

        if (seenResponseKeys.TryGetValue(responseKey, out var existing))
        {
            if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Selection '{responseKey}' resolves to two different fields ('{existing.Name}' and '{name}'). " +
                    "Add an alias to disambiguate.");
            }

            // Same field twice via fragments — skip the duplicate; first occurrence wins.
            return;
        }

        var args = ExtractArguments(field);
        var children = field.SelectionSet is null
            ? []
            : Resolve(field.SelectionSet);

        var resolved = new ResolvedSelection(responseKey, name, args, children);

        buffer.Add(resolved);

        seenResponseKeys[responseKey] = resolved;
    }

    private void ResolveSpread(
        GraphQLFragmentSpread spread,
        List<ResolvedSelection> buffer,
        Dictionary<string, ResolvedSelection> seenResponseKeys,
        HashSet<string> fragmentCycleGuard)
    {
        var fragmentName = spread.FragmentName.Name.StringValue;

        if (!fragments.TryGetValue(fragmentName, out var fragment))
        {
            throw new ArgumentException($"Fragment '{fragmentName}' referenced but not defined.");
        }

        if (!fragmentCycleGuard.Add(fragmentName))
        {
            throw new ArgumentException($"Fragment cycle detected involving '{fragmentName}'.");
        }

        try
        {
            ResolveInto(fragment.SelectionSet, buffer, seenResponseKeys, fragmentCycleGuard);
        }
        finally
        {
            _ = fragmentCycleGuard.Remove(fragmentName);
        }
    }

    private Dictionary<string, JsonNode?> ExtractArguments(GraphQLField field)
    {
        if (field.Arguments is null || field.Arguments.Count == 0)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        }

        var dict = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

        foreach (var argument in field.Arguments)
        {
            dict[argument.Name.StringValue] = GraphQLValueCoercer.ToJsonNode(argument.Value, variables);
        }

        return dict;
    }

    private bool IncludeSelection(ASTNode node)
    {
        var directives = GetDirectives(node);

        if (directives is null || directives.Count == 0)
        {
            return true;
        }

        foreach (var directive in directives)
        {
            var name = directive.Name.StringValue;

            if (string.Equals(name, "include", StringComparison.Ordinal) && !DirectiveIfArgument(directive))
            {
                return false;
            }

            if (string.Equals(name, "skip", StringComparison.Ordinal) && DirectiveIfArgument(directive))
            {
                return false;
            }
        }

        return true;
    }

    private static GraphQLDirectives? GetDirectives(ASTNode node) => node switch
    {
        GraphQLField f          => f.Directives,
        GraphQLFragmentSpread s => s.Directives,
        GraphQLInlineFragment i => i.Directives,
        _                       => null
    };

    private bool DirectiveIfArgument(GraphQLDirective directive)
    {
        var ifArg = directive.Arguments?.FirstOrDefault(a =>
                                                            string.Equals(a.Name.StringValue, "if", StringComparison.Ordinal))
                    ?? throw new ArgumentException($"Directive @{directive.Name.StringValue} requires an 'if' argument.");

        var value = GraphQLValueCoercer.ToJsonNode(ifArg.Value, variables);

        if (value is JsonValue jv && jv.TryGetValue(out bool b))
        {
            return b;
        }

        throw new ArgumentException($"Directive @{directive.Name.StringValue}(if: ...) must resolve to a Boolean; got '{value?.ToJsonString() ?? "null"}'.");
    }
}