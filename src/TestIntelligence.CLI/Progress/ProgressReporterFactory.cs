using System;

namespace TestIntelligence.CLI.Progress
{
    /// <summary>
    /// Factory for creating progress reporters based on context.
    /// </summary>
    public static class ProgressReporterFactory
    {
        /// <summary>
        /// Creates a progress reporter appropriate for the current context.
        /// </summary>
        /// <param name="verbose">Whether to show detailed progress information</param>
        /// <param name="forceConsole">Force console output even in non-interactive environments</param>
        /// <returns>An appropriate progress reporter implementation</returns>
        public static IProgressReporter Create(bool verbose = true, bool forceConsole = false)
        {
            // Check if we're in an interactive console environment
            if (IsInteractiveConsole() || forceConsole)
            {
                return new ConsoleProgressBar(showDetails: verbose);
            }
            else
            {
                // For non-interactive environments (CI/CD, redirected output, etc.)
                return new SimpleProgressReporter(verbose);
            }
        }
        
        /// <summary>
        /// Creates a no-op progress reporter that doesn't display anything.
        /// </summary>
        /// <returns>A silent progress reporter</returns>
        public static IProgressReporter CreateSilent()
        {
            return new SilentProgressReporter();
        }
        
        private static bool IsInteractiveConsole()
        {
            try
            {
                // Check if we have a proper console window
                return !Console.IsOutputRedirected && 
                       !Console.IsErrorRedirected && 
                       Console.WindowWidth > 0 && 
                       Console.WindowHeight > 0;
            }
            catch
            {
                return false;
            }
        }
    }
    
    /// <summary>
    /// Simple progress reporter for non-interactive environments.
    /// </summary>
    internal class SimpleProgressReporter : IProgressReporter
    {
        private readonly bool _verbose;
        private int _lastPercentage = -1;
        
        public SimpleProgressReporter(bool verbose)
        {
            _verbose = verbose;
        }
        
        public void ReportProgress(int percentage, string message)
        {
            ReportProgress(percentage, message, null);
        }
        
        public void ReportProgress(int percentage, string message, string? detail)
        {
            // Only show progress at 10% intervals to avoid spam
            if (percentage != _lastPercentage && percentage % 10 == 0)
            {
                Console.WriteLine($"[{percentage:D3}%] {message}");
                _lastPercentage = percentage;
            }
            
            if (_verbose && !string.IsNullOrWhiteSpace(detail))
            {
                Console.WriteLine($"  - {detail}");
            }
        }
        
        public void Complete(string? completionMessage = null)
        {
            var message = completionMessage ?? "Operation completed successfully";
            Console.WriteLine($"✅ {message}");
        }
        
        public void ReportError(string errorMessage)
        {
            Console.WriteLine($"❌ Error: {errorMessage}");
        }

        public void Dispose()
        {
            // Nothing to dispose for simple reporter
        }
    }
    
    /// <summary>
    /// Silent progress reporter that doesn't output anything.
    /// </summary>
    internal class SilentProgressReporter : IProgressReporter
    {
        public void ReportProgress(int percentage, string message) { }
        public void ReportProgress(int percentage, string message, string? detail) { }
        public void Complete(string? completionMessage = null) { }
        public void ReportError(string errorMessage) { }
        public void Dispose() { }
    }
}