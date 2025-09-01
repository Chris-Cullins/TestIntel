using System;

namespace TestIntelligence.CLI.Progress
{
    /// <summary>
    /// Interface for reporting progress during long-running operations.
    /// </summary>
    public interface IProgressReporter : IDisposable
    {
        /// <summary>
        /// Reports progress for the current operation.
        /// </summary>
        /// <param name="percentage">Progress as a percentage (0-100)</param>
        /// <param name="message">Current operation message</param>
        void ReportProgress(int percentage, string message);
        
        /// <summary>
        /// Reports progress with additional details.
        /// </summary>
        /// <param name="percentage">Progress as a percentage (0-100)</param>
        /// <param name="message">Current operation message</param>
        /// <param name="detail">Additional detail about current operation</param>
        void ReportProgress(int percentage, string message, string? detail);
        
        /// <summary>
        /// Completes the progress reporting and cleans up the display.
        /// </summary>
        /// <param name="completionMessage">Optional completion message</param>
        void Complete(string? completionMessage = null);
        
        /// <summary>
        /// Reports an error during the operation.
        /// </summary>
        /// <param name="errorMessage">Error message to display</param>
        void ReportError(string errorMessage);
    }
    
    /// <summary>
    /// Progress information for operations.
    /// </summary>
    public class ProgressInfo
    {
        public int Percentage { get; set; }
        public string Message { get; set; }
        public string? Detail { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        
        public ProgressInfo(int percentage, string message, string? detail = null)
        {
            Percentage = Math.Max(0, Math.Min(100, percentage));
            Message = message ?? string.Empty;
            Detail = detail;
        }
    }
}