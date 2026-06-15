namespace GrpCurl.Net.Studio.ViewModels.Services;

/// <summary>
///     Opens an external URI (e.g. a <c>google.rpc.Help</c> link) in the user's default handler.
///     Abstracted so view models stay UI-free (SPEC-030 §1/§4); the caller confirms first.
/// </summary>
public interface ILauncherService
{
    Task<bool> LaunchUriAsync(string uri, CancellationToken cancellationToken = default);
}
