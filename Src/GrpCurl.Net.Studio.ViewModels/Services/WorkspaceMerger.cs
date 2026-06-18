using GrpCurl.Net.Studio.ViewModels.Models.Connections;

namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Merges an imported workspace into the current one (FR-164). Imported connections, TLS profiles, and
///     environments are <em>added</em> — nothing in the current workspace is overwritten. Each imported item
///     gets a fresh id (so ids never collide), and a name that already exists gets an " (imported)" suffix.
///     Connection→profile references are remapped to the imported profiles' new ids. Imported secrets are not
///     copied (the source keychain is unavailable, SEC-041); the summary reports how many must be re-entered.
/// </summary>
public static class WorkspaceMerger
{
    public static (WorkspaceModel Merged, WorkspaceMergeSummary Summary) Merge(WorkspaceModel current, WorkspaceModel incoming)
    {
        var merged = current.Copy();

        var connectionNames = new HashSet<string>(merged.Connections.Select(c => c.Name), StringComparer.Ordinal);
        var profileNames = new HashSet<string>(merged.TlsProfiles.Select(p => p.Name), StringComparer.Ordinal);
        var environmentNames = new HashSet<string>(merged.Environments.Select(e => e.Name), StringComparer.Ordinal);

        var missingSecrets = new List<MissingSecret>();

        // Profiles first: imported connections reference them, so build the old→new id map here.
        var profileIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var addedProfiles = new List<string>();

        foreach (var profile in incoming.TlsProfiles)
        {
            var newId = NewId();
            profileIdMap[profile.Id] = newId;
            profile.Id = newId;
            profile.Name = Dedup(profile.Name, profileNames);

            if (!string.IsNullOrWhiteSpace(profile.ClientCertPasswordSecretRef))
            {
                missingSecrets.Add(new MissingSecret(
                    $"TLS profile '{profile.Name}' — client-certificate password", profile.ClientCertPasswordSecretRef));
            }

            merged.TlsProfiles.Add(profile);
            addedProfiles.Add(profile.Name);
        }

        var addedConnections = new List<string>();

        foreach (var connection in incoming.Connections)
        {
            connection.Id = NewId();
            connection.Name = Dedup(connection.Name, connectionNames);

            // Re-point the TLS reference at the imported profile's new id; drop a reference to a profile
            // that wasn't imported rather than silently binding to an unrelated local one.
            connection.TlsProfileId = connection.TlsProfileId is { } oldRef && profileIdMap.TryGetValue(oldRef, out var mapped)
                ? mapped
                : null;

            merged.Connections.Add(connection);
            addedConnections.Add(connection.Name);
        }

        var addedEnvironments = new List<string>();

        foreach (var environment in incoming.Environments)
        {
            environment.Id = NewId();
            environment.Name = Dedup(environment.Name, environmentNames);

            foreach (var variable in environment.Variables.Where(v => v.IsSecret && v.Value.SecretRef is not null))
            {
                missingSecrets.Add(new MissingSecret(
                    $"Environment '{environment.Name}' — variable '{variable.Name}'", variable.Value.SecretRef!));
            }

            merged.Environments.Add(environment);
            addedEnvironments.Add(environment.Name);
        }

        return (merged, new WorkspaceMergeSummary(addedConnections, addedProfiles, addedEnvironments, missingSecrets));
    }

    private static string NewId() => Guid.NewGuid().ToString();

    /// <summary>Returns <paramref name="name" /> or, if already taken, the first free " (imported[ N])" variant.</summary>
    private static string Dedup(string name, HashSet<string> taken)
    {
        var candidate = taken.Contains(name) ? $"{name} (imported)" : name;

        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{name} (imported {n})";
        }

        taken.Add(candidate);
        return candidate;
    }
}

/// <summary>One secret an import will leave dangling locally (SEC-041): a display name + the keyref to supply a value for.</summary>
public sealed record MissingSecret(string DisplayName, string KeyRef);

/// <summary>
///     What a <see cref="WorkspaceMerger.Merge" /> will add, for the pre-merge confirmation (FR-164) and for
///     surfacing the secrets the user can supply inline afterwards (SEC-041).
/// </summary>
public sealed record WorkspaceMergeSummary(
    IReadOnlyList<string> Connections,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<string> Environments,
    IReadOnlyList<MissingSecret> MissingSecrets)
{
    /// <summary>The number of secret values the import can't carry (they live only in the source machine's keychain).</summary>
    public int SecretsToReenter => MissingSecrets.Count;

    public int TotalAdded => Connections.Count + Profiles.Count + Environments.Count;

    public bool IsEmpty => TotalAdded == 0;

    /// <summary>A human-readable pre-merge summary for the confirmation dialog.</summary>
    public string Describe()
    {
        if (IsEmpty)
        {
            return "The selected workspace has nothing to import.";
        }

        var lines = new List<string>();
        Append(lines, "connection", Connections);
        Append(lines, "TLS profile", Profiles);
        Append(lines, "environment", Environments);

        if (SecretsToReenter > 0)
        {
            lines.Add(
                $"\n{SecretsToReenter} secret value(s) are not imported (they live only in the source machine's "
                + "keychain) and must be re-entered in the relevant editor.");
        }

        return string.Join("\n", lines);
    }

    private static void Append(List<string> lines, string noun, IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return;
        }

        lines.Add($"{names.Count} {noun}{(names.Count == 1 ? string.Empty : "s")}: {string.Join(", ", names)}");
    }
}
