namespace GrpCurl.Net.Utilities;

/// <summary>
///     Thin shim over <see cref="Console.IsInputRedirected" /> so tests can override the
///     value without redirecting actual stdin. xUnit's runner redirects stdin, which
///     makes the underlying property unreliable for test assertions.
/// </summary>
internal static class ConsoleEnvironment
{
    private static Func<bool>? _isInputRedirectedOverride;

    /// <summary>
    ///     <c>true</c> if stdin is redirected (piped or from a file); <c>false</c> when
    ///     stdin is attached to an interactive terminal.
    /// </summary>
    public static bool IsInputRedirected
        => _isInputRedirectedOverride?.Invoke() ?? Console.IsInputRedirected;

    /// <summary>
    ///     Sets a test override for <see cref="IsInputRedirected" />. Pass <c>null</c> to
    ///     restore default behaviour.
    /// </summary>
    internal static void SetIsInputRedirectedOverride(Func<bool>? overrideFn)
        => _isInputRedirectedOverride = overrideFn;

    private static Func<Stream>? _standardInputOverride;

    /// <summary>
    ///     Opens the standard input stream, honouring any test override. xUnit's runner
    ///     owns the real stdin handle, so tests substitute an in-memory stream instead.
    /// </summary>
    public static Stream OpenStandardInput()
        => _standardInputOverride?.Invoke() ?? Console.OpenStandardInput();

    /// <summary>
    ///     Sets a test override for <see cref="OpenStandardInput" />. Pass <c>null</c> to
    ///     restore default behaviour.
    /// </summary>
    internal static void SetStandardInputOverride(Func<Stream>? overrideFn)
        => _standardInputOverride = overrideFn;
}