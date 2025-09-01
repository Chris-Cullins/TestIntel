using System;
using System.Collections.Generic;
using System.Linq;

namespace TestIntelligence.CLI.Progress
{
    /// <summary>
    /// Specialized progress tracker for cache operations.
    /// </summary>
    public class CacheOperationProgress
    {
        private readonly IProgressReporter _reporter;
        private readonly List<CacheOperationStep> _steps;
        private int _currentStepIndex;
        private int _totalSteps;
        
        public CacheOperationProgress(IProgressReporter reporter)
        {
            _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
            _steps = new List<CacheOperationStep>();
            _currentStepIndex = 0;
        }
        
        /// <summary>
        /// Defines the steps for the cache operation.
        /// </summary>
        public void DefineSteps(params CacheOperationStep[] steps)
        {
            _steps.Clear();
            _steps.AddRange(steps);
            _totalSteps = _steps.Sum(s => s.Weight);
            _currentStepIndex = 0;
        }
        
        /// <summary>
        /// Starts the next step in the operation.
        /// </summary>
        public void StartNextStep()
        {
            if (_currentStepIndex < _steps.Count)
            {
                var step = _steps[_currentStepIndex];
                var completedWeight = _steps.Take(_currentStepIndex).Sum(s => s.Weight);
                var percentage = (int)((completedWeight * 100.0) / _totalSteps);
                
                _reporter.ReportProgress(percentage, step.Description);
                _currentStepIndex++;
            }
        }
        
        /// <summary>
        /// Reports progress within the current step.
        /// </summary>
        public void ReportStepProgress(int stepPercentage, string? detail = null)
        {
            if (_currentStepIndex == 0) return;
            
            var currentStep = _steps[_currentStepIndex - 1];
            var completedWeight = _steps.Take(_currentStepIndex - 1).Sum(s => s.Weight);
            var currentStepProgress = (currentStep.Weight * stepPercentage) / 100.0;
            var totalProgress = (completedWeight + currentStepProgress) / _totalSteps;
            var percentage = (int)(totalProgress * 100);
            
            _reporter.ReportProgress(percentage, currentStep.Description, detail);
        }
        
        /// <summary>
        /// Completes the current step and moves to the next.
        /// </summary>
        public void CompleteCurrentStep(string? detail = null)
        {
            ReportStepProgress(100, detail);
        }
        
        /// <summary>
        /// Completes all remaining steps and finishes the operation.
        /// </summary>
        public void Complete(string? completionMessage = null)
        {
            _reporter.ReportProgress(100, "Finalizing...");
            _reporter.Complete(completionMessage);
        }
        
        /// <summary>
        /// Reports an error during the operation.
        /// </summary>
        public void ReportError(string errorMessage)
        {
            _reporter.ReportError(errorMessage);
        }
    }
    
    /// <summary>
    /// Represents a step in a cache operation.
    /// </summary>
    public class CacheOperationStep
    {
        public string Description { get; set; }
        public int Weight { get; set; }
        
        public CacheOperationStep(string description, int weight = 1)
        {
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Weight = Math.Max(1, weight);
        }
    }
    
    /// <summary>
    /// Pre-defined cache operation steps for common scenarios.
    /// </summary>
    public static class CacheOperationSteps
    {
        public static readonly CacheOperationStep InitializingCache = 
            new CacheOperationStep("Initializing cache system", 1);
            
        public static readonly CacheOperationStep AnalyzingSolution = 
            new CacheOperationStep("Analyzing solution structure", 2);
            
        public static readonly CacheOperationStep LoadingProjects = 
            new CacheOperationStep("Loading project metadata", 3);
            
        public static readonly CacheOperationStep BuildingCompilations = 
            new CacheOperationStep("Building Roslyn compilations", 5);
            
        public static readonly CacheOperationStep GeneratingCallGraphs = 
            new CacheOperationStep("Generating call graphs", 4);
            
        public static readonly CacheOperationStep DiscoveringTests = 
            new CacheOperationStep("Discovering test methods", 3);
            
        public static readonly CacheOperationStep CachingMetadata = 
            new CacheOperationStep("Caching metadata", 2);
            
        public static readonly CacheOperationStep CreatingSnapshot = 
            new CacheOperationStep("Creating solution snapshot", 1);
            
        public static readonly CacheOperationStep ValidatingCache = 
            new CacheOperationStep("Validating cache integrity", 1);
            
        // Pre-defined operation sequences
        public static CacheOperationStep[] CacheInitSequence => new[]
        {
            InitializingCache,
            AnalyzingSolution,
            CreatingSnapshot
        };
        
        public static CacheOperationStep[] CacheWarmUpSequence => new[]
        {
            InitializingCache,
            AnalyzingSolution,
            LoadingProjects,
            BuildingCompilations,
            GeneratingCallGraphs,
            DiscoveringTests,
            CachingMetadata,
            CreatingSnapshot,
            ValidatingCache
        };
        
        public static CacheOperationStep[] CacheStatusSequence => new[]
        {
            new CacheOperationStep("Reading cache statistics", 1),
            new CacheOperationStep("Analyzing cache health", 1)
        };
        
        public static CacheOperationStep[] CacheClearSequence => new[]
        {
            new CacheOperationStep("Clearing cache files", 2),
            new CacheOperationStep("Cleaning up directories", 1)
        };
    }
}