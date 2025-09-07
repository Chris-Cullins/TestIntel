using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TestIntelligence.TestUtilities
{
    public class MemoryPressureTestHarness : IDisposable
    {
        private readonly ILogger? _logger;
        private readonly List<MemorySnapshot> _snapshots;
        private readonly Timer? _monitoringTimer;
        private long _initialMemory;
        private long _peakMemory;
        private DateTime _testStartTime;
        private bool _isMonitoring;

        public MemoryPressureTestHarness(ILogger? logger = null, bool enableContinuousMonitoring = false)
        {
            _logger = logger;
            _snapshots = new List<MemorySnapshot>();
            
            if (enableContinuousMonitoring)
            {
                _monitoringTimer = new Timer(TakeSnapshot, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
            }
        }

        public void StartTest(string testName = "Memory Pressure Test")
        {
            _testStartTime = DateTime.UtcNow;
            _initialMemory = GC.GetTotalMemory(true);
            _peakMemory = _initialMemory;
            _isMonitoring = true;
            
            _snapshots.Clear();
            TakeSnapshot($"Start: {testName}");
            
            _logger?.LogInformation("Starting memory pressure test: {TestName}", testName);
        }

        public void TakeSnapshot(string? label = null)
        {
            if (!_isMonitoring) return;
            
            var currentMemory = GC.GetTotalMemory(false);
            _peakMemory = Math.Max(_peakMemory, currentMemory);
            
            var snapshot = new MemorySnapshot
            {
                Timestamp = DateTime.UtcNow,
                Label = label ?? $"Snapshot {_snapshots.Count + 1}",
                TotalMemory = currentMemory,
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),
                MemoryDelta = currentMemory - _initialMemory
            };
            
            _snapshots.Add(snapshot);
        }

        public void TakeSnapshot(object? state)
        {
            TakeSnapshot(state?.ToString());
        }

        public MemoryPressureTestResult EndTest(string? finalLabel = null)
        {
            if (!_isMonitoring) throw new InvalidOperationException("Test not started");
            
            _isMonitoring = false;
            
            // Force garbage collection before final measurement
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            TakeSnapshot(finalLabel ?? "End");
            
            var finalMemory = GC.GetTotalMemory(true);
            var testDuration = DateTime.UtcNow - _testStartTime;
            
            var result = new MemoryPressureTestResult
            {
                InitialMemory = _initialMemory,
                FinalMemory = finalMemory,
                PeakMemory = _peakMemory,
                RetainedMemory = finalMemory - _initialMemory,
                MaxMemoryIncrease = _peakMemory - _initialMemory,
                TestDuration = testDuration,
                Snapshots = _snapshots.ToArray(),
                MemoryLeaked = finalMemory > _initialMemory * 1.1 // 10% tolerance
            };
            
            _logger?.LogInformation("Memory pressure test completed. Peak: {PeakMemoryMB:F2}MB, Retained: {RetainedMemoryMB:F2}MB", 
                result.PeakMemory / 1024.0 / 1024.0, 
                result.RetainedMemory / 1024.0 / 1024.0);
            
            return result;
        }

        public async Task<MemoryPressureTestResult> RunTestAsync<T>(
            Func<IMemoryPressureContext, Task<T>> testAction, 
            string testName = "Memory Test",
            MemoryPressureConfiguration? config = null)
        {
            config ??= new MemoryPressureConfiguration();
            
            StartTest(testName);
            
            var context = new MemoryPressureContext(this, config);
            
            try
            {
                await testAction(context);
                
                // Verify memory limits weren't exceeded during test
                if (config.MaxMemoryLimitBytes.HasValue)
                {
                    var currentUsage = _peakMemory - _initialMemory;
                    if (currentUsage > config.MaxMemoryLimitBytes.Value)
                    {
                        throw new MemoryLimitExceededException(
                            $"Memory usage ({currentUsage:N0} bytes) exceeded limit ({config.MaxMemoryLimitBytes.Value:N0} bytes)");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Memory pressure test failed");
                throw;
            }
            
            return EndTest();
        }

        public async Task SimulateMemoryPressure(int allocationSizeMB = 50, int durationSeconds = 5)
        {
            _logger?.LogInformation("Simulating memory pressure: {SizeMB}MB for {Duration}s", allocationSizeMB, durationSeconds);
            
            var allocations = new List<byte[]>();
            var endTime = DateTime.UtcNow.AddSeconds(durationSeconds);
            
            try
            {
                // Allocate memory in chunks
                while (DateTime.UtcNow < endTime)
                {
                    var chunk = new byte[1024 * 1024]; // 1MB chunks
                    
                    // Fill with some data to prevent optimization
                    for (int i = 0; i < chunk.Length; i += 4096)
                    {
                        chunk[i] = (byte)(i % 256);
                    }
                    
                    allocations.Add(chunk);
                    TakeSnapshot($"Allocated {allocations.Count}MB");
                    
                    if (allocations.Count >= allocationSizeMB)
                    {
                        break;
                    }
                    
                    await Task.Delay(100); // Small delay to allow monitoring
                }
                
                // Hold the memory for the duration
                var remainingTime = endTime - DateTime.UtcNow;
                if (remainingTime > TimeSpan.Zero)
                {
                    await Task.Delay(remainingTime);
                }
            }
            finally
            {
                // Release allocations
                allocations.Clear();
                TakeSnapshot("Memory pressure released");
            }
        }

        public void ForceGarbageCollection()
        {
            TakeSnapshot("Before GC");
            
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            
            TakeSnapshot("After GC");
        }

        public void EnableServerGC()
        {
            if (GCSettings.IsServerGC)
            {
                _logger?.LogInformation("Server GC is already enabled");
                return;
            }
            
            _logger?.LogInformation("Enabling Server GC mode");
            // Note: GC mode can only be set at application startup via config
            // This method serves as a documentation point for test setup
        }

        public double GetMemoryEfficiencyRatio()
        {
            if (_snapshots.Count < 2) return 1.0;
            
            var maxIncrease = _peakMemory - _initialMemory;
            var retainedIncrease = GC.GetTotalMemory(true) - _initialMemory;
            
            return maxIncrease > 0 ? (double)retainedIncrease / maxIncrease : 1.0;
        }

        public void Dispose()
        {
            _monitoringTimer?.Dispose();
            _isMonitoring = false;
        }
    }

    public class MemoryPressureContext : IMemoryPressureContext
    {
        private readonly MemoryPressureTestHarness _harness;
        private readonly MemoryPressureConfiguration _config;

        internal MemoryPressureContext(MemoryPressureTestHarness harness, MemoryPressureConfiguration config)
        {
            _harness = harness;
            _config = config;
        }

        public void TakeSnapshot(string label) => _harness.TakeSnapshot(label);
        
        public async Task SimulateMemoryPressure(int sizeMB = 50, int durationSeconds = 5) 
            => await _harness.SimulateMemoryPressure(sizeMB, durationSeconds);
        
        public void ForceGarbageCollection() => _harness.ForceGarbageCollection();
        
        public bool ShouldTriggerGC()
        {
            if (!_config.AutoGCThresholdBytes.HasValue) return false;
            
            var currentMemory = GC.GetTotalMemory(false);
            return currentMemory > _config.AutoGCThresholdBytes.Value;
        }

        public void TriggerGCIfNeeded()
        {
            if (ShouldTriggerGC())
            {
                ForceGarbageCollection();
            }
        }
    }

    public interface IMemoryPressureContext
    {
        void TakeSnapshot(string label);
        Task SimulateMemoryPressure(int sizeMB = 50, int durationSeconds = 5);
        void ForceGarbageCollection();
        bool ShouldTriggerGC();
        void TriggerGCIfNeeded();
    }

    public class MemoryPressureConfiguration
    {
        public long? MaxMemoryLimitBytes { get; set; }
        public long? AutoGCThresholdBytes { get; set; } = 100 * 1024 * 1024; // 100MB default
        public bool EnableContinuousMonitoring { get; set; } = true;
        public TimeSpan MonitoringInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    }

    public class MemoryPressureTestResult
    {
        public long InitialMemory { get; set; }
        public long FinalMemory { get; set; }
        public long PeakMemory { get; set; }
        public long RetainedMemory { get; set; }
        public long MaxMemoryIncrease { get; set; }
        public TimeSpan TestDuration { get; set; }
        public MemorySnapshot[] Snapshots { get; set; } = Array.Empty<MemorySnapshot>();
        public bool MemoryLeaked { get; set; }

        public double InitialMemoryMB => InitialMemory / 1024.0 / 1024.0;
        public double FinalMemoryMB => FinalMemory / 1024.0 / 1024.0;
        public double PeakMemoryMB => PeakMemory / 1024.0 / 1024.0;
        public double RetainedMemoryMB => RetainedMemory / 1024.0 / 1024.0;
        public double MaxMemoryIncreaseMB => MaxMemoryIncrease / 1024.0 / 1024.0;
        
        public double MemoryEfficiencyRatio => MaxMemoryIncrease > 0 ? (double)RetainedMemory / MaxMemoryIncrease : 1.0;
    }

    public class MemorySnapshot
    {
        public DateTime Timestamp { get; set; }
        public string Label { get; set; } = string.Empty;
        public long TotalMemory { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        public long MemoryDelta { get; set; }

        public double TotalMemoryMB => TotalMemory / 1024.0 / 1024.0;
        public double MemoryDeltaMB => MemoryDelta / 1024.0 / 1024.0;
    }

    public class MemoryLimitExceededException : Exception
    {
        public MemoryLimitExceededException(string message) : base(message) { }
        public MemoryLimitExceededException(string message, Exception innerException) : base(message, innerException) { }
    }
}