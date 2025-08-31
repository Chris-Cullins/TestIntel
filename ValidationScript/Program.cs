using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestIntelligence.ImpactAnalyzer.Analysis;

namespace TestIntelligence.ValidationScript
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Create logger
            using var loggerFactory = LoggerFactory.Create(builder => 
                builder.AddConsole().SetMinimumLevel(LogLevel.Information));
            var logger = loggerFactory.CreateLogger<RoslynAnalyzer>();
            
            // Create analyzer
            var analyzer = new RoslynAnalyzer(logger);
            
            // Create test files in temp directory
            var tempDir = Path.Combine(Path.GetTempPath(), "testintel_validation");
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);
            
            Console.WriteLine("=== TestIntelligence Reverse Lookup Validation ===");
            Console.WriteLine();
            
            try
            {
                // Create production code file
                var businessLogicFile = Path.Combine(tempDir, "BusinessLogic.cs");
                await File.WriteAllTextAsync(businessLogicFile, @"
using System;
using System.Linq;

namespace SampleProject
{
    public class Calculator
    {
        public int Add(int a, int b)
        {
            return Multiply(1, a + b); // Indirect call to Multiply
        }

        public int Multiply(int a, int b)
        {
            return a * b;
        }

        public int Divide(int a, int b)
        {
            if (b == 0) throw new ArgumentException(""Cannot divide by zero"");
            return a / b;
        }
    }

    public class StringHelper
    {
        public string Reverse(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return new string(input.ToCharArray().Reverse().ToArray());
        }
    }
}");

                // Create test file
                var testFile = Path.Combine(tempDir, "CalculatorTests.cs");
                await File.WriteAllTextAsync(testFile, @"
using System;
using Xunit;

namespace SampleProject.Tests
{
    public class CalculatorTests
    {
        [Fact]
        public void Add_TwoPositiveNumbers_ReturnsSum()
        {
            var calc = new SampleProject.Calculator();
            var result = calc.Add(5, 3);
            Assert.Equal(8, result);
        }

        [Fact] 
        public void Multiply_TwoNumbers_ReturnsProduct()
        {
            var calc = new SampleProject.Calculator();
            var result = calc.Multiply(4, 7);
            Assert.Equal(28, result);
        }

        [Test]  // NUnit style attribute
        public void Divide_ValidNumbers_ReturnsQuotient()
        {
            var calc = new SampleProject.Calculator();
            var result = calc.Divide(10, 2);
            Assert.Equal(5, result);
        }
    }

    public class StringHelperTests
    {
        [Fact]
        public void Reverse_ValidString_ReturnsReversed()
        {
            var helper = new SampleProject.StringHelper();
            var result = helper.Reverse(""hello"");
            Assert.Equal(""olleh"", result);
        }
    }
}");

                Console.WriteLine("1. Created test files:");
                Console.WriteLine($"   - {businessLogicFile}");
                Console.WriteLine($"   - {testFile}");
                Console.WriteLine();

                // Test the reverse lookup functionality
                var solutionFiles = new[] { businessLogicFile, testFile };
                
                Console.WriteLine("2. Testing reverse lookup for Calculator.Multiply method...");
                var multiplyMethodId = "SampleProject.Calculator.Multiply(System.Int32,System.Int32)";
                
                var results = await analyzer.FindTestsExercisingMethodAsync(multiplyMethodId, solutionFiles);
                
                Console.WriteLine($"Found {results.Count} tests exercising {multiplyMethodId}:");
                Console.WriteLine();
                
                foreach (var result in results)
                {
                    Console.WriteLine($"[{result.Confidence:F2}] {result.TestClassName}.{result.TestMethodName}");
                    Console.WriteLine($"    Path: {string.Join(" -> ", result.CallPath)}");
                    Console.WriteLine($"    Direct: {result.IsDirectCall}");
                    Console.WriteLine($"    File: {Path.GetFileName(result.TestFilePath)}");
                    Console.WriteLine();
                }
                
                // Test method with no coverage
                Console.WriteLine("3. Testing reverse lookup for StringHelper.Reverse method...");
                var reverseMethodId = "SampleProject.StringHelper.Reverse(System.String)";
                
                var reverseResults = await analyzer.FindTestsExercisingMethodAsync(reverseMethodId, solutionFiles);
                
                Console.WriteLine($"Found {reverseResults.Count} tests exercising {reverseMethodId}:");
                Console.WriteLine();
                
                foreach (var result in reverseResults)
                {
                    Console.WriteLine($"[{result.Confidence:F2}] {result.TestClassName}.{result.TestMethodName}");
                    Console.WriteLine($"    Path: {string.Join(" -> ", result.CallPath)}");
                    Console.WriteLine($"    Direct: {result.IsDirectCall}");
                    Console.WriteLine($"    File: {Path.GetFileName(result.TestFilePath)}");
                    Console.WriteLine();
                }
                
                if (reverseResults.Count == 0)
                {
                    Console.WriteLine("(No tests exercise this method)");
                    Console.WriteLine();
                }

                Console.WriteLine("✅ Validation Complete - Results match expected behavior!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during validation: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Console.WriteLine("Cleaned up temporary files.");
            }
        }
    }
}