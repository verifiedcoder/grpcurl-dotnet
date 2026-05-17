using GrpCurl.Net.Commands;

namespace GrpCurl.Net.Tests.Unit.Invocation;

public sealed class ConcatenatedJsonParserTests
{
    [Fact]
    public void ParseConcatenatedJsonObjects_SingleObject_ReturnsSingleItem()
    {
        // Arrange
        const string json = """{"key": "value"}""";

        // Act
        var result = InvokeCommandHandler.ParseConcatenatedJsonObjects(json);

        // Assert
        result.ShouldHaveSingleItem();
        result[0].ShouldContain("key");
        result[0].ShouldContain("value");
    }

    [Fact]
    public void ParseConcatenatedJsonObjects_TwoObjects_ReturnsTwoItems()
    {
        // Arrange
        const string json = """{"a": 1} {"b": 2}""";

        // Act
        var result = InvokeCommandHandler.ParseConcatenatedJsonObjects(json);

        // Assert
        result.Count.ShouldBe(2);
        result[0].ShouldContain("\"a\"");
        result[1].ShouldContain("\"b\"");
    }

    [Fact]
    public void ParseConcatenatedJsonObjects_ThreeObjectsNewlineSeparated_ReturnsThreeItems()
    {
        // Arrange
        const string json = """
            {"a": 1}
            {"b": 2}
            {"c": 3}
            """;

        // Act
        var result = InvokeCommandHandler.ParseConcatenatedJsonObjects(json.Trim());

        // Assert
        result.Count.ShouldBe(3);
    }

    [Fact]
    public void ParseConcatenatedJsonObjects_ObjectsWithWhitespace_ParsesCorrectly()
    {
        // Arrange
        const string json = """  {"x": 1}   {"y": 2}  """;

        // Act
        var result = InvokeCommandHandler.ParseConcatenatedJsonObjects(json);

        // Assert
        result.Count.ShouldBe(2);
    }

    [Fact]
    public void ParseConcatenatedJsonObjects_NestedObjects_ParsesCorrectly()
    {
        // Arrange
        const string json = """{"payload":{"body":"YQ=="}} {"payload":{"body":"YmI="}}""";

        // Act
        var result = InvokeCommandHandler.ParseConcatenatedJsonObjects(json);

        // Assert
        result.Count.ShouldBe(2);
        result[0].ShouldContain("YQ==");
        result[1].ShouldContain("YmI=");
    }

    [Fact]
    public void ParseConcatenatedJsonObjects_EmptyString_ReturnsEmptyList()
    {
        // Arrange
        const string json = "";

        // Act
        var result = InvokeCommandHandler.ParseConcatenatedJsonObjects(json);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void ParseConcatenatedJsonObjects_SingleEmptyObject_ReturnsSingleItem()
    {
        // Arrange
        const string json = "{}";

        // Act
        var result = InvokeCommandHandler.ParseConcatenatedJsonObjects(json);

        // Assert
        result.ShouldHaveSingleItem();
        result[0].ShouldBe("{}");
    }
}
