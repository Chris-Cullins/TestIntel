using System.Linq;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public class TestCoverageResult
    {
        public string TestMethodId { get; }
        public string TestMethodName { get; }
        public string TestClassName { get; }
        public string TestFilePath { get; }
        public string[] CallPath { get; }
        public double Confidence { get; }
        public int CallDepth => CallPath.Length - 1;

        public TestCoverageResult(
            string testMethodId,
            string testMethodName,
            string testClassName,
            string testFilePath,
            string[] callPath,
            double confidence)
        {
            TestMethodId = testMethodId ?? throw new System.ArgumentNullException(nameof(testMethodId));
            TestMethodName = testMethodName ?? throw new System.ArgumentNullException(nameof(testMethodName));
            TestClassName = testClassName ?? throw new System.ArgumentNullException(nameof(testClassName));
            TestFilePath = testFilePath ?? throw new System.ArgumentNullException(nameof(testFilePath));
            CallPath = callPath ?? throw new System.ArgumentNullException(nameof(callPath));
            Confidence = confidence;
        }

        public override string ToString()
        {
            return $"{TestClassName}.{TestMethodName} (Confidence: {Confidence:F2}, Depth: {CallDepth})";
        }

        public string GetCallPathDisplay()
        {
            return string.Join(" -> ", CallPath.Select(p => p.Split('.').LastOrDefault() ?? p));
        }
    }
}