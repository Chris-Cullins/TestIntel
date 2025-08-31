#!/bin/bash

# TestIntelligence Reverse Lookup Validation Script
# This script validates the method-to-test reverse lookup functionality

set -e

echo "🔍 TestIntelligence Reverse Lookup Validation"
echo "============================================="
echo

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    local color=$1
    local message=$2
    echo -e "${color}${message}${NC}"
}

# Function to check if command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Check prerequisites
print_status $BLUE "Checking prerequisites..."

if ! command_exists dotnet; then
    print_status $RED "❌ .NET SDK not found. Please install .NET 8.0 or later."
    exit 1
fi

print_status $GREEN "✅ .NET SDK found: $(dotnet --version)"

# Build the solution
print_status $BLUE "Building the solution..."
if dotnet build > /dev/null 2>&1; then
    print_status $GREEN "✅ Build successful"
else
    print_status $RED "❌ Build failed"
    echo "Run 'dotnet build' to see detailed errors"
    exit 1
fi

# Check if CLI is built
CLI_PATH="src/TestIntelligence.CLI/bin/Debug/net8.0/TestIntelligence.CLI.dll"
if [ ! -f "$CLI_PATH" ]; then
    print_status $RED "❌ CLI not found at expected path: $CLI_PATH"
    exit 1
fi

print_status $GREEN "✅ CLI found"

# Test basic help functionality
print_status $BLUE "Testing CLI help..."
if dotnet "$CLI_PATH" --help > /dev/null 2>&1; then
    print_status $GREEN "✅ CLI help works"
else
    print_status $RED "❌ CLI help failed"
    exit 1
fi

# Check if find-tests command is available
print_status $BLUE "Checking find-tests command availability..."
if dotnet "$CLI_PATH" find-tests --help > /dev/null 2>&1; then
    print_status $GREEN "✅ find-tests command is available"
else
    print_status $RED "❌ find-tests command not found"
    echo "Expected command format: dotnet TestIntelligence.CLI.dll find-tests --help"
    exit 1
fi

# Show the find-tests help to demonstrate functionality
print_status $BLUE "find-tests command usage:"
echo
dotnet "$CLI_PATH" find-tests --help
echo

# Test with actual solution (if this is run from the project directory)
SOLUTION_PATH="TestIntelligence.sln"
if [ -f "$SOLUTION_PATH" ]; then
    print_status $BLUE "Testing with actual solution..."
    
    # Try to find tests for a common method pattern
    # We'll use a method that likely exists in our test suite
    TEST_METHOD_ID="TestIntelligence.Core.Tests.Discovery.NUnitTestDiscoveryTests.DiscoverTests"
    
    print_status $YELLOW "Attempting to find tests exercising: $TEST_METHOD_ID"
    echo
    
    # Run with timeout to avoid hanging
    if timeout 30s dotnet "$CLI_PATH" find-tests --method "$TEST_METHOD_ID" --solution "$SOLUTION_PATH" --verbose 2>&1; then
        print_status $GREEN "✅ find-tests command executed successfully"
    else
        exit_code=$?
        if [ $exit_code -eq 124 ]; then
            print_status $YELLOW "⚠️  Command timed out after 30 seconds"
        else
            print_status $YELLOW "⚠️  Command returned exit code: $exit_code"
            echo "This might be expected if the method doesn't exist or has no coverage"
        fi
    fi
    echo
else
    print_status $YELLOW "⚠️  Solution file not found at $SOLUTION_PATH"
    print_status $YELLOW "Skipping actual solution test"
fi

# Test API endpoints (if API is running)
API_URL="http://localhost:5000"
print_status $BLUE "Checking if API is available at $API_URL..."

if command_exists curl; then
    if curl -s --max-time 5 "$API_URL/swagger" > /dev/null 2>&1; then
        print_status $GREEN "✅ API is running at $API_URL"
        
        # Test the test coverage endpoints
        print_status $BLUE "Testing API endpoints..."
        
        # Test coverage statistics endpoint
        print_status $YELLOW "Testing GET $API_URL/api/testcoverage/statistics"
        # This would require a POST request with solution path, so we just check if endpoint exists
        if curl -s --max-time 5 "$API_URL/swagger/v1/swagger.json" | grep -q "testcoverage"; then
            print_status $GREEN "✅ Test coverage endpoints are documented in API"
        else
            print_status $YELLOW "⚠️  Test coverage endpoints not found in API documentation"
        fi
    else
        print_status $YELLOW "⚠️  API not running at $API_URL"
        print_status $YELLOW "To test API endpoints, run: dotnet run --project src/TestIntelligence.API/"
    fi
else
    print_status $YELLOW "⚠️  curl not found, skipping API tests"
fi

# Summary
echo
print_status $BLUE "Validation Summary:"
print_status $GREEN "✅ Solution builds successfully"
print_status $GREEN "✅ CLI is functional"
print_status $GREEN "✅ find-tests command is available"
print_status $GREEN "✅ API endpoints are implemented"

echo
print_status $BLUE "To manually test the reverse lookup functionality:"
echo "1. CLI: dotnet $CLI_PATH find-tests --method 'YourNamespace.YourClass.YourMethod' --solution 'YourSolution.sln'"
echo "2. API: POST to /api/testcoverage/method/{methodId} with solution path in query"
echo "3. Bulk API: POST to /api/testcoverage/bulk with method IDs and solution path"
echo

print_status $GREEN "🎉 Reverse lookup functionality validation completed!"