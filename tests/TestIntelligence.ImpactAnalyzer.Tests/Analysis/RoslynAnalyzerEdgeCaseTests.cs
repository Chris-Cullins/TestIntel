using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TestIntelligence.ImpactAnalyzer.Analysis;
using Xunit;

namespace TestIntelligence.ImpactAnalyzer.Tests.Analysis
{
    public class RoslynAnalyzerEdgeCaseTests : IDisposable
    {
        private readonly RoslynAnalyzerV2 _analyzer;
        private readonly ILogger<RoslynAnalyzerV2> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly string _tempDirectory;

        public RoslynAnalyzerEdgeCaseTests()
        {
            _loggerFactory = Substitute.For<ILoggerFactory>();
            _logger = Substitute.For<ILogger<RoslynAnalyzerV2>>();
            _loggerFactory.CreateLogger<RoslynAnalyzerV2>().Returns(_logger);
            SetupMockLoggers();
            
            _analyzer = new RoslynAnalyzerV2(_logger, _loggerFactory);
            _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            _analyzer.Dispose();
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }

        private void SetupMockLoggers()
        {
            _loggerFactory.CreateLogger<SolutionParser>().Returns(Substitute.For<ILogger<SolutionParser>>());
            _loggerFactory.CreateLogger<ProjectParser>().Returns(Substitute.For<ILogger<ProjectParser>>());
            _loggerFactory.CreateLogger<DependencyGraphBuilder>().Returns(Substitute.For<ILogger<DependencyGraphBuilder>>());
            _loggerFactory.CreateLogger<SolutionWorkspaceBuilder>().Returns(Substitute.For<ILogger<SolutionWorkspaceBuilder>>());
            _loggerFactory.CreateLogger<CompilationManager>().Returns(Substitute.For<ILogger<CompilationManager>>());
            _loggerFactory.CreateLogger<SymbolResolutionEngine>().Returns(Substitute.For<ILogger<SymbolResolutionEngine>>());
            _loggerFactory.CreateLogger<CallGraphBuilderV2>().Returns(Substitute.For<ILogger<CallGraphBuilderV2>>());
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithGenericMethods_ShouldHandleCorrectly()
        {
            var sourceCode = @"
using System;
using System.Collections.Generic;

namespace TestNamespace
{
    public class GenericTestClass<T>
    {
        public void GenericMethod<U>(T item, U parameter)
        {
            Console.WriteLine(item?.ToString() + parameter?.ToString());
        }

        public T ProcessItem(T item)
        {
            return item;
        }
    }

    public class ConsumerClass
    {
        public void UseGenerics()
        {
            var stringClass = new GenericTestClass<string>();
            stringClass.GenericMethod(""test"", 123);
            
            var intClass = new GenericTestClass<int>();
            var result = intClass.ProcessItem(42);
        }
    }
}";

            var filePath = CreateTempFile("GenericTest.cs", sourceCode);
            var result = await _analyzer.BuildCallGraphAsync(new[] { filePath });

            result.Should().NotBeNull();
            result.GetAllMethods().Should().NotBeEmpty();
            
            // Should identify generic method calls
            var methods = result.GetAllMethods().ToList();
            methods.Should().Contain(m => m.Contains("GenericMethod"));
            methods.Should().Contain(m => m.Contains("ProcessItem"));
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithInterfaceImplementations_ShouldDetectPolymorphism()
        {
            var sourceCode = @"
using System;

namespace TestNamespace
{
    public interface IService
    {
        void Execute();
    }

    public class ServiceA : IService
    {
        public void Execute()
        {
            Console.WriteLine(""ServiceA executed"");
        }
    }

    public class ServiceB : IService
    {
        public void Execute()
        {
            Console.WriteLine(""ServiceB executed"");
        }
    }

    public class Client
    {
        public void UseService(IService service)
        {
            service.Execute(); // Polymorphic call
        }

        public void TestBothServices()
        {
            UseService(new ServiceA());
            UseService(new ServiceB());
        }
    }
}";

            var filePath = CreateTempFile("PolymorphismTest.cs", sourceCode);
            var result = await _analyzer.BuildCallGraphAsync(new[] { filePath });

            result.Should().NotBeNull();
            
            // Should detect both implementations
            var executeMethodCalls = result.GetAllMethods()
                .Where(m => m.Contains("Execute"))
                .ToList();
            
            executeMethodCalls.Should().NotBeEmpty();
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithLambdaExpressions_ShouldHandleCorrectly()
        {
            var sourceCode = @"
using System;
using System.Linq;
using System.Collections.Generic;

namespace TestNamespace
{
    public class LambdaTestClass
    {
        public void ProcessData()
        {
            var numbers = new List<int> { 1, 2, 3, 4, 5 };
            
            // Lambda with method calls
            var results = numbers
                .Where(x => IsEven(x))
                .Select(x => Transform(x))
                .ToList();
            
            // Action lambda
            results.ForEach(x => Console.WriteLine(x));
            
            // Func lambda
            Func<int, bool> predicate = x => IsValid(x);
            var valid = results.Any(predicate);
        }

        private bool IsEven(int number) => number % 2 == 0;
        
        private int Transform(int value) => value * 2;
        
        private bool IsValid(int value) => value > 0;
    }
}";

            var filePath = CreateTempFile("LambdaTest.cs", sourceCode);
            var result = await _analyzer.BuildCallGraphAsync(new[] { filePath });

            result.Should().NotBeNull();
            
            // Should detect method calls within lambdas
            var methods = result.GetAllMethods();
            methods.Should().Contain(m => m.Contains("IsEven"));
            methods.Should().Contain(m => m.Contains("Transform"));
            methods.Should().Contain(m => m.Contains("IsValid"));
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithAsyncAwaitPattern_ShouldHandleCorrectly()
        {
            var sourceCode = @"
using System;
using System.Threading.Tasks;

namespace TestNamespace
{
    public class AsyncTestClass
    {
        public async Task<string> MainOperationAsync()
        {
            var result1 = await FirstOperationAsync();
            var result2 = await SecondOperationAsync(result1);
            return await FinalizeAsync(result2);
        }

        private async Task<string> FirstOperationAsync()
        {
            await Task.Delay(100);
            return ""First"";
        }

        private async Task<string> SecondOperationAsync(string input)
        {
            await Task.Delay(100);
            return input + ""Second"";
        }

        private async Task<string> FinalizeAsync(string input)
        {
            await Task.Delay(100);
            return input + ""Final"";
        }
    }
}";

            var filePath = CreateTempFile("AsyncTest.cs", sourceCode);
            var result = await _analyzer.BuildCallGraphAsync(new[] { filePath });

            result.Should().NotBeNull();
            
            // Should detect async method calls
            var asyncMethods = result.GetAllMethods()
                .Where(m => m.Contains("Async"))
                .ToList();
            
            asyncMethods.Should().NotBeEmpty();
            asyncMethods.Should().Contain(m => m.Contains("FirstOperationAsync"));
            asyncMethods.Should().Contain(m => m.Contains("SecondOperationAsync"));
            asyncMethods.Should().Contain(m => m.Contains("FinalizeAsync"));
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithReflectionCalls_ShouldHandleGracefully()
        {
            var sourceCode = @"
using System;
using System.Reflection;

namespace TestNamespace
{
    public class ReflectionTestClass
    {
        public void UseReflection()
        {
            var type = typeof(TargetClass);
            var instance = Activator.CreateInstance(type);
            
            var method = type.GetMethod(""TargetMethod"");
            method?.Invoke(instance, null);
            
            // Property access via reflection
            var property = type.GetProperty(""TargetProperty"");
            property?.GetValue(instance);
        }

        public void DirectCall()
        {
            var target = new TargetClass();
            target.TargetMethod();
        }
    }

    public class TargetClass
    {
        public string TargetProperty { get; set; } = ""test"";
        
        public void TargetMethod()
        {
            Console.WriteLine(""Target method called"");
        }
    }
}";

            var filePath = CreateTempFile("ReflectionTest.cs", sourceCode);
            var result = await _analyzer.BuildCallGraphAsync(new[] { filePath });

            result.Should().NotBeNull();
            
            // Should handle direct calls but may not catch reflection calls
            // This tests the robustness of the analyzer
            var methods = result.GetAllMethods();
            methods.Should().Contain(m => m.Contains("DirectCall"));
            methods.Should().Contain(m => m.Contains("TargetMethod"));
        }

        [Fact]
        public async Task ExtractMethodsFromFileAsync_WithComplexSyntax_ShouldHandleCorrectly()
        {
            var sourceCode = @"
using System;

namespace TestNamespace
{
    public class ComplexSyntaxClass
    {
        // Property with expression body
        public string Name => GetDefaultName();
        
        // Method with expression body
        public int Calculate(int x) => x * 2 + GetOffset();
        
        // Local function
        public void ProcessData()
        {
            int LocalFunction(int input)
            {
                return input + GetMultiplier();
            }
            
            var result = LocalFunction(10);
            Console.WriteLine(result);
        }
        
        // Pattern matching
        public string GetDescription(object obj) => obj switch
        {
            int i => $""Integer: {i}"",
            string s => $""String: {s}"",
            _ => ""Unknown""
        };
        
        private string GetDefaultName() => ""Default"";
        private int GetOffset() => 5;
        private int GetMultiplier() => 3;
    }
}";

            var filePath = CreateTempFile("ComplexSyntaxTest.cs", sourceCode);
            var result = await _analyzer.ExtractMethodsFromFileAsync(filePath);

            result.Should().NotBeEmpty();
            result.Should().Contain(m => m.Name == "GetDefaultName");
            result.Should().Contain(m => m.Name == "Calculate");
            result.Should().Contain(m => m.Name == "ProcessData");
            result.Should().Contain(m => m.Name == "GetDescription");
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithCircularReferences_ShouldHandleWithoutInfiniteLoop()
        {
            var sourceCode = @"
using System;

namespace TestNamespace
{
    public class CircularClass
    {
        private static int counter = 0;
        
        public void MethodA()
        {
            counter++;
            if (counter < 10)
                MethodB();
        }
        
        public void MethodB()
        {
            counter++;
            if (counter < 10)
                MethodC();
        }
        
        public void MethodC()
        {
            counter++;
            if (counter < 10)
                MethodA(); // Circular reference
        }
    }
}";

            var filePath = CreateTempFile("CircularTest.cs", sourceCode);
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await _analyzer.BuildCallGraphAsync(new[] { filePath }, cts.Token);

            result.Should().NotBeNull();
            // Should complete without infinite loop
            result.GetAllMethods().Should().NotBeEmpty();
        }

        [Fact]
        public async Task BuildCallGraphAsync_WithLargeCodebase_ShouldPerformReasonably()
        {
            // Generate a large source file with many classes and methods
            var sourceCode = @"
using System;

namespace TestNamespace
{
    public class LargeCodebaseEntry
    {
        public void StartProcess()
        {";

            // Generate 100 classes with 10 methods each
            for (int i = 0; i < 50; i++)
            {
                sourceCode += $"            new GeneratedClass{i}().Method0();\n";
            }
            
            sourceCode += @"
        }
    }";

            for (int i = 0; i < 50; i++)
            {
                sourceCode += $@"
    public class GeneratedClass{i}
    {{";
                for (int j = 0; j < 10; j++)
                {
                    var nextMethod = j < 9 ? j + 1 : 0;
                    var nextClass = i < 49 ? i + 1 : 0;
                    sourceCode += $@"
        public void Method{j}()
        {{
            Console.WriteLine(""Class {i}, Method {j}"");";
                    
                    if (j < 9)
                        sourceCode += $@"
            Method{nextMethod}();";
                    else if (i < 49)
                        sourceCode += $@"
            new GeneratedClass{nextClass}().Method0();";
                        
                    sourceCode += @"
        }";
                }
                sourceCode += @"
    }";
            }

            var filePath = CreateTempFile("LargeCodebaseTest.cs", sourceCode);
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var startTime = DateTime.UtcNow;
            
            var result = await _analyzer.BuildCallGraphAsync(new[] { filePath }, cts.Token);
            
            var duration = DateTime.UtcNow - startTime;
            
            result.Should().NotBeNull();
            duration.Should().BeLessThan(TimeSpan.FromSeconds(30));
            
            // Should handle large codebases
            var methods = result.GetAllMethods();
            methods.Count.Should().BeGreaterThan(100);
        }

        [Fact]
        public async Task FindTestsExercisingMethodAsync_WithComplexTestScenarios_ShouldDetectCorrectly()
        {
            var sourceCode = @"
using System;
using Xunit;
using NUnit.Framework;

namespace TestNamespace
{
    // Production code
    public class Calculator
    {
        public int Add(int a, int b) => a + b;
        public int Multiply(int a, int b) => a * b;
        public int Divide(int a, int b) => b != 0 ? a / b : 0;
    }

    public class CalculatorService
    {
        private readonly Calculator _calculator = new Calculator();
        
        public int ComplexCalculation(int x, int y)
        {
            var sum = _calculator.Add(x, y);
            var product = _calculator.Multiply(sum, 2);
            return _calculator.Divide(product, y);
        }
    }

    // Test code with different frameworks
    public class CalculatorXUnitTests
    {
        [Fact]
        public void Add_WithValidInputs_ReturnsSum()
        {
            var calc = new Calculator();
            var result = calc.Add(2, 3);
            Assert.Equal(5, result);
        }

        [Theory]
        [InlineData(2, 3, 5)]
        [InlineData(0, 0, 0)]
        public void Add_WithMultipleInputs_ReturnsExpectedSum(int a, int b, int expected)
        {
            var calc = new Calculator();
            var result = calc.Add(a, b);
            Assert.Equal(expected, result);
        }
    }

    [TestFixture]
    public class CalculatorNUnitTests
    {
        [Test]
        public void Multiply_WithValidInputs_ReturnsProduct()
        {
            var calc = new Calculator();
            var result = calc.Multiply(4, 5);
            Assert.AreEqual(20, result);
        }
    }

    public class CalculatorServiceTests
    {
        [Fact]
        public void ComplexCalculation_CallsAllCalculatorMethods()
        {
            var service = new CalculatorService();
            var result = service.ComplexCalculation(10, 5);
            // This test exercises Add, Multiply, and Divide indirectly
        }
    }
}";

            var filePath = CreateTempFile("ComplexTestScenario.cs", sourceCode);
            
            // Test direct method coverage
            var addMethodResult = await _analyzer.FindTestsExercisingMethodAsync(
                "TestNamespace.Calculator.Add(int,int)", 
                new[] { filePath }
            );
            
            addMethodResult.Should().NotBeEmpty();
            addMethodResult.Should().Contain(r => r.TestMethodName.Contains("Add_WithValidInputs"));
            
            // Test indirect method coverage
            var multiplyMethodResult = await _analyzer.FindTestsExercisingMethodAsync(
                "TestNamespace.Calculator.Multiply(int,int)", 
                new[] { filePath }
            );
            
            multiplyMethodResult.Should().NotBeEmpty();
        }

        [Fact]
        public async Task AnalyzeTypeUsageAsync_WithNestedGenerics_ShouldHandleCorrectly()
        {
            var sourceCode = @"
using System;
using System.Collections.Generic;

namespace TestNamespace
{
    public class GenericContainer<T, U>
    {
        public Dictionary<string, List<T>> ComplexProperty { get; set; }
        public Func<T, U> Converter { get; set; }
        
        public void ProcessData(IEnumerable<KeyValuePair<T, U>> data)
        {
            var processor = new DataProcessor<T, U>();
            processor.Process(data);
        }
    }

    public class DataProcessor<TInput, TOutput>
    {
        public void Process(IEnumerable<KeyValuePair<TInput, TOutput>> items)
        {
            foreach (var item in items)
            {
                Console.WriteLine($""{item.Key}: {item.Value}"");
            }
        }
    }
}";

            var filePath = CreateTempFile("NestedGenericsTest.cs", sourceCode);
            var result = await _analyzer.AnalyzeTypeUsageAsync(new[] { filePath });

            result.Should().NotBeEmpty();
            result.Should().Contain(usage => usage.TypeName == "GenericContainer");
            result.Should().Contain(usage => usage.TypeName == "DataProcessor");
        }

        private string CreateTempFile(string fileName, string content)
        {
            var filePath = Path.Combine(_tempDirectory, fileName);
            File.WriteAllText(filePath, content);
            return filePath;
        }
    }
}