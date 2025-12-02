using GrpCurl.Net.Commands;

namespace GrpCurl.Net.Tests.Unit.Commands;

public sealed class CommandConstantsTests
{
    [Fact]
    public void CommandFailed_HasExpectedValue()
    {
        // Arrange & Act & Assert
        CommandConstants.CommandFailed.ShouldBe("Command failed");
    }

    [Fact]
    public void Suggestions_StartsWithDimMarkup()
    {
        // Arrange & Act & Assert
        CommandConstants.Suggestions.ShouldStartWith("[dim]");
    }

    [Fact]
    public void Suggestions_ContainsSuggestionsWord()
    {
        // Arrange & Act & Assert
        CommandConstants.Suggestions.ShouldContain("Suggestions");
    }

    [Fact]
    public void RequestSerialisation_IsNotEmpty()
    {
        // Arrange & Act & Assert
        string.IsNullOrEmpty(CommandConstants.RequestSerialisation).ShouldBeFalse();
    }

    [Fact]
    public void NetworkRoundTrip_HasExpectedValue()
    {
        // Arrange & Act & Assert
        CommandConstants.NetworkRoundTrip.ShouldBe("Network Round-Trip");
    }

    [Fact]
    public void ResponseDeserialization_HasExpectedValue()
    {
        // Arrange & Act & Assert
        CommandConstants.ResponseDeserialization.ShouldBe("Response Deserialization");
    }

    [Fact]
    public void AllConstants_AreNotNull()
    {
        // Arrange & Act & Assert
        CommandConstants.CommandFailed.ShouldNotBeNull();
        CommandConstants.Suggestions.ShouldNotBeNull();
        CommandConstants.RequestSerialisation.ShouldNotBeNull();
        CommandConstants.NetworkRoundTrip.ShouldNotBeNull();
        CommandConstants.ResponseDeserialization.ShouldNotBeNull();
    }
}
