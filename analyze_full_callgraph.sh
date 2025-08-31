#!/bin/bash

# TestIntelligence Complete Call Graph Analysis Script
# Generates comprehensive method call graph reports with multiple formats and detail levels

set -e  # Exit on any error

echo "🔍 TestIntelligence Complete Call Graph Analysis"
echo "==============================================="
echo "This script generates comprehensive method call graph reports"
echo "for the entire TestIntelligence library codebase."
echo

# Configuration
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
REPORTS_DIR="CallGraphReports_${TIMESTAMP}"

# Create timestamped output directory
echo "📁 Creating output directory: $REPORTS_DIR"
mkdir -p "$REPORTS_DIR"

# Build CLI with progress indication
echo "🔨 Building TestIntelligence CLI..."
if dotnet build src/TestIntelligence.CLI --configuration Release --verbosity quiet; then
    echo "✅ CLI build completed successfully"
else
    echo "⚠️  CLI build completed with warnings (continuing...)"
fi

echo
echo "🚀 Starting comprehensive call graph analysis..."
echo "   This will generate multiple reports with different detail levels"
echo

# Report 1: Executive Summary (Top 15 methods, concise)
echo "1️⃣  Generating Executive Summary Report..."
dotnet run --project src/TestIntelligence.CLI --configuration Release -- callgraph \
    --path src \
    --format text \
    --max-methods 15 \
    --output "$REPORTS_DIR/01_executive_summary.txt" \
    2>/dev/null && echo "   ✅ Executive summary complete"

# Report 2: Standard Analysis (30 methods, balanced detail)
echo "2️⃣  Generating Standard Analysis Report..."
dotnet run --project src/TestIntelligence.CLI --configuration Release -- callgraph \
    --path src \
    --format text \
    --max-methods 30 \
    --output "$REPORTS_DIR/02_standard_analysis.txt" \
    2>/dev/null && echo "   ✅ Standard analysis complete"

# Report 3: Detailed Architecture View (50 methods, verbose)
echo "3️⃣  Generating Detailed Architecture Report..."
dotnet run --project src/TestIntelligence.CLI --configuration Release -- callgraph \
    --path src \
    --format text \
    --max-methods 50 \
    --verbose \
    --output "$REPORTS_DIR/03_detailed_architecture.txt" \
    2>/dev/null && echo "   ✅ Detailed architecture complete"

# Report 4: Complete Method Inventory (100 methods, verbose)
echo "4️⃣  Generating Complete Method Inventory..."
dotnet run --project src/TestIntelligence.CLI --configuration Release -- callgraph \
    --path src \
    --format text \
    --max-methods 100 \
    --verbose \
    --output "$REPORTS_DIR/04_complete_inventory.txt" \
    2>/dev/null && echo "   ✅ Complete inventory complete"

# Report 5: JSON Data Export (for programmatic use)
echo "5️⃣  Generating JSON Data Export..."
dotnet run --project src/TestIntelligence.CLI --configuration Release -- callgraph \
    --path src \
    --format json \
    --max-methods 75 \
    --output "$REPORTS_DIR/05_data_export.json" \
    2>/dev/null && echo "   ✅ JSON export complete"

# Report 6: Core Components Analysis (focused on key files)
echo "6️⃣  Generating Core Components Analysis..."
dotnet run --project src/TestIntelligence.CLI --configuration Release -- callgraph \
    --path src/TestIntelligence.ImpactAnalyzer \
    --format text \
    --max-methods 25 \
    --verbose \
    --output "$REPORTS_DIR/06_core_impact_analyzer.txt" \
    2>/dev/null && echo "   ✅ Core components analysis complete"

# Report 7: CLI Components Analysis
echo "7️⃣  Generating CLI Components Analysis..."
dotnet run --project src/TestIntelligence.CLI --configuration Release -- callgraph \
    --path src/TestIntelligence.CLI \
    --format text \
    --max-methods 20 \
    --verbose \
    --output "$REPORTS_DIR/07_cli_components.txt" \
    2>/dev/null && echo "   ✅ CLI components analysis complete"

# Generate analysis summary
echo "8️⃣  Generating Analysis Summary..."
cat > "$REPORTS_DIR/00_ANALYSIS_SUMMARY.txt" << EOF
TestIntelligence Library - Complete Call Graph Analysis
======================================================

Generated: $(date)
Analysis ID: ${TIMESTAMP}
Source Path: $(pwd)/src
CLI Version: $(dotnet run --project src/TestIntelligence.CLI --configuration Release -- version 2>/dev/null | head -1 || echo "TestIntelligence CLI")

