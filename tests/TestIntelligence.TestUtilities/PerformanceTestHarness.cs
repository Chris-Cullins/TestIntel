using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.TestUtilities
{
    public class PerformanceTestHarness : IDisposable
    {
        private readonly ILogger? _logger;
        private readonly List<PerformanceMeasurement> _measurements;
        private readonly Stopwatch _overallStopwatch;
        private string? _currentTestName;

        public PerformanceTestHarness(ILogger? logger = null)
        {
            _logger = logger;
            _measurements = new List<PerformanceMeasurement>();
            _overallStopwatch = new Stopwatch();
        }

        public void StartTest(string testName)
        {
            _currentTestName = testName;
            _measurements.Clear();
            _overallStopwatch.Restart();
            _logger?.LogInformation("Starting performance test: {TestName}", testName);
        }

        public async Task<PerformanceResult<T>> MeasureAsync<T>(
            string operationName, 
            Func<Task<T>> operation, 
            int iterations = 1,
            bool warmup = true)
        {
            if (_currentTestName == null)
                throw new InvalidOperationException("Test not started. Call StartTest first.");

            var results = new List<T>();
            var timings = new List<TimeSpan>();
            var memoryBefore = GC.GetTotalMemory(false);

            // Warmup
            if (warmup && iterations > 1)
            {
                _logger?.LogDebug("Performing warmup for {Operation}", operationName);
                await operation();
                GC.Collect();
                await Task.Delay(50); // Brief pause after warmup
            }

            _logger?.LogInformation("Measuring {Operation} over {Iterations} iterations", operationName, iterations);

            // Main measurements
            for (int i = 0; i < iterations; i++)
            {
                var sw = Stopwatch.StartNew();
                
                var result = await operation();
                
                sw.Stop();
                results.Add(result);
                timings.Add(sw.Elapsed);
                
                if (i > 0 && i % Math.Max(1, iterations / 10) == 0)
                {
                    _logger?.LogDebug("Completed {Current}/{Total} iterations", i + 1, iterations);
                }
            }

            var memoryAfter = GC.GetTotalMemory(false);

            var measurement = new PerformanceMeasurement
            {
                OperationName = operationName,
                Iterations = iterations,
                Timings = timings.ToArray(),
                MemoryUsed = memoryAfter - memoryBefore,
                Timestamp = DateTime.UtcNow
            };

            _measurements.Add(measurement);

            return new PerformanceResult<T>
            {
                Results = results.ToArray(),
                Measurement = measurement
            };
        }

        public async Task<PerformanceResult> MeasureAsync(
            string operationName, 
            Func<Task> operation, 
            int iterations = 1,
            bool warmup = true)
        {
            var result = await MeasureAsync(operationName, async () =>
            {
                await operation();
                return true; // dummy return value
            }, iterations, warmup);

            return new PerformanceResult
            {
                Measurement = result.Measurement
            };
        }

        public PerformanceResult<T> Measure<T>(
            string operationName,
            Func<T> operation,
            int iterations = 1,
            bool warmup = true)
        {
            return MeasureAsync(operationName, () => Task.FromResult(operation()), iterations, warmup).Result;
        }

        public PerformanceResult Measure(
            string operationName,
            Action operation,
            int iterations = 1,
            bool warmup = true)
        {
            return MeasureAsync(operationName, () =>
            {
                operation();
                return Task.CompletedTask;
            }, iterations, warmup).Result;
        }

        public async Task<ConcurrencyPerformanceResult> MeasureConcurrentAsync<T>(
            string operationName,
            Func<int, Task<T>> operation, // Function that takes a task index
            int concurrentTasks,
            int iterationsPerTask = 1)
        {
            if (_currentTestName == null)
                throw new InvalidOperationException("Test not started. Call StartTest first.");

            _logger?.LogInformation("Measuring concurrent {Operation}: {Tasks} tasks, {Iterations} iterations each", 
                operationName, concurrentTasks, iterationsPerTask);

            var overallStopwatch = Stopwatch.StartNew();
            var memoryBefore = GC.GetTotalMemory(false);
            var results = new List<T>();
            var taskTimings = new List<TimeSpan>();

            var tasks = Enumerable.Range(0, concurrentTasks).Select(async taskIndex =>
            {
                var taskResults = new List<T>();
                var taskStopwatch = Stopwatch.StartNew();

                for (int i = 0; i < iterationsPerTask; i++)
                {
                    var result = await operation(taskIndex);
                    taskResults.Add(result);
                }

                taskStopwatch.Stop();
                return new { Results = taskResults, Duration = taskStopwatch.Elapsed };
            });

            var taskResults = await Task.WhenAll(tasks);
            overallStopwatch.Stop();

            var memoryAfter = GC.GetTotalMemory(false);

            foreach (var taskResult in taskResults)
            {
                results.AddRange(taskResult.Results);
                taskTimings.Add(taskResult.Duration);
            }

            var measurement = new ConcurrencyPerformanceMeasurement
            {
                OperationName = operationName,
                ConcurrentTasks = concurrentTasks,
                IterationsPerTask = iterationsPerTask,
                TotalIterations = concurrentTasks * iterationsPerTask,
                OverallDuration = overallStopwatch.Elapsed,
                TaskTimings = taskTimings.ToArray(),
                MemoryUsed = memoryAfter - memoryBefore,
                Timestamp = DateTime.UtcNow
            };

            return new ConcurrencyPerformanceResult
            {
                Results = results.Cast<object>().ToArray(),
                Measurement = measurement
            };
        }

        public PerformanceTestReport EndTest()
        {
            if (_currentTestName == null)
                throw new InvalidOperationException("Test not started. Call StartTest first.");

            _overallStopwatch.Stop();

            var report = new PerformanceTestReport
            {
                TestName = _currentTestName,
                OverallDuration = _overallStopwatch.Elapsed,
                Measurements = _measurements.ToArray(),
                CompletedAt = DateTime.UtcNow
            };

            _logger?.LogInformation("Performance test completed: {TestName} in {Duration}ms", 
                _currentTestName, _overallStopwatch.ElapsedMilliseconds);

            LogSummary(report);

            _currentTestName = null;
            return report;
        }

        private void LogSummary(PerformanceTestReport report)
        {
            _logger?.LogInformation("Performance Test Summary:");
            _logger?.LogInformation("  Test: {TestName}", report.TestName);
            _logger?.LogInformation("  Overall Duration: {Duration}ms", report.OverallDuration.TotalMilliseconds);
            _logger?.LogInformation("  Operations: {Count}", report.Measurements.Length);

            foreach (var measurement in report.Measurements)
            {
                var avgMs = measurement.AverageTime.TotalMilliseconds;
                var minMs = measurement.MinTime.TotalMilliseconds;
                var maxMs = measurement.MaxTime.TotalMilliseconds;
                var throughput = measurement.Throughput;

                _logger?.LogInformation("    {Operation}: Avg={Avg:F2}ms, Min={Min:F2}ms, Max={Max:F2}ms, Throughput={Throughput:F1}/s",
                    measurement.OperationName, avgMs, minMs, maxMs, throughput);
            }
        }

        public static PerformanceBenchmark CreateBenchmark(string name, ILogger? logger = null)
        {
            return new PerformanceBenchmark(name, logger);
        }

        public IDisposable StartMeasurement(string measurementName)
        {
            return new MeasurementContext(measurementName, _logger);
        }

        public double GetPeakMemoryUsageMB()
        {
            return Process.GetCurrentProcess().PeakWorkingSet64 / (1024.0 * 1024.0);
        }

        public void Dispose()
        {
            _overallStopwatch?.Stop();
        }
    }

    public class PerformanceBenchmark
    {
        private readonly string _name;
        private readonly ILogger? _logger;
        private readonly Dictionary<string, BenchmarkScenario> _scenarios;

        internal PerformanceBenchmark(string name, ILogger? logger)
        {
            _name = name;
            _logger = logger;
            _scenarios = new Dictionary<string, BenchmarkScenario>();
        }

        public PerformanceBenchmark AddScenario(string name, Func<Task> scenario, int iterations = 100)
        {
            _scenarios[name] = new BenchmarkScenario
            {
                Name = name,
                Action = scenario,
                Iterations = iterations
            };
            return this;
        }

        public async Task<BenchmarkReport> RunAsync()
        {
            _logger?.LogInformation("Starting benchmark: {Name}", _name);

            var results = new List<PerformanceMeasurement>();

            foreach (var scenario in _scenarios.Values)
            {
                using var harness = new PerformanceTestHarness(_logger);
                harness.StartTest($"{_name} - {scenario.Name}");

                await harness.MeasureAsync(scenario.Name, scenario.Action, scenario.Iterations);

                var report = harness.EndTest();
                results.AddRange(report.Measurements);
            }

            return new BenchmarkReport
            {
                BenchmarkName = _name,
                Measurements = results.ToArray(),
                CompletedAt = DateTime.UtcNow
            };
        }

        private class BenchmarkScenario
        {
            public string Name { get; set; } = string.Empty;
            public Func<Task> Action { get; set; } = () => Task.CompletedTask;
            public int Iterations { get; set; }
        }
    }

    public class PerformanceResult<T>
    {
        public T[] Results { get; set; } = Array.Empty<T>();
        public PerformanceMeasurement Measurement { get; set; } = new();
    }

    public class PerformanceResult
    {
        public PerformanceMeasurement Measurement { get; set; } = new();
    }

    public class ConcurrencyPerformanceResult
    {
        public object[] Results { get; set; } = Array.Empty<object>();
        public ConcurrencyPerformanceMeasurement Measurement { get; set; } = new();
    }

    public class PerformanceMeasurement
    {
        public string OperationName { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public TimeSpan[] Timings { get; set; } = Array.Empty<TimeSpan>();
        public long MemoryUsed { get; set; }
        public DateTime Timestamp { get; set; }

        public TimeSpan TotalTime => TimeSpan.FromTicks(Timings.Sum(t => t.Ticks));
        public TimeSpan AverageTime => TimeSpan.FromTicks(Timings.Sum(t => t.Ticks) / Timings.Length);
        public TimeSpan MinTime => Timings.Min();
        public TimeSpan MaxTime => Timings.Max();
        public double StandardDeviation => CalculateStandardDeviation();
        public double Throughput => Iterations / TotalTime.TotalSeconds;

        private double CalculateStandardDeviation()
        {
            if (Timings.Length <= 1) return 0;

            var avgTicks = Timings.Average(t => t.Ticks);
            var variance = Timings.Select(t => Math.Pow(t.Ticks - avgTicks, 2)).Average();
            return Math.Sqrt(variance) / TimeSpan.TicksPerMillisecond; // Convert to milliseconds
        }
    }

    public class ConcurrencyPerformanceMeasurement
    {
        public string OperationName { get; set; } = string.Empty;
        public int ConcurrentTasks { get; set; }
        public int IterationsPerTask { get; set; }
        public int TotalIterations { get; set; }
        public TimeSpan OverallDuration { get; set; }
        public TimeSpan[] TaskTimings { get; set; } = Array.Empty<TimeSpan>();
        public long MemoryUsed { get; set; }
        public DateTime Timestamp { get; set; }

        public TimeSpan AverageTaskTime => TimeSpan.FromTicks(TaskTimings.Sum(t => t.Ticks) / TaskTimings.Length);
        public TimeSpan MaxTaskTime => TaskTimings.Max();
        public TimeSpan MinTaskTime => TaskTimings.Min();
        public double OverallThroughput => TotalIterations / OverallDuration.TotalSeconds;
        public double ParallelismEfficiency => TaskTimings.Sum(t => t.TotalMilliseconds) / OverallDuration.TotalMilliseconds / ConcurrentTasks;
    }

    public class PerformanceTestReport
    {
        public string TestName { get; set; } = string.Empty;
        public TimeSpan OverallDuration { get; set; }
        public PerformanceMeasurement[] Measurements { get; set; } = Array.Empty<PerformanceMeasurement>();
        public DateTime CompletedAt { get; set; }

        public double TotalThroughput => Measurements.Sum(m => m.Throughput);
        public TimeSpan TotalMeasurementTime => TimeSpan.FromTicks(Measurements.Sum(m => m.TotalTime.Ticks));
    }

    public class BenchmarkReport
    {
        public string BenchmarkName { get; set; } = string.Empty;
        public PerformanceMeasurement[] Measurements { get; set; } = Array.Empty<PerformanceMeasurement>();
        public DateTime CompletedAt { get; set; }

        public PerformanceMeasurement? GetBestPerforming() => 
            Measurements.OrderByDescending(m => m.Throughput).FirstOrDefault();

        public PerformanceMeasurement? GetWorstPerforming() => 
            Measurements.OrderBy(m => m.Throughput).FirstOrDefault();
    }

    public class MeasurementContext : IDisposable
    {
        private readonly string _name;
        private readonly ILogger? _logger;
        private readonly Stopwatch _stopwatch;

        public MeasurementContext(string name, ILogger? logger)
        {
            _name = name;
            _logger = logger;
            _stopwatch = Stopwatch.StartNew();
            _logger?.LogDebug("Started measurement: {Name}", name);
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _logger?.LogDebug("Completed measurement: {Name} in {Duration}ms", _name, _stopwatch.ElapsedMilliseconds);
        }
    }
}