namespace GrpCurl.Net.Invocation;

/// <summary>Shared response-message recursion policy. Root depth is zero; sibling fields share a budget.</summary>
internal static class MessageNesting
{
    public const int MaxDepth = 100;
    // A message may contribute an object and a repeated-field array, plus the root container.
    public const int MaxJsonDepth = (MaxDepth + 1) * 2;

    public static void Check(int depth, string phase)
    {
        if (depth > MaxDepth) { throw new MessageNestingException(phase); }
    }
}

internal sealed class MessageNestingException(string phase)
    : InvalidOperationException($"Protobuf message nesting exceeds the limit of {MessageNesting.MaxDepth} during {phase}.")
{
    public int Limit { get; } = MessageNesting.MaxDepth;
    public string Phase { get; } = phase;
}
