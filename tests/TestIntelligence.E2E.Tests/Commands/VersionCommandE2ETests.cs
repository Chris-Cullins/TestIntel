using FluentAssertions;
using TestIntelligence.E2E.Tests.Helpers;
using Xunit;

namespace TestIntelligence.E2E.Tests.Commands;

[Collection("E2E Tests")]
public class VersionCommandE2ETests
{
    [Fact]
    public async Task Version_Command_ReturnsVersionInformation()
    {
        // Act
        var result = await CliTestHelper.RunCliCommandAsync("version", "");

        // Assert
        result.Success.Should().BeTrue($"Command should succeed. Error: {result.StandardError}");
        result.StandardOutput.Should().Contain("TestIntelligence CLI");
        result.StandardOutput.Should().Contain("Intelligent test analysis and selection tool");
        result.StandardOutput.Should().MatchRegex(@"v\d+\.\d+\.\d+");
    }

    [Fact]
    public async Task Version_CommandExists_InHelpOutput()
    {
        // Act
        var result = await CliTestHelper.RunCliCommandAsync("--help", "");

        // Assert
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("version");
        result.StandardOutput.Should().Contain("Show version information");
    }
}