using GrpCurl.Net.Studio.ViewModels.Services;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class JsonMessageSplitterTests
{
    [Fact]
    public void Splits_a_json_array_into_elements()
        => JsonMessageSplitter.Split("""[{ "a": 1 }, { "b": 2 }]""").Count.ShouldBe(2);

    [Fact]
    public void Splits_concatenated_objects()
    {
        var parts = JsonMessageSplitter.Split("""{ "a": 1 } { "b": 2 } { "c": 3 }""");

        parts.Count.ShouldBe(3);
        parts[1].ShouldContain("\"b\"");
    }

    [Fact]
    public void Respects_braces_inside_strings()
    {
        var parts = JsonMessageSplitter.Split("""{ "a": "}{" } { "b": 2 }""");

        parts.Count.ShouldBe(2);
        parts[0].ShouldContain("}{");
    }

    [Fact]
    public void A_single_object_returns_one_element()
        => JsonMessageSplitter.Split("""{ "a": 1 }""").ShouldHaveSingleItem();

    [Fact]
    public void Empty_input_returns_nothing()
        => JsonMessageSplitter.Split("   ").ShouldBeEmpty();
}
