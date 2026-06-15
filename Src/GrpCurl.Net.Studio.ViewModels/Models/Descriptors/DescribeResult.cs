namespace GrpCurl.Net.Studio.ViewModels.Models.Descriptors;

/// <summary>Outcome of describing a symbol: a structured <see cref="SymbolDescription" />, or an error.</summary>
public sealed record DescribeResult
{
    private DescribeResult(bool ok, SymbolDescription? symbol, DescriptorLoadError? error)
    {
        Ok = ok;
        Symbol = symbol;
        Error = error;
    }

    public bool Ok { get; }
    public SymbolDescription? Symbol { get; }
    public DescriptorLoadError? Error { get; }

    public static DescribeResult Success(SymbolDescription symbol) => new(true, symbol, null);
    public static DescribeResult Failure(DescriptorLoadError error) => new(false, null, error);
}
