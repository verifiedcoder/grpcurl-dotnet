using System.Text.RegularExpressions;
using GrpCurl.Net.Studio.ViewModels.Models.Connections;
using GrpCurl.Net.Utilities;

namespace GrpCurl.Net.Studio.ViewModels;

/// <summary>The kind of secret leak the save-time scanner found (SEC-034).</summary>
public enum SecretLeakKind
{
    /// <summary>A plain string field holds a value equal to a known secret value currently in memory.</summary>
    SecretValueInPlainField,

    /// <summary>A header whose name is sensitive (per Core's redaction rules) carries a non-<c>${VAR}</c> literal.</summary>
    SensitiveHeaderLiteral
}

/// <summary>One finding from <see cref="WorkspaceSecretScanner" />: a kind and a human-readable location.</summary>
public sealed record SecretLeak(SecretLeakKind Kind, string Location);

/// <summary>
///     SEC-034 (T1 backstop): scans a <see cref="WorkspaceModel" /> before an explicit save for secret material
///     that would end up as a plain literal in the committed file — either a string field equal to a resolved
///     secret value, or a sensitive-named header (<see cref="SecretRedactor.ShouldRedact" />) whose value is a
///     literal rather than a <c>${VAR}</c> reference. The schema already forbids representing a secret directly;
///     this is the defence-in-depth scan on top.
/// </summary>
public static partial class WorkspaceSecretScanner
{
    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex EnvVarPattern();

    public static IReadOnlyList<SecretLeak> Scan(WorkspaceModel workspace, IReadOnlyCollection<string> secretValues)
    {
        var leaks = new List<SecretLeak>();
        var secrets = secretValues.Where(v => !string.IsNullOrEmpty(v)).ToHashSet(StringComparer.Ordinal);

        foreach (var connection in workspace.Connections)
        {
            var where = $"connection '{connection.Name}'";
            ScanHeaders(connection.ReflectionHeaders, $"{where} reflection header", leaks, secrets);
            ScanValue(connection.Address, $"{where} address", leaks, secrets);
            ScanValue(connection.Authority, $"{where} authority", leaks, secrets);
            ScanValue(connection.ServerName, $"{where} server name", leaks, secrets);
            ScanValue(connection.UserAgent, $"{where} user agent", leaks, secrets);
            ScanValue(connection.Notes, $"{where} notes", leaks, secrets);
        }

        foreach (var request in workspace.SavedRequests)
        {
            var where = $"saved request '{request.Name}'";
            ScanHeaders(request.Headers, $"{where} header", leaks, secrets);
            ScanValue(request.Body, $"{where} body", leaks, secrets);
        }

        foreach (var environment in workspace.Environments)
        {
            foreach (var variable in environment.Variables.Where(v => !v.IsSecret))
            {
                ScanValue(variable.Value.Literal, $"environment '{environment.Name}' variable '{variable.Name}'", leaks, secrets);
            }
        }

        return leaks;
    }

    private static void ScanHeaders(List<HeaderEntry> headers, string label, List<SecretLeak> leaks, HashSet<string> secrets)
    {
        foreach (var header in headers)
        {
            if (string.IsNullOrEmpty(header.Value))
            {
                continue;
            }

            // A sensitive-named header must reference a variable (or secret), never carry a literal value.
            if (SecretRedactor.ShouldRedact(header.Name) && !EnvVarPattern().IsMatch(header.Value))
            {
                leaks.Add(new SecretLeak(SecretLeakKind.SensitiveHeaderLiteral, $"{label} '{header.Name}'"));
            }
            else if (secrets.Contains(header.Value))
            {
                leaks.Add(new SecretLeak(SecretLeakKind.SecretValueInPlainField, $"{label} '{header.Name}'"));
            }
        }
    }

    private static void ScanValue(string? value, string label, List<SecretLeak> leaks, HashSet<string> secrets)
    {
        if (!string.IsNullOrEmpty(value) && secrets.Contains(value))
        {
            leaks.Add(new SecretLeak(SecretLeakKind.SecretValueInPlainField, label));
        }
    }
}