📊 Generated Reports:
--------------------
EOF

# List all generated files with descriptions
for file in "$REPORTS_DIR"/*.txt "$REPORTS_DIR"/*.json; do
    if [ -f "$file" ]; then
        filename=$(basename "$file")
        filesize=$(ls -lh "$file" | awk '{print $5}')
        case "$filename" in
            "01_executive_summary.txt")
                echo "📋 $filename ($filesize) - High-level overview of top 15 methods" >> "$REPORTS_DIR/00_ANALYSIS_SUMMARY.txt"
                ;;
            "02_standard_analysis.txt")
                echo "📊 $filename ($filesize) - Standard analysis of top 30 methods" >> "$REPORTS_DIR/00_ANALYSIS_SUMMARY.txt"
                ;;
            "03_detailed_architecture.txt")
                echo "🏗️  $filename ($filesize) - Detailed architecture view (50 methods, verbose)" >> "$REPORTS_DIR/00_ANALYSIS_SUMMARY.txt"
                ;;
            "04_complete_inventory.txt")
                echo "📚 $filename ($filesize) - Complete method inventory (100 methods, verbose)" >> "$REPORTS_DIR/00_ANALYSIS_SUMMARY.txt"
                ;;
            "05_data_export.json")
                echo "🔧 $filename ($filesize) - JSON data export for programmatic use" >> "$REPORTS_DIR/00_ANALYSIS_SUMMARY.txt"
                ;;
            "06_core_impact_analyzer.txt")
                echo "⚡ $filename ($filesize) - Core ImpactAnalyzer components analysis" >> "$REPORTS_DIR/00_ANALYSIS_SUMMARY.txt"
                ;;
            "07_cli_components.txt")
                echo "💻 $filename ($filesize) - CLI components detailed analysis" >> "$REPORTS_DIR/00_ANALYSIS_SUMMARY.txt"
                ;;
        esac
    fi
done

cat >> "$REPORTS_DIR/00_ANALYSIS_SUMMARY.txt" << EOF

🚀 Quick Start Guide:
--------------------
1. Executive Summary:     cat $REPORTS_DIR/01_executive_summary.txt
2. Standard Analysis:     cat $REPORTS_DIR/02_standard_analysis.txt  
3. Detailed Architecture: cat $REPORTS_DIR/03_detailed_architecture.txt
4. Complete Inventory:    cat $REPORTS_DIR/04_complete_inventory.txt
5. JSON Processing:       jq '.' $REPORTS_DIR/05_data_export.json

🎯 Use Cases:
-------------
- Code Review:        Use 01_executive_summary.txt and 02_standard_analysis.txt
- Architecture Study: Use 03_detailed_architecture.txt and 04_complete_inventory.txt
- Automation/Tools:   Use 05_data_export.json for programmatic processing
- Component Analysis: Use 06_core_impact_analyzer.txt and 07_cli_components.txt

📈 Key Insights to Look For:
---------------------------
- Methods with highest call counts (potential refactoring candidates)
- Most frequently called methods (critical dependencies)
- Method call relationships and dependency chains
- Component coupling and architectural patterns
- Test coverage implications based on call relationships

💡 Next Steps:
--------------
1. Review executive summary for high-level architecture overview
2. Study detailed reports to understand component relationships
3. Use JSON data to create visualization diagrams
4. Compare reports over time to track architectural evolution
5. Identify refactoring opportunities based on call patterns

Generated with TestIntelligence CLI - Intelligent Test Analysis Tool
EOF

echo "   ✅ Analysis summary complete"

echo
echo "🎉 Complete Call Graph Analysis Finished!"
echo "========================================="
echo
echo "📁 All reports generated in: $REPORTS_DIR"
echo
echo "📊 Generated Files:"
ls -lh "$REPORTS_DIR"/ | grep -E "\.(txt|json)$" | while read -r line; do
    echo "   $line"
done

echo
echo "🚀 Quick Commands:"
echo "   Summary:     cat $REPORTS_DIR/00_ANALYSIS_SUMMARY.txt"
echo "   Executive:   cat $REPORTS_DIR/01_executive_summary.txt"
echo "   Detailed:    cat $REPORTS_DIR/03_detailed_architecture.txt"
echo "   JSON:        jq '.TotalMethods' $REPORTS_DIR/05_data_export.json"
echo
echo "✨ Analysis complete! Happy code exploration! 🔍"