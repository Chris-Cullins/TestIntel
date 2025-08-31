using System;
using System.IO;

namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public class MethodInfo
    {
        public MethodInfo(string id, string name, string containingType, string filePath, int lineNumber, bool isTestMethod = false)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ContainingType = containingType ?? throw new ArgumentNullException(nameof(containingType));
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            LineNumber = lineNumber;
            IsTestMethod = isTestMethod;
        }

        public string Id { get; }
        public string Name { get; }
        public string ContainingType { get; }
        public string FilePath { get; }
        public int LineNumber { get; }
        public bool IsTestMethod { get; }

        public override string ToString()
        {
            return $"{ContainingType}.{Name} at {Path.GetFileName(FilePath)}:{LineNumber}";
        }
    }
}