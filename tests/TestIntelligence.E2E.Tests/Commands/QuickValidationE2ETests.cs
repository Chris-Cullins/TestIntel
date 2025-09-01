using FluentAssertions;
using TestIntelligence.E2E.Tests.Helpers;
using Xunit;

namespace TestIntelligence.E2E.Tests.Commands;

[Collection("E2E Tests")]
public class QuickValidationE2ETests
{
    [Fact]
    public async Task CLI_Version_Command_Works()
    {
        // Act
        var result = await CliTestHelper.RunCliCommandAsync("version", "");

        // Assert
        result.Success.Should().BeTrue($"Command should succeed. Error: {result.StandardError}");
        result.StandardOutput.Should().Contain("TestIntelligence CLI");
        result.StandardOutput.Should().Contain("v1.0.0.0");
    }

    [Fact]
    public async Task CLI_Help_Command_Works()
    {
        // Act
        var result = await CliTestHelper.RunCliCommandAsync("--help", "");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("TestIntelligence - Intelligent test analysis and selection tool");
        result.StandardOutput.Should().Contain("analyze");
        result.StandardOutput.Should().Contain("find-tests");
        result.StandardOutput.Should().Contain("callgraph");
    }

    [Fact]
    public async Task CLI_InvalidCommand_ShowsError()
    {
        // Act
        var result = await CliTestHelper.RunCliCommandAsync("invalid-command", "");

        // Assert
        result.Success.Should().BeFalse();
        result.StandardError.Should().Contain("'invalid-command' was not matched");
    }
}