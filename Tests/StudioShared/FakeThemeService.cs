using CommunityToolkit.Mvvm.ComponentModel;
using GrpCurl.Net.Studio.ViewModels.Models;
using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.TestSupport;

public sealed partial class FakeThemeService : ObservableObject, IThemeService
{
    [ObservableProperty]
    private AppTheme _current = AppTheme.System;

    public int SetCount { get; private set; }

    public Task SetAsync(AppTheme theme, CancellationToken cancellationToken = default)
    {
        SetCount++;
        Current = theme;
        return Task.CompletedTask;
    }
}
