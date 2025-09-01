using System;
using System.Text;
using System.Threading;

namespace TestIntelligence.CLI.Progress
{
    /// <summary>
    /// Console-based progress bar implementation.
    /// </summary>
    public class ConsoleProgressBar : IProgressReporter, IDisposable
    {
        private readonly object _lock = new object();
        private readonly Timer _refreshTimer;
        private bool _disposed = false;
        private int _lastLineCount = 0;
        private ProgressInfo? _currentProgress;
        private readonly bool _showDetails;
        private readonly int _barWidth;
        
        public ConsoleProgressBar(bool showDetails = true, int barWidth = 40)
        {
            _showDetails = showDetails;
            _barWidth = barWidth;
            _currentProgress = new ProgressInfo(0, "Initializing...");
            
            // Refresh the progress bar every 100ms for smooth animation
            _refreshTimer = new Timer(RefreshDisplay, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        }
        
        public void ReportProgress(int percentage, string message)
        {
            ReportProgress(percentage, message, null);
        }
        
        public void ReportProgress(int percentage, string message, string? detail)
        {
            lock (_lock)
            {
                if (_disposed) return;
                
                _currentProgress = new ProgressInfo(percentage, message, detail);
            }
        }
        
        public void Complete(string? completionMessage = null)
        {
            lock (_lock)
            {
                if (_disposed) return;
                
                // Clear the progress display
                ClearCurrentDisplay();
                
                // Show completion message
                var message = completionMessage ?? "✅ Operation completed successfully";
                Console.WriteLine(message);
                Console.WriteLine();
                
                _disposed = true;
            }
        }
        
        public void ReportError(string errorMessage)
        {
            lock (_lock)
            {
                if (_disposed) return;
                
                // Clear the progress display
                ClearCurrentDisplay();
                
                // Show error message
                Console.WriteLine($"❌ Error: {errorMessage}");
                Console.WriteLine();
                
                _disposed = true;
            }
        }
        
        private void RefreshDisplay(object? state)
        {
            lock (_lock)
            {
                if (_disposed || _currentProgress == null) return;
                
                try
                {
                    RenderProgressBar(_currentProgress);
                }
                catch
                {
                    // Ignore rendering errors to prevent crashes
                }
            }
        }
        
        private void RenderProgressBar(ProgressInfo progress)
        {
            // Clear previous display
            ClearCurrentDisplay();
            
            var output = new StringBuilder();
            
            // Build progress bar
            var filledWidth = (int)(_barWidth * progress.Percentage / 100.0);
            var emptyWidth = _barWidth - filledWidth;
            
            var progressBar = new StringBuilder();
            progressBar.Append('[');
            progressBar.Append(new string('█', filledWidth));
            progressBar.Append(new string('░', emptyWidth));
            progressBar.Append(']');
            
            // Main progress line
            output.AppendLine($"{progressBar} {progress.Percentage:D3}% {progress.Message}");
            
            // Detail line if enabled and available
            if (_showDetails && !string.IsNullOrWhiteSpace(progress.Detail))
            {
                var detailPrefix = "  └─ ";
                var maxDetailLength = Console.WindowWidth - detailPrefix.Length - 1;
                var detail = progress.Detail.Length > maxDetailLength 
                    ? progress.Detail.Substring(0, maxDetailLength - 3) + "..."
                    : progress.Detail;
                    
                output.AppendLine($"{detailPrefix}{detail}");
            }
            
            // Write to console
            var outputText = output.ToString();
            Console.Write(outputText);
            
            // Track how many lines we wrote
            _lastLineCount = outputText.Split('\n').Length - 1;
        }
        
        private void ClearCurrentDisplay()
        {
            if (_lastLineCount > 0)
            {
                // Move cursor up and clear each line
                for (int i = 0; i < _lastLineCount; i++)
                {
                    Console.SetCursorPosition(0, Console.CursorTop - 1);
                    Console.Write(new string(' ', Console.WindowWidth - 1));
                    Console.SetCursorPosition(0, Console.CursorTop);
                }
                _lastLineCount = 0;
            }
        }
        
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                
                _refreshTimer?.Dispose();
                ClearCurrentDisplay();
                _disposed = true;
            }
        }
    }
}