namespace GrpCurl.Net.Studio.ViewModels.Services;

public interface IHistoryStatus
{
    string? Message { get; }
}

public interface IHistoryPolicy
{
    bool AllowHistory { get; }
    bool AllowRequests { get; }
    bool AllowResponses { get; }
    string? Message { get; }
}

public sealed class HistoryStorageException(string message) : InvalidOperationException(message);
