namespace TestIntelligence.ImpactAnalyzer.Analysis
{
    public enum TypeUsageContext
    {
        Declaration,
        Reference,
        Inheritance,
        Implementation,
        Generic
    }

    public class TypeUsageInfo
    {
        public string TypeName { get; }
        public string Namespace { get; }
        public string FilePath { get; }
        public int LineNumber { get; }
        public TypeUsageContext Context { get; }

        public TypeUsageInfo(string typeName, string nameSpace, string filePath, int lineNumber, TypeUsageContext context)
        {
            TypeName = typeName ?? throw new System.ArgumentNullException(nameof(typeName));
            Namespace = nameSpace ?? string.Empty;
            FilePath = filePath ?? throw new System.ArgumentNullException(nameof(filePath));
            LineNumber = lineNumber;
            Context = context;
        }

        public override string ToString()
        {
            var fullName = string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";
            return $"{Context}: {fullName} at {System.IO.Path.GetFileName(FilePath)}:{LineNumber}";
        }
    }
}