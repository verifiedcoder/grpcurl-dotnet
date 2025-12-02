using GrpCurl.Net.Commands;
using System.CommandLine;

namespace GrpCurl.Net.Tests.Unit.Commands;

public sealed class DescribeCommandTests
{
    [Fact]
    public void Create_ReturnsValidCommand()
    {
        // Arrange & Act
        var command = DescribeCommandHandler.Create();

        // Assert
        command.ShouldNotBeNull();
        command.Name.ShouldBe("describe");
    }

    [Fact]
    public void Create_HasDescription()
    {
        // Arrange & Act
        var command = DescribeCommandHandler.Create();

        // Assert
        string.IsNullOrEmpty(command.Description).ShouldBeFalse();
        command.Description.ShouldContain("Describe");
    }

    [Fact]
    public void Create_HasTwoArguments()
    {
        // Arrange & Act
        var command = DescribeCommandHandler.Create();

        // Assert
        command.Arguments.Count.ShouldBe(2);
    }

    [Fact]
    public void Create_HasAddressArgument()
    {
        // Arrange & Act
        var command = DescribeCommandHandler.Create();

        // Assert
        var addressArg = command.Arguments.FirstOrDefault(a => a.Name == "address");
        addressArg.ShouldNotBeNull();
    }

    [Fact]
    public void Create_HasSymbolArgument()
    {
        // Arrange & Act
        var command = DescribeCommandHandler.Create();

        // Assert
        var symbolArg = command.Arguments.FirstOrDefault(a => a.Name == "symbol");
        symbolArg.ShouldNotBeNull();
    }

    [Fact]
    public void Create_AddressArgument_IsOptional()
    {
        // Arrange & Act
        var command = DescribeCommandHandler.Create();

        // Assert
        var addressArg = command.Arguments.FirstOrDefault(a => a.Name == "address");
        addressArg.ShouldNotBeNull();
        addressArg.Arity.ShouldBe(ArgumentArity.ZeroOrOne);
    }

    [Fact]
    public void Create_SymbolArgument_IsOptional()
    {
        // Arrange & Act
        var command = DescribeCommandHandler.Create();

        // Assert
        var symbolArg = command.Arguments.FirstOrDefault(a => a.Name == "symbol");
        symbolArg.ShouldNotBeNull();
        symbolArg.Arity.ShouldBe(ArgumentArity.ZeroOrOne);
    }

    [Fact]
    public void Create_HasMultipleOptions()
    {
        // Arrange & Act
        var command = DescribeCommandHandler.Create();

        // Assert
        command.Options.Count.ShouldBeGreaterThanOrEqualTo(10);
    }
}
