using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.CLI.Commands;
using TestIntelligence.CLI.Services;
using Xunit;

namespace TestIntelligence.CLI.Tests.Commands
{
    public class AnalyzeCommandHandlerTests
    {
        private readonly ILogger<AnalyzeCommandHandler> _mockLogger;
        private readonly IAnalysisService _mockAnalysisService;
        private readonly IServiceProvider _mockServiceProvider;
        private readonly CommandContext _context;
        private readonly AnalyzeCommandHandler _handler;

        public AnalyzeCommandHandlerTests()
        {
            _mockLogger = Substitute.For<ILogger<AnalyzeCommandHandler>>();
            _mockAnalysisService = Substitute.For<IAnalysisService>();
            _mockServiceProvider = Substitute.For<IServiceProvider>();
            
            // Setup service provider to return our mock analysis service
            _mockServiceProvider.GetService(typeof(IAnalysisService)).Returns(_mockAnalysisService);
            
            _context = new CommandContext(_mockServiceProvider);
            _handler = new AnalyzeCommandHandler(_mockLogger);
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new AnalyzeCommandHandler(null!));
        }

        [Fact]
        public async Task ExecuteAsync_WithValidParameters_CallsAnalysisService()
        {
            // Arrange
            var path = "/test/path";
            var output = "/test/output";
            var format = "json";
            var verbose = true;

            _context.SetParameter("path", path);
            _context.SetParameter("output", output);
            _context.SetParameter("format", format);
            _context.SetParameter("verbose", verbose);

            _mockAnalysisService.AnalyzeAsync(path, output, format, verbose)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(0, result);
            await _mockAnalysisService.Received(1).AnalyzeAsync(path, output, format, verbose);
        }

        [Fact]
        public async Task ExecuteAsync_WithMissingPath_ReturnsErrorCode()
        {
            // Arrange
            // Don't set path parameter
            _context.SetParameter("output", "/test/output");

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(1, result); // Error exit code
        }

        [Fact]
        public async Task ExecuteAsync_WithNullPath_ReturnsErrorCode()
        {
            // Arrange
            _context.SetParameter("path", null!);

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(1, result); // Error exit code
        }

        [Fact]
        public async Task ExecuteAsync_WithEmptyPath_ReturnsErrorCode()
        {
            // Arrange
            _context.SetParameter("path", string.Empty);

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(1, result); // Error exit code
        }

        [Fact]
        public async Task ExecuteAsync_WithDefaultFormat_UsesTextFormat()
        {
            // Arrange
            var path = "/test/path";
            
            _context.SetParameter("path", path);
            // Don't set format parameter - should default to "text"
            _context.SetParameter("verbose", false);

            _mockAnalysisService.AnalyzeAsync(path, null, "text", false)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(0, result);
            await _mockAnalysisService.Received(1).AnalyzeAsync(path, null, "text", false);
        }

        [Fact]
        public async Task ExecuteAsync_WhenAnalysisServiceThrows_ReturnsErrorCode()
        {
            // Arrange
            var path = "/test/path";
            
            _context.SetParameter("path", path);

            _mockAnalysisService.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(Task.FromException(new InvalidOperationException("Analysis failed")));

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(1, result); // Error exit code
        }

        [Fact]
        public async Task ExecuteAsync_WhenCancelled_ReturnsCancelledCode()
        {
            // Arrange
            var path = "/test/path";
            var cancellationToken = new CancellationToken(true);
            
            _context.SetParameter("path", path);

            _mockAnalysisService.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(Task.FromException(new OperationCanceledException()));

            // Act
            var result = await _handler.ExecuteAsync(_context, cancellationToken);

            // Assert
            Assert.Equal(130, result); // Cancelled exit code
        }

        [Fact]
        public async Task ExecuteAsync_WhenFileNotFound_ReturnsFileNotFoundCode()
        {
            // Arrange
            var path = "/nonexistent/path";
            
            _context.SetParameter("path", path);

            _mockAnalysisService.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(Task.FromException(new System.IO.FileNotFoundException("File not found", path)));

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(2, result); // File not found exit code
        }

        [Fact]
        public async Task ExecuteAsync_WhenDirectoryNotFound_ReturnsDirectoryNotFoundCode()
        {
            // Arrange
            var path = "/nonexistent/directory";
            
            _context.SetParameter("path", path);

            _mockAnalysisService.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(Task.FromException(new System.IO.DirectoryNotFoundException("Directory not found")));

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(2, result); // Directory not found exit code
        }

        [Fact]
        public async Task ExecuteAsync_WhenUnauthorizedAccess_ReturnsPermissionDeniedCode()
        {
            // Arrange
            var path = "/restricted/path";
            
            _context.SetParameter("path", path);

            _mockAnalysisService.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(Task.FromException(new UnauthorizedAccessException("Access denied")));

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(13, result); // Permission denied exit code
        }
    }
}