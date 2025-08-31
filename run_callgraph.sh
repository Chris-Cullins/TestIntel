#!/bin/bash

# TestIntelligence Call Graph Analysis Script
echo "TestIntelligence Call Graph Analysis"
echo "===================================="
echo

# Build the CLI first
echo "Building TestIntelligence CLI..."
dotnet build src/TestIntelligence.CLI --configuration Release --verbosity quiet

# Create output directory
mkdir -p CallGraphReports
timestamp=$(date +"%Y%m%d_%H%M%S")

echo "Running call graph analysis..."
echo

# Standard text report (30 methods)
echo "1. Generating standard text report..."
dotnet run --project src/TestIntelligence.CLI --configuration Release -- callgraph \
    --path src \
    --format text \
    --max-methods 30 \
    --output "CallGraphReports/callgraph_standard_${timestamp}.txt" \
    2>/dev/null

# Detailed verbose report (50 methods)  
echo "2. Generating detailed verbose report..."
dotnet run --project src/TestIntelligence.CLI --configuration Release -- callgraph \
    --path src \
    --format text \
    --max-methods 50 \
    --verbose \
    --output "CallGraphReports/callgraph_detailed_${timestamp}.txt" \
    2>/dev/null

# JSON export for programmatic use
echo "3. Generating JSON export..."
dotnet run --project src/TestIntelligence.CLI --configuration Release -- callgraph \
    --path src \
    --format json \
    --max-methods 25 \
    --output "CallGraphReports/callgraph_export_${timestamp}.json" \
    2>/dev/null

# Quick focused report (top 10 methods)
echo "4. Generating focused report (top 10)..."
dotnet run --project src/TestIntelligence.CLI --configuration Release -- callgraph \
    --path src \
    --format text \
    --max-methods 10 \
    --verbose \
    --output "CallGraphReports/callgraph_focused_${timestamp}.txt" \
    2>/dev/null

echo
echo "Analysis complete! Reports generated in CallGraphReports/"
echo "Files created:"
ls -lh CallGraphReports/*_${timestamp}.*

echo
echo "To view the main report:"
echo "cat CallGraphReports/callgraph_standard_${timestamp}.txt"
echo
echo "To view detailed analysis:" 
echo "cat CallGraphReports/callgraph_detailed_${timestamp}.txt"
echo
echo "To view focused report:"
echo "cat CallGraphReports/callgraph_focused_${timestamp}.txt"