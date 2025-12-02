using GrpCurl.Net.Commands;
using System.CommandLine;

namespace GrpCurl.Net.Tests.Unit.Commands;

public sealed class InvokeCommandTests
{
    [Fact]
    public void Create_ReturnsValidCommand()
    {
        // Arrange & Act
        var command = InvokeCommandHandler.Create();

        // Assert
        command.ShouldNotBeNull();
        command.Name.ShouldBe("invoke");
    }

    [Fact]
    public void Create_HasDescription()
    {
        // Arrange & Act
        var command = InvokeCommandHandler.Create();

        // Assert
        string.IsNullOrEmpty(command.Description).ShouldBeFalse();
        command.Description.ShouldContain("Invoke");
    }

    [Fact]
    public void Create_HasTwoArguments()
    {
        // Arrange & Act
        var command = InvokeCommandHandler.Create();

        // Assert
        command.Arguments.Count.ShouldBe(2);
    }

    [Fact]
    public void Create_HasAddressArgument()
    {
        // Arrange & Act
        var command = InvokeCommandHandler.Create();

        // Assert
        var addressArg = command.Arguments.FirstOrDefault(a => a.Name == "address");
        addressArg.ShouldNotBeNull();
    }

    [Fact]
    public void Create_HasMethodArgument()
    {
        // Arrange & Act
        var command = InvokeCommandHandler.Create();

        // Assert
        var methodArg = command.Arguments.FirstOrDefault(a => a.Name == "method");
        methodArg.ShouldNotBeNull();
    }

    [Fact]
    public void Create_AddressArgument_IsRequired()
    {
        // Arrange & Act
        var command = InvokeCommandHandler.Create();

        // Assert
        var addressArg = command.Arguments.FirstOrDefault(a => a.Name == "address");
        addressArg.ShouldNotBeNull();
        addressArg.Arity.ShouldBe(ArgumentArity.ExactlyOne);
    }

    [Fact]
    public void Create_MethodArgument_IsRequired()
    {
        // Arrange & Act
        var command = InvokeCommandHandler.Create();

        // Assert
        var methodArg = command.Arguments.FirstOrDefault(a => a.Name == "method");
        methodArg.ShouldNotBeNull();
        methodArg.Arity.ShouldBe(ArgumentArity.ExactlyOne);
    }

    [Fact]
    public void Create_HasMultipleOptions()
    {
        // Arrange & Act
        var command = InvokeCommandHandler.Create();

        // Assert
        command.Options.Count.ShouldBeGreaterThanOrEqualTo(15);
    }
}
