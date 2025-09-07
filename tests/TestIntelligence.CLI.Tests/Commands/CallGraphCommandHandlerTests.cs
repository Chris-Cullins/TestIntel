using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.CLI.Commands;
using TestIntelligence.CLI.Services;
using Xunit;

namespace TestIntelligence.CLI.Tests.Commands
{
    public class CallGraphCommandHandlerTests
    {
        private readonly ILogger<CallGraphCommandHandler> _mockLogger;
        private readonly ICallGraphService _mockCallGraphService;
        private readonly IServiceProvider _mockServiceProvider;
        private readonly CommandContext _context;
        private readonly CallGraphCommandHandler _handler;

        public CallGraphCommandHandlerTests()
        {
            _mockLogger = Substitute.For<ILogger<CallGraphCommandHandler>>();
            _mockCallGraphService = Substitute.For<ICallGraphService>();
            _mockServiceProvider = Substitute.For<IServiceProvider>();
            
            // Setup service provider to return our mock call graph service
            _mockServiceProvider.GetService(typeof(ICallGraphService)).Returns(_mockCallGraphService);
            
            _context = new CommandContext(_mockServiceProvider);
            _handler = new CallGraphCommandHandler(_mockLogger);
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CallGraphCommandHandler(null!));
        }

        [Fact]
        public async Task ExecuteAsync_WithValidParameters_CallsCallGraphService()
        {
            // Arrange
            var path = "/test/path";
            var output = "/test/output";
            var format = "json";
            var verbose = true;
            var maxMethods = 100;

            _context.SetParameter("path", path);
            _context.SetParameter("output", output);
            _context.SetParameter("format", format);
            _context.SetParameter("verbose", verbose);
            _context.SetParameter("max-methods", maxMethods);

            _mockCallGraphService.AnalyzeCallGraphAsync(path, output, format, verbose, maxMethods)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(0, result);
            await _mockCallGraphService.Received(1).AnalyzeCallGraphAsync(path, output, format, verbose, maxMethods);
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

            _mockCallGraphService.AnalyzeCallGraphAsync(path, null, "text", false, null)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(0, result);
            await _mockCallGraphService.Received(1).AnalyzeCallGraphAsync(path, null, "text", false, null);
        }

        [Fact]
        public async Task ExecuteAsync_WithoutMaxMethods_PassesNull()
        {
            // Arrange
            var path = "/test/path";
            
            _context.SetParameter("path", path);
            // Don't set max-methods parameter

            _mockCallGraphService.AnalyzeCallGraphAsync(path, null, "text", false, null)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(0, result);
            await _mockCallGraphService.Received(1).AnalyzeCallGraphAsync(path, null, "text", false, null);
        }

        [Fact]
        public async Task ExecuteAsync_WhenCallGraphServiceThrows_ReturnsErrorCode()
        {
            // Arrange
            var path = "/test/path";
            
            _context.SetParameter("path", path);

            _mockCallGraphService.AnalyzeCallGraphAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int?>())
                .ThrowsAsync(new InvalidOperationException("Call graph analysis failed"));

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

            _mockCallGraphService.AnalyzeCallGraphAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int?>())
                .ThrowsAsync(new OperationCanceledException());

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

            _mockCallGraphService.AnalyzeCallGraphAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int?>())
                .ThrowsAsync(new System.IO.FileNotFoundException("File not found", path));

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(2, result); // File not found exit code
        }
    }
}