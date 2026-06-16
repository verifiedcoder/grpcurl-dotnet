using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

public sealed class FakeProtocService : IProtocService
{
    public ProtocInfo DetectResult { get; set; } = ProtocInfo.Ok("/usr/bin/protoc", "libprotoc 3.21.12");

    public ProtocInfo VerifyResult { get; set; } = ProtocInfo.Ok("/opt/protoc", "libprotoc 25.1");

    public string? LastVerifiedPath { get; private set; }

    public int DetectCount { get; private set; }

    public int VerifyCount { get; private set; }

    public Task<ProtocInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        DetectCount++;
        return Task.FromResult(DetectResult);
    }

    public Task<ProtocInfo> VerifyAsync(string path, CancellationToken cancellationToken = default)
    {
        VerifyCount++;
        LastVerifiedPath = path;
        return Task.FromResult(VerifyResult);
    }
}
