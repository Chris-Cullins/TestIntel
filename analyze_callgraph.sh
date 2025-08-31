#!/bin/bash

# TestIntelligence Call Graph Analysis Script
# Generates comprehensive method call graph reports for the entire library

set -e  # Exit on any error

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CLI_PROJECT="$SCRIPT_DIR/src/TestIntelligence.CLI"
SOURCE_PATH="$SCRIPT_DIR/src"
OUTPUT_DIR="$SCRIPT_DIR/CallGraphReports"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to check if dotnet is available
check_prerequisites() {
    print_status "Checking prerequisites..."
    
    if ! command -v dotnet &> /dev/null; then
        print_error ".NET CLI is not installed or not in PATH"
        exit 1
    fi
    
    if [ ! -d "$CLI_PROJECT" ]; then
        print_error "TestIntelligence CLI project not found at: $CLI_PROJECT"
        exit 1
    fi
    
    if [ ! -d "$SOURCE_PATH" ]; then
        print_error "Source path not found at: $SOURCE_PATH"
        exit 1
    fi
    
    print_success "Prerequisites check passed"
}

# Function to build the CLI project
build_cli() {
    print_status "Building TestIntelligence CLI..."
    
    if dotnet build "$CLI_PROJECT" --configuration Release --verbosity quiet > /dev/null 2>&1; then
        print_success "CLI build completed successfully"
    else
        print_warning "CLI build completed with warnings (continuing...)"
    fi
}

# Function to create output directory
setup_output_dir() {
    print_status "Setting up output directory..."
    
    mkdir -p "$OUTPUT_DIR"
    print_success "Output directory ready: $OUTPUT_DIR"
}

# Function to run call graph analysis
run_callgraph_analysis() {
    local format="$1"
    local max_methods="$2"
    local verbose_flag="$3"
    local output_suffix="$4"
    local description="$5"
    
    print_status "Running $description..."
    
    local output_file="$OUTPUT_DIR/callgraph_${output_suffix}_${TIMESTAMP}"
    if [ "$format" = "json" ]; then
        output_file="${output_file}.json"
    else
        output_file="${output_file}.txt"
    fi
    
    local cmd="dotnet run --project \"$CLI_PROJECT\" --configuration Release -- callgraph --path \"$SOURCE_PATH\" --format \"$format\" --max-methods $max_methods --output \"$output_file\""
    
    if [ "$verbose_flag" = "true" ]; then
        cmd="$cmd --verbose"
    fi
    
    # Suppress build warnings but capture actual analysis errors
    if eval $cmd 2>/tmp/callgraph_stderr.log; then
        print_success "$description completed: $output_file"
        
        # Show file size and line count for text files
        if [ "$format" = "text" ]; then
            local line_count=$(wc -l < "$output_file")
            local file_size=$(du -h "$output_file" | cut -f1)
            print_status "  → Generated $line_count lines ($file_size)"
        else
            local file_size=$(du -h "$output_file" | cut -f1)
            print_status "  → Generated JSON file ($file_size)"
        fi
    else
        print_error "$description failed. Check /tmp/callgraph_stderr.log for details"
        if [ -f /tmp/callgraph_stderr.log ]; then
            echo "Error details:"
            tail -20 /tmp/callgraph_stderr.log
        fi
        return 1
    fi
}

# Function to generate summary report
generate_summary() {
    print_status "Generating analysis summary..."
    
    local summary_file="$OUTPUT_DIR/analysis_summary_${TIMESTAMP}.txt"
    
    cat > "$summary_file" << EOF
TestIntelligence Library - Call Graph Analysis Summary
=====================================================

Generated: $(date)
Source Path: $SOURCE_PATH
Analysis Tool: TestIntelligence CLI v$(dotnet run --project "$CLI_PROJECT" --configuration Release -- version 2>/dev/null | grep "TestIntelligence CLI" | head -1)

Reports Generated:
------------------
EOF

    # List all generated files
    find "$OUTPUT_DIR" -name "*_${TIMESTAMP}.*" -type f | sort | while read -r file; do
        local basename=$(basename "$file")
        local size=$(du -h "$file" | cut -f1)
        echo "- $basename ($size)" >> "$summary_file"
    done
    
    cat >> "$summary_file" << EOF

Usage Instructions:
------------------
1. Text Reports: Open with any text editor for human-readable analysis
2. JSON Reports: Use for programmatic processing or integration with other tools
3. Verbose Reports: Contain detailed method call relationships
4. Standard Reports: Focus on high-level statistics and top methods

Next Steps:
-----------
- Review the summary report for key insights
- Use verbose reports to understand specific method dependencies
- Import JSON data into visualization tools for call graph diagrams
- Compare reports over time to track architectural changes
EOF
    
    print_success "Summary report generated: $summary_file"
}

# Function to display final results
show_results() {
    print_success "Call graph analysis completed!"
    echo
    print_status "Generated reports in: $OUTPUT_DIR"
    
    echo
    echo "Files created:"
    find "$OUTPUT_DIR" -name "*_${TIMESTAMP}.*" -type f | sort | while read -r file; do
        local basename=$(basename "$file")
        local size=$(du -h "$file" | cut -f1)
        printf "  %-40s %s\n" "$basename" "$size"
    done
    
    echo
    print_status "To view the main report:"
    echo "  cat \"$OUTPUT_DIR/callgraph_standard_${TIMESTAMP}.txt\""
    
    print_status "To view detailed analysis:"
    echo "  cat \"$OUTPUT_DIR/callgraph_detailed_${TIMESTAMP}.txt\""
    
    print_status "To process JSON data:"
    echo "  jq '.' \"$OUTPUT_DIR/callgraph_json_${TIMESTAMP}.json\""
}

# Main execution
main() {
    echo "TestIntelligence Call Graph Analysis"
    echo "===================================="
    echo
    
    check_prerequisites
    build_cli
    setup_output_dir
    
    echo
    print_status "Starting comprehensive call graph analysis..."
    echo
    
    # Generate different types of reports
    run_callgraph_analysis "text" "30" "false" "standard" "Standard analysis (30 methods)"
    run_callgraph_analysis "text" "100" "true" "detailed" "Detailed verbose analysis (100 methods)"
    run_callgraph_analysis "json" "50" "false" "json" "JSON export (50 methods)"
    run_callgraph_analysis "text" "10" "true" "focused" "Focused analysis (10 methods, verbose)"
    
    generate_summary
    
    echo
    show_results
    
    # Cleanup temporary files
    rm -f /tmp/callgraph_stderr.log
}

# Handle script interruption
trap 'print_error "Analysis interrupted"; exit 1' INT TERM

# Run main function
main "$@"