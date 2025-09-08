# TestIntelligence Performance Testing Commands

## Quick Performance Test
Run a basic performance test suite with timing measurements:

```bash
# Create results directory
mkdir -p perf-results

# Test 1: Basic Test Discovery Performance
echo "=== Test Discovery Performance ===" | tee perf-results/results-$(date +%Y%m%d-%H%M%S).txt
time dotnet run --project src/TestIntelligence.CLI -- analyze --path TestIntelligence.sln --format json --output perf-results/analyze-$(date +%Y%m%d-%H%M%S).json 2>&1 | tee -a perf-results/results-$(date +%Y%m%d-%H%M%S).txt

# Test 2: Test Selection Performance
echo "=== Test Selection Performance ===" | tee -a perf-results/results-$(date +%Y%m%d-%H%M%S).txt
time dotnet run --project src/TestIntelligence.CLI -- select --path TestIntelligence.sln --changes "src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs" --confidence Medium --max-tests 50 --output perf-results/select-$(date +%Y%m%d-%H%M%S).json 2>&1 | tee -a perf-results/results-$(date +%Y%m%d-%H%M%S).txt

# Test 3: Impact Analysis Performance
echo "=== Impact Analysis Performance ===" | tee -a perf-results/results-$(date +%Y%m%d-%H%M%S).txt
time dotnet run --project src/TestIntelligence.CLI -- diff --solution TestIntelligence.sln --git-command "diff HEAD~1" --format json --output perf-results/diff-$(date +%Y%m%d-%H%M%S).json 2>&1 | tee -a perf-results/results-$(date +%Y%m%d-%H%M%S).txt

# Test 4: Cache Performance
echo "=== Cache Operations Performance ===" | tee -a perf-results/results-$(date +%Y%m%d-%H%M%S).txt
time dotnet run --project src/TestIntelligence.CLI -- cache --solution TestIntelligence.sln --action warm-up 2>&1 | tee -a perf-results/results-$(date +%Y%m%d-%H%M%S).txt

# Memory usage summary
echo "=== Memory Usage Summary ===" | tee -a perf-results/results-$(date +%Y%m%d-%H%M%S).txt
ps aux | grep TestIntelligence || echo "No running processes found"
```

## Comprehensive Performance Test
Run a full performance benchmark with detailed metrics:

```bash
# Create comprehensive results directory
mkdir -p perf-results/comprehensive

# Function to run test with memory monitoring
run_with_monitoring() {
    local test_name=$1
    local command=$2
    echo "=== $test_name ===" | tee -a perf-results/comprehensive/full-results-$(date +%Y%m%d-%H%M%S).txt
    
    # Run with time and memory monitoring
    /usr/bin/time -l $command 2>&1 | tee -a perf-results/comprehensive/full-results-$(date +%Y%m%d-%H%M%S).txt
    
    # Capture system info
    echo "System Memory: $(sysctl -n hw.memsize | awk '{print $1/1024/1024/1024 " GB"}')" | tee -a perf-results/comprehensive/full-results-$(date +%Y%m%d-%H%M%S).txt
    echo "CPU Count: $(sysctl -n hw.ncpu)" | tee -a perf-results/comprehensive/full-results-$(date +%Y%m%d-%H%M%S).txt
    echo "---" | tee -a perf-results/comprehensive/full-results-$(date +%Y%m%d-%H%M%S).txt
}

# Comprehensive test suite
run_with_monitoring "Test Discovery - Large Solution" "dotnet run --project src/TestIntelligence.CLI -- analyze --path TestIntelligence.sln --format json --verbose"

run_with_monitoring "Call Graph Generation" "dotnet run --project src/TestIntelligence.CLI -- callgraph --path TestIntelligence.sln --format json --max-methods 100"

run_with_monitoring "Find Tests - Core Method" "dotnet run --project src/TestIntelligence.CLI -- find-tests --method 'TestIntelligence.Core.Discovery.NUnitTestDiscovery.DiscoverTestsAsync' --solution TestIntelligence.sln --format json --verbose"

run_with_monitoring "Test Selection - Multiple Files" "dotnet run --project src/TestIntelligence.CLI -- select --path TestIntelligence.sln --changes 'src/TestIntelligence.Core/Discovery/NUnitTestDiscovery.cs' 'src/TestIntelligence.Core/Models/TestMethod.cs' --confidence High --max-tests 100"

run_with_monitoring "Coverage Analysis" "dotnet run --project src/TestIntelligence.CLI -- analyze-coverage --solution TestIntelligence.sln --tests 'TestIntelligence.Core.Tests.Discovery.NUnitTestDiscoveryTests.DiscoverTestsAsync_ValidAssembly_ReturnsTests' --git-command 'diff HEAD~1'"
```

## Stress Test
Run stress tests to identify performance bottlenecks:

```bash
# Create stress test directory
mkdir -p perf-results/stress

# Run multiple iterations to test consistency
for i in {1..5}; do
    echo "=== Stress Test Iteration $i ===" | tee -a perf-results/stress/stress-test-$(date +%Y%m%d-%H%M%S).txt
    
    # Test discovery stress test
    time dotnet run --project src/TestIntelligence.CLI -- analyze --path TestIntelligence.sln --format json --output perf-results/stress/analyze-iter-$i.json 2>&1 | tee -a perf-results/stress/stress-test-$(date +%Y%m%d-%H%M%S).txt
    
    # Memory cleanup between iterations
    sleep 2
done

# Analyze results
echo "=== Stress Test Summary ===" | tee -a perf-results/stress/stress-test-$(date +%Y%m%d-%H%M%S).txt
ls -la perf-results/stress/analyze-iter-*.json | tee -a perf-results/stress/stress-test-$(date +%Y%m%d-%H%M%S).txt
```

## Baseline Performance Test
Create baseline measurements for comparison:

```bash
# Create baseline directory
mkdir -p perf-results/baseline

# Get system info
echo "=== System Information ===" > perf-results/baseline/system-info.txt
uname -a >> perf-results/baseline/system-info.txt
sysctl -n hw.memsize | awk '{print "Memory: " $1/1024/1024/1024 " GB"}' >> perf-results/baseline/system-info.txt
sysctl -n hw.ncpu | awk '{print "CPU Cores: " $1}' >> perf-results/baseline/system-info.txt
dotnet --version | awk '{print ".NET Version: " $1}' >> perf-results/baseline/system-info.txt

# Baseline test suite
echo "=== Baseline Performance Tests ===" > perf-results/baseline/baseline-$(date +%Y%m%d-%H%M%S).txt

# Test 1: Cold start performance
echo "Cold Start Test Discovery:" >> perf-results/baseline/baseline-$(date +%Y%m%d-%H%M%S).txt
time dotnet run --project src/TestIntelligence.CLI -- analyze --path TestIntelligence.sln --format text 2>&1 | tail -5 >> perf-results/baseline/baseline-$(date +%Y%m%d-%H%M%S).txt

# Test 2: Warm cache performance
echo "Warm Cache Test Discovery:" >> perf-results/baseline/baseline-$(date +%Y%m%d-%H%M%S).txt
time dotnet run --project src/TestIntelligence.CLI -- analyze --path TestIntelligence.sln --format text 2>&1 | tail -5 >> perf-results/baseline/baseline-$(date +%Y%m%d-%H%M%S).txt

# Test 3: Memory usage baseline
echo "Memory Baseline:" >> perf-results/baseline/baseline-$(date +%Y%m%d-%H%M%S).txt
/usr/bin/time -l dotnet run --project src/TestIntelligence.CLI -- analyze --path TestIntelligence.sln --format json --output /dev/null 2>&1 | grep -E "(maximum resident|real|user|sys)" >> perf-results/baseline/baseline-$(date +%Y%m%d-%H%M%S).txt

echo "Baseline tests completed. Results saved to perf-results/baseline/"
```

## Performance Monitoring
Continuous performance monitoring setup:

```bash
# Monitor performance over time
mkdir -p perf-results/monitoring

# Create monitoring script
cat > perf-results/monitoring/monitor.sh << 'EOF'
#!/bin/bash
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
RESULTS_FILE="perf-results/monitoring/monitor-$TIMESTAMP.txt"

echo "=== Performance Monitor - $TIMESTAMP ===" > $RESULTS_FILE

# System load
echo "System Load:" >> $RESULTS_FILE
uptime >> $RESULTS_FILE

# Memory usage
echo "Memory Usage:" >> $RESULTS_FILE
vm_stat | head -5 >> $RESULTS_FILE

# Quick test
echo "Quick Test Performance:" >> $RESULTS_FILE
time dotnet run --project src/TestIntelligence.CLI -- analyze --path TestIntelligence.sln --format text > /dev/null 2>> $RESULTS_FILE

echo "Monitor results saved to $RESULTS_FILE"
EOF

chmod +x perf-results/monitoring/monitor.sh
echo "Monitoring script created. Run: ./perf-results/monitoring/monitor.sh"
```

## Analysis Commands
Commands to analyze performance results:

```bash
# Compare performance over time
ls -la perf-results/*/results-*.txt | head -10

# Extract timing information
grep "real\|user\|sys" perf-results/*/results-*.txt

# Memory usage trends
grep -r "maximum resident" perf-results/*/

# Test counts and timing correlation
grep -A5 -B5 "tests discovered\|Analysis completed" perf-results/*/results-*.txt
```

## Usage Notes

1. **Before running tests:**
   - Ensure the solution builds successfully: `dotnet build`
   - Close other memory-intensive applications
   - Run tests on a consistent system state

2. **Interpreting results:**
   - `real` = wall clock time
   - `user` = CPU time spent in user mode  
   - `sys` = CPU time spent in system mode
   - `maximum resident` = peak memory usage

3. **Creating baselines:**
   - Run baseline tests after major changes
   - Keep historical data for trend analysis
   - Document system configuration with results

4. **Automated testing:**
   - Add these commands to CI/CD pipelines
   - Set performance regression thresholds
   - Generate reports automatically

## Example Usage

To get started with performance testing:

1. **Run a quick test:**
   ```bash
   # Copy and paste the Quick Performance Test commands above
   ```

2. **Create a baseline:**
   ```bash
   # Copy and paste the Baseline Performance Test commands above
   ```

3. **Monitor over time:**
   ```bash
   # Run the monitoring setup, then execute the monitor script periodically
   ```

This approach gives you persistent data that you can analyze over time to track performance trends and identify regressions.