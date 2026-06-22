using GrpCurl.Net.Studio.ViewModels.Documents;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class JsonTextTests
{
    [Fact]
    public void Pretty_prints_compact_json_with_indentation()
    {
        JsonText.TryPrettyPrint("{\"a\":1,\"b\":[2,3]}", out var formatted).ShouldBeTrue();

        formatted.ShouldContain("\n");
        formatted.ShouldContain("  \"a\": 1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("nope")]
    public void Leaves_blank_or_invalid_input_untouched(string input)
    {
        JsonText.TryPrettyPrint(input, out var formatted).ShouldBeFalse();
        formatted.ShouldBeEmpty();
    }

    [Fact]
    public void Reports_no_change_when_already_formatted()
    {
        _ = JsonText.TryPrettyPrint("{\"a\":1}", out var once);

        // Re-formatting the already-indented form is a no-op (so the editor isn't dirtied needlessly).
        JsonText.TryPrettyPrint(once, out _).ShouldBeFalse();
    }

    [Fact]
    public void Tolerates_comments_and_trailing_commas()
    {
        JsonText.TryPrettyPrint("{\"a\":1,}", out var formatted).ShouldBeTrue();
        formatted.ShouldContain("\"a\": 1");
    }
}
