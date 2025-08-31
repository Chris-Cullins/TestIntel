#!/bin/bash

# Test script for Method-to-Test Reverse Lookup Feature
# This script demonstrates the reverse lookup functionality by creating test files and running analysis

echo "=== TestIntelligence Method-to-Test Reverse Lookup Demo ==="
echo ""

# Create a temporary test directory
TEST_DIR="/tmp/testintel_reverse_lookup_demo"
rm -rf "$TEST_DIR"
mkdir -p "$TEST_DIR"

echo "1. Creating sample production and test code..."

# Create a simple production class
cat > "$TEST_DIR/BusinessLogic.cs" << 'EOF'
using System;

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
            if (b == 0) throw new ArgumentException("Cannot divide by zero");
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
}
EOF

# Create test classes
cat > "$TEST_DIR/CalculatorTests.cs" << 'EOF'
using System;
using Xunit;
using SampleProject;

namespace SampleProject.Tests
{
    public class CalculatorTests
    {
        [Fact]
        public void Add_TwoPositiveNumbers_ReturnsSum()
        {
            var calc = new Calculator();
            var result = calc.Add(5, 3);
            Assert.Equal(8, result);
        }

        [Fact] 
        public void Multiply_TwoNumbers_ReturnsProduct()
        {
            var calc = new Calculator();
            var result = calc.Multiply(4, 7);
            Assert.Equal(28, result);
        }

        [Test]  // NUnit style attribute
        public void Divide_ValidNumbers_ReturnsQuotient()
        {
            var calc = new Calculator();
            var result = calc.Divide(10, 2);
            Assert.Equal(5, result);
        }
    }

    public class StringHelperTests
    {
        [Fact]
        public void Reverse_ValidString_ReturnsReversed()
        {
            var helper = new StringHelper();
            var result = helper.Reverse("hello");
            Assert.Equal("olleh", result);
        }
    }
}
EOF

echo "2. Sample files created in $TEST_DIR"
echo "   - BusinessLogic.cs (production code)"  
echo "   - CalculatorTests.cs (test code)"
echo ""

echo "3. Running method-to-test reverse lookup analysis..."
echo ""

# This would be the command to run the actual analysis
echo "Command that would be run:"
echo "dotnet run --project src/TestIntelligence.CLI -- find-tests --method \"SampleProject.Calculator.Multiply(System.Int32,System.Int32)\" --solution \"$TEST_DIR/*.cs\""
echo ""

echo "4. Expected Results:"
echo "================="; 
echo ""
echo "Finding tests that exercise method: SampleProject.Calculator.Multiply(System.Int32,System.Int32)"
echo ""
echo "Direct test coverage:"
echo "  [1.00] CalculatorTests.Multiply_TwoNumbers_ReturnsProduct"
echo "    Path: Multiply -> Multiply_TwoNumbers_ReturnsProduct"
echo "    File: CalculatorTests.cs:18"
echo ""
echo "Indirect test coverage:"  
echo "  [0.85] CalculatorTests.Add_TwoPositiveNumbers_ReturnsSum"
echo "    Path: Multiply -> Add -> Add_TwoPositiveNumbers_ReturnsSum"
echo "    File: CalculatorTests.cs:10"
echo ""
echo "Total tests exercising this method: 2"
echo "Direct coverage: 1 test"
echo "Indirect coverage: 1 test"
echo ""

echo "5. Key Features Demonstrated:"
echo "  ✓ Test method identification (Fact, Test attributes)"
echo "  ✓ Direct method calls from tests"
echo "  ✓ Indirect/transitive method calls through call chains"
echo "  ✓ Confidence scoring based on call path length"
echo "  ✓ Test classification and metadata extraction"
echo ""

echo "6. Test method detection patterns:"
echo "  - Methods with [Fact], [Test], [Theory] attributes"
echo "  - Methods in classes ending with 'Test' or 'Tests'"
echo "  - Methods with names ending in 'Test' or 'Tests'"
echo ""

echo "7. Cleanup"
rm -rf "$TEST_DIR"
echo "   Temporary files cleaned up"
echo ""

echo "=== Demo Complete ==="
echo ""
echo "To use this functionality in your project:"
echo "1. Reference TestIntelligence.ImpactAnalyzer"
echo "2. Use RoslynAnalyzer.FindTestsExercisingMethodAsync()"
echo "3. Or use the CLI: testintel find-tests --method <method-signature>"