using GrpCurl.Net.Commands;
using System.CommandLine;

namespace GrpCurl.Net.Tests.Unit.Commands;

public sealed class ListCommandHandlerTests
{
    [Fact]
    public void Create_ReturnsValidCommand()
    {
        // Arrange

        // Act
        var command = ListCommandHandler.Create();

        // Assert
        _ = command.ShouldNotBeNull();
        command.Name.ShouldBe("list");
    }

    [Fact]
    public void Create_HasDescription()
    {
        // Arrange

        // Act
        var command = ListCommandHandler.Create();

        // Assert
        string.IsNullOrEmpty(command.Description).ShouldBeFalse();

        command.Description.ShouldContain("List");
    }

    [Fact]
    public void Create_HasTwoArguments()
    {
        // Arrange

        // Act
        var command = ListCommandHandler.Create();

        // Assert
        command.Arguments.Count.ShouldBe(2);
    }

    [Fact]
    public void Create_HasAddressArgument()
    {
        // Arrange

        // Act
        var command = ListCommandHandler.Create();

        // Assert
        var addressArg = command.Arguments.FirstOrDefault(a => a.Name == "address");

        _ = addressArg.ShouldNotBeNull();
    }

    [Fact]
    public void Create_HasServiceArgument()
    {
        // Arrange

        // Act
        var command = ListCommandHandler.Create();

        // Assert
        var serviceArg = command.Arguments.FirstOrDefault(a => a.Name == "service");

        _ = serviceArg.ShouldNotBeNull();
    }

    [Fact]
    public void Create_AddressArgument_IsOptional()
    {
        // Arrange

        // Act
        var command = ListCommandHandler.Create();

        // Assert
        var addressArg = command.Arguments.FirstOrDefault(a => a.Name == "address");

        _ = addressArg.ShouldNotBeNull();
        addressArg.Arity.ShouldBe(ArgumentArity.ZeroOrOne);
    }

    [Fact]
    public void Create_ServiceArgument_IsOptional()
    {
        // Arrange

        // Act
        var command = ListCommandHandler.Create();

        // Assert
        var serviceArg = command.Arguments.FirstOrDefault(a => a.Name == "service");

        _ = serviceArg.ShouldNotBeNull();
        serviceArg.Arity.ShouldBe(ArgumentArity.ZeroOrOne);
    }

    [Fact]
    public void Create_HasMultipleOptions()
    {
        // Arrange

        // Act
        var command = ListCommandHandler.Create();

        // Assert
        command.Options.Count.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void Create_HasMaxTimeOption()
    {
        // Arrange

        // Act
        var command = ListCommandHandler.Create();

        // Assert
        HasOption(command, "--max-time").ShouldBeTrue();
    }

    private static bool HasOption(Command command, string name)
        => command.Options.Any(option => option.Name == name || option.Name == name.TrimStart('-'));
}
