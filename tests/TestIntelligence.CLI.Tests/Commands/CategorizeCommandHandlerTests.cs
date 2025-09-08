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
    public class CategorizeCommandHandlerTests
    {
        private readonly ILogger<CategorizeCommandHandler> _mockLogger;
        private readonly ICategorizationService _mockCategorizationService;
        private readonly IServiceProvider _mockServiceProvider;
        private readonly CommandContext _context;
        private readonly CategorizeCommandHandler _handler;

        public CategorizeCommandHandlerTests()
        {
            _mockLogger = Substitute.For<ILogger<CategorizeCommandHandler>>();
            _mockCategorizationService = Substitute.For<ICategorizationService>();
            _mockServiceProvider = Substitute.For<IServiceProvider>();
            
            // Setup service provider to return our mock categorization service
            _mockServiceProvider.GetService(typeof(ICategorizationService)).Returns(_mockCategorizationService);
            
            _context = new CommandContext(_mockServiceProvider);
            _handler = new CategorizeCommandHandler(_mockLogger);
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CategorizeCommandHandler(null!));
        }

        [Fact]
        public async Task ExecuteAsync_WithValidParameters_CallsCategorizationService()
        {
            // Arrange
            var path = "/test/path";
            var output = "/test/output";

            _context.SetParameter("path", path);
            _context.SetParameter("output", output);

            _mockCategorizationService.CategorizeAsync(path, output)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(0, result);
            await _mockCategorizationService.Received(1).CategorizeAsync(path, output);
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
        public async Task ExecuteAsync_WithoutOutput_PassesNullOutput()
        {
            // Arrange
            var path = "/test/path";
            
            _context.SetParameter("path", path);
            // Don't set output parameter

            _mockCategorizationService.CategorizeAsync(path, null)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(0, result);
            await _mockCategorizationService.Received(1).CategorizeAsync(path, null);
        }

        [Fact]
        public async Task ExecuteAsync_WhenCategorizationServiceThrows_ReturnsErrorCode()
        {
            // Arrange
            var path = "/test/path";
            
            _context.SetParameter("path", path);

            _mockCategorizationService.CategorizeAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromException(new InvalidOperationException("Categorization failed")));

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

            _mockCategorizationService.CategorizeAsync(Arg.Any<string>(), Arg.Any<string>())
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

            _mockCategorizationService.CategorizeAsync(Arg.Any<string>(), Arg.Any<string>())
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

            _mockCategorizationService.CategorizeAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromException(new System.IO.DirectoryNotFoundException("Directory not found")));

            // Act
            var result = await _handler.ExecuteAsync(_context);

            // Assert
            Assert.Equal(2, result); // Directory not found exit code
        }
    }
}