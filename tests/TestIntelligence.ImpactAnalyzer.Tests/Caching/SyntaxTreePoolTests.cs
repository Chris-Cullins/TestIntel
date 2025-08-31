using System;
using System.IO;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.ImpactAnalyzer.Caching;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Caching
{
    public class SyntaxTreePoolTests : IDisposable
    {
        private readonly ILogger<SyntaxTreePool> _logger;
        private readonly SyntaxTreePool _pool;

        public SyntaxTreePoolTests()
        {
            _logger = Substitute.For<ILogger<SyntaxTreePool>>();
            _pool = new SyntaxTreePool(_logger);
        }

        public void Dispose()
        {
            _pool?.Dispose();
        }

        [Fact]
        public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
        {
            var action = () => new SyntaxTreePool(null!);

            action.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_WithValidLogger_ShouldCreateInstance()
        {
            using var pool = new SyntaxTreePool(_logger);

            pool.Should().NotBeNull();
        }

        [Fact]
        public void GetOrParse_WithSimpleCode_ShouldReturnSyntaxTree()
        {
            var sourceCode = "class Test { }";
            var filePath = "test.cs";

            var syntaxTree = _pool.GetOrParse(sourceCode, filePath);

            syntaxTree.Should().NotBeNull();
            syntaxTree.FilePath.Should().Be(filePath);
        }

        [Fact]
        public void GetOrParse_WithSameContent_ShouldReturnCachedTree()
        {
            var sourceCode = "class Test { }";
            var filePath = "test.cs";

            var syntaxTree1 = _pool.GetOrParse(sourceCode, filePath);
            var syntaxTree2 = _pool.GetOrParse(sourceCode, filePath);

            var stats = _pool.GetStatistics();
            stats.TotalRequests.Should().Be(2);
            stats.CacheHits.Should().Be(1);
            stats.CacheHitRatio.Should().BeApproximately(0.5, 0.01);
        }

        [Fact]
        public void GetOrParse_WithDifferentContent_ShouldCreateNewTree()
        {
            var sourceCode1 = "class Test1 { }";
            var sourceCode2 = "class Test2 { }";
            var filePath = "test.cs";

            var syntaxTree1 = _pool.GetOrParse(sourceCode1, filePath);
            var syntaxTree2 = _pool.GetOrParse(sourceCode2, filePath);

            syntaxTree1.Should().NotBeNull();
            syntaxTree2.Should().NotBeNull();
            
            var stats = _pool.GetStatistics();
            stats.NewCreations.Should().Be(2);
            stats.CacheHits.Should().Be(0);
        }

        [Fact]
        public void Return_WithValidSyntaxTree_ShouldReturnToPool()
        {
            var sourceCode = "class Test { }";
            var syntaxTree = _pool.GetOrParse(sourceCode, "test.cs");

            _pool.Return(syntaxTree);

            var stats = _pool.GetStatistics();
            stats.PoolSize.Should().Be(1);
        }

        [Fact]
        public void Return_WithNullSyntaxTree_ShouldNotThrow()
        {
            var action = () => _pool.Return(null!);

            action.Should().NotThrow();
        }

        [Fact]
        public void GetOrParse_WithPooledTree_ShouldReuseFromPool()
        {
            var sourceCode1 = "class Test1 { }";
            var sourceCode2 = "class Test2 { }";
            
            // Create and return a tree to the pool
            var syntaxTree1 = _pool.GetOrParse(sourceCode1, "test1.cs");
            _pool.Return(syntaxTree1);
            
            // Request a new tree - should use from pool
            var syntaxTree2 = _pool.GetOrParse(sourceCode2, "test2.cs");

            var stats = _pool.GetStatistics();
            stats.PoolHits.Should().BeGreaterThan(0);
        }

        [Fact]
        public void GetStatistics_WithMultipleOperations_ShouldTrackCorrectly()
        {
            var sourceCode = "class Test { }";
            
            // First request - new creation
            var tree1 = _pool.GetOrParse(sourceCode, "test.cs");
            // Second request - cache hit
            var tree2 = _pool.GetOrParse(sourceCode, "test.cs");
            // Return to pool
            _pool.Return(tree1);
            // Third request with different content - pool hit
            var tree3 = _pool.GetOrParse("class Different { }", "different.cs");

            var stats = _pool.GetStatistics();
            stats.TotalRequests.Should().Be(3);
            stats.CacheHits.Should().Be(1);
            stats.NewCreations.Should().Be(1);
            stats.PoolHits.Should().Be(1);
            stats.ReuseRatio.Should().BeApproximately(0.67, 0.01); // 2 reuses out of 3 requests
        }

        [Fact]
        public void Clear_ShouldResetAllStatistics()
        {
            var sourceCode = "class Test { }";
            var tree = _pool.GetOrParse(sourceCode, "test.cs");
            _pool.Return(tree);

            _pool.Clear();

            var stats = _pool.GetStatistics();
            stats.TotalRequests.Should().Be(0);
            stats.CacheHits.Should().Be(0);
            stats.PoolHits.Should().Be(0);
            stats.NewCreations.Should().Be(0);
            stats.CacheSize.Should().Be(0);
            stats.PoolSize.Should().Be(0);
        }

        [Fact]
        public void Pool_WithCustomOptions_ShouldRespectLimits()
        {
            var options = new SyntaxTreePoolOptions
            {
                MaxPoolSize = 2,
                MaxCacheSize = 3,
                CleanupInterval = TimeSpan.FromMilliseconds(100)
            };
            
            using var customPool = new SyntaxTreePool(_logger, options);

            // Add more trees than the pool size limit
            for (int i = 0; i < 5; i++)
            {
                var tree = customPool.GetOrParse($"class Test{i} {{ }}", $"test{i}.cs");
                customPool.Return(tree);
            }

            var stats = customPool.GetStatistics();
            stats.PoolSize.Should().BeLessOrEqualTo(2);
        }

        [Fact]
        public void Pool_WithManyDifferentFiles_ShouldRespectCacheLimit()
        {
            var options = new SyntaxTreePoolOptions
            {
                MaxPoolSize = 10,
                MaxCacheSize = 3,
                CleanupInterval = TimeSpan.FromMilliseconds(100)
            };
            
            using var customPool = new SyntaxTreePool(_logger, options);

            // Add more unique content than cache size limit
            for (int i = 0; i < 10; i++)
            {
                customPool.GetOrParse($"class Test{i} {{ }}", $"test{i}.cs");
            }

            var stats = customPool.GetStatistics();
            stats.CacheSize.Should().BeLessOrEqualTo(3);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("incomplete syntax {")]
        [InlineData("namespace Test { class Incomplete")]
        public void GetOrParse_WithInvalidOrEmptyCode_ShouldStillReturnTree(string sourceCode)
        {
            var result = _pool.GetOrParse(sourceCode, "test.cs");

            result.Should().NotBeNull();
            // Roslyn can parse even invalid syntax into a syntax tree with errors
        }

        [Fact]
        public void GetOrParse_WithLargeFile_ShouldHandleGracefully()
        {
            // Generate a large source file
            var largeSourceCode = "using System;\nnamespace LargeTest {\n";
            for (int i = 0; i < 1000; i++)
            {
                largeSourceCode += $"public class Test{i} {{ public void Method{i}() {{ Console.WriteLine(\"{i}\"); }} }}\n";
            }
            largeSourceCode += "}";

            var result = _pool.GetOrParse(largeSourceCode, "large.cs");

            result.Should().NotBeNull();
            result.Length.Should().BeGreaterThan(10000);
        }

        [Fact]
        public void GetStatistics_InitialState_ShouldReturnZeros()
        {
            var stats = _pool.GetStatistics();

            stats.TotalRequests.Should().Be(0);
            stats.CacheHits.Should().Be(0);
            stats.PoolHits.Should().Be(0);
            stats.NewCreations.Should().Be(0);
            stats.CacheSize.Should().Be(0);
            stats.PoolSize.Should().Be(0);
            stats.CacheHitRatio.Should().Be(0);
            stats.PoolHitRatio.Should().Be(0);
            stats.ReuseRatio.Should().Be(0);
        }

        [Fact]
        public void Dispose_ShouldCleanupResources()
        {
            var sourceCode = "class Test { }";
            var tree = _pool.GetOrParse(sourceCode, "test.cs");
            _pool.Return(tree);

            _pool.Dispose();

            // Should not throw - dispose should cleanup everything
            var stats = _pool.GetStatistics();
            stats.Should().NotBeNull(); // Statistics should still be accessible
        }

        [Fact]
        public async System.Threading.Tasks.Task Pool_UnderHighConcurrency_ShouldRemainThreadSafe()
        {
            const int threadCount = 10;
            const int operationsPerThread = 50;
            var tasks = new System.Threading.Tasks.Task[threadCount];

            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks[t] = System.Threading.Tasks.Task.Run(() =>
                {
                    for (int i = 0; i < operationsPerThread; i++)
                    {
                        var sourceCode = $"class Test{threadId}_{i} {{ }}";
                        var tree = _pool.GetOrParse(sourceCode, $"test{threadId}_{i}.cs");
                        
                        // Randomly return some trees to the pool
                        if (i % 3 == 0)
                        {
                            _pool.Return(tree);
                        }
                    }
                });
            }

            await System.Threading.Tasks.Task.WhenAll(tasks);

            var stats = _pool.GetStatistics();
            stats.TotalRequests.Should().Be(threadCount * operationsPerThread);
            // Should not have thrown any exceptions
        }
    }

    public class SyntaxTreePoolOptionsTests
    {
        [Fact]
        public void DefaultOptions_ShouldHaveReasonableDefaults()
        {
            var options = new SyntaxTreePoolOptions();

            options.MaxPoolSize.Should().Be(100);
            options.MaxCacheSize.Should().Be(500);
            options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(5));
        }

        [Fact]
        public void Options_ShouldBeConfigurable()
        {
            var options = new SyntaxTreePoolOptions
            {
                MaxPoolSize = 50,
                MaxCacheSize = 200,
                CleanupInterval = TimeSpan.FromMinutes(2)
            };

            options.MaxPoolSize.Should().Be(50);
            options.MaxCacheSize.Should().Be(200);
            options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(2));
        }
    }

    public class SyntaxTreePoolStatisticsTests
    {
        [Fact]
        public void CacheHitRatio_WithZeroRequests_ShouldReturnZero()
        {
            var stats = new SyntaxTreePoolStatistics();

            stats.CacheHitRatio.Should().Be(0);
        }

        [Fact]
        public void CacheHitRatio_WithRequests_ShouldCalculateCorrectly()
        {
            var stats = new SyntaxTreePoolStatistics
            {
                TotalRequests = 10,
                CacheHits = 3
            };

            stats.CacheHitRatio.Should().BeApproximately(0.3, 0.001);
        }

        [Fact]
        public void PoolHitRatio_WithRequests_ShouldCalculateCorrectly()
        {
            var stats = new SyntaxTreePoolStatistics
            {
                TotalRequests = 10,
                PoolHits = 4
            };

            stats.PoolHitRatio.Should().BeApproximately(0.4, 0.001);
        }

        [Fact]
        public void ReuseRatio_WithRequests_ShouldCalculateCorrectly()
        {
            var stats = new SyntaxTreePoolStatistics
            {
                TotalRequests = 10,
                CacheHits = 3,
                PoolHits = 2
            };

            stats.ReuseRatio.Should().BeApproximately(0.5, 0.001); // (3+2)/10
        }
    }
}