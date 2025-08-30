using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TestIntelligence.Core.Models;
using TestIntelligence.DataTracker.Models;

namespace TestIntelligence.DataTracker.Analysis
{
    /// <summary>
    /// Detects Entity Framework 6 usage patterns in test methods.
    /// </summary>
    public class EF6PatternDetector : IEF6PatternDetector
    {
        private static readonly string[] EF6Namespaces = 
        {
            "System.Data.Entity",
            "System.Data.Entity.Core",
            "System.Data.Entity.Infrastructure"
        };

        private static readonly string[] EF6ContextBaseTypes = 
        {
            "DbContext",
            "System.Data.Entity.DbContext"
        };

        private static readonly string[] EF6DatabaseOperationMethods = 
        {
            "Add", "AddRange", "Remove", "RemoveRange", "Update",
            "Find", "Where", "FirstOrDefault", "SingleOrDefault",
            "SaveChanges", "SaveChangesAsync"
        };

        /// <inheritdoc />
        public IReadOnlyList<DatabaseFramework> SupportedFrameworks => 
            new[] { DatabaseFramework.EntityFramework6 };

        /// <inheritdoc />
        public async Task<IReadOnlyList<DataDependency>> DetectDatabaseOperationsAsync(
            TestMethod testMethod, 
            CancellationToken cancellationToken = default)
        {
            if (testMethod == null)
                throw new ArgumentNullException(nameof(testMethod));

            cancellationToken.ThrowIfCancellationRequested();

            var dependencies = new List<DataDependency>();

            try
            {
                // Analyze the method using reflection to find EF6 patterns
                var methodBody = GetMethodBodySource(testMethod.MethodInfo);
                if (string.IsNullOrEmpty(methodBody))
                    return dependencies;

                var syntaxTree = CSharpSyntaxTree.ParseText(methodBody);
                var root = await syntaxTree.GetRootAsync(cancellationToken);

                // Look for DbContext instantiation and usage
                var contextUsages = await DetectDbContextUsageAsync(testMethod.MethodInfo);
                foreach (var usage in contextUsages)
                {
                    var dependency = new DataDependency(
                        testMethod.GetUniqueId(),
                        DataDependencyType.Database,
                        $"EF6:{usage.ContextType.Name}",
                        usage.AccessType,
                        usage.EntitySets);

                    dependencies.Add(dependency);
                }

                // Look for specific EF6 patterns in syntax
                dependencies.AddRange(await AnalyzeSyntaxForEF6Patterns(root, testMethod, cancellationToken));
            }
            catch (Exception ex)
            {
                // Log error but don't throw - return partial results
                System.Diagnostics.Debug.WriteLine($"Error detecting EF6 operations in {testMethod.MethodName}: {ex.Message}");
            }

            return dependencies;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<DataDependency>> DetectDatabaseOperationsAsync(
            IEnumerable<MethodInfo> setupMethods, 
            CancellationToken cancellationToken = default)
        {
            if (setupMethods == null)
                throw new ArgumentNullException(nameof(setupMethods));

            cancellationToken.ThrowIfCancellationRequested();

            var allDependencies = new List<DataDependency>();

            foreach (var method in setupMethods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var methodBody = GetMethodBodySource(method);
                    if (string.IsNullOrEmpty(methodBody))
                        continue;

                    var syntaxTree = CSharpSyntaxTree.ParseText(methodBody);
                    var root = await syntaxTree.GetRootAsync(cancellationToken);

                    // Create a temporary test method for analysis
                    var tempTestMethod = new TestMethod(method, method.DeclaringType!, 
                        "temp", TestIntelligence.Core.Assembly.FrameworkVersion.NetFramework48);

                    var dependencies = await AnalyzeSyntaxForEF6Patterns(root, tempTestMethod, cancellationToken);
                    allDependencies.AddRange(dependencies);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error analyzing setup method {method.Name}: {ex.Message}");
                }
            }

            return allDependencies;
        }

        /// <inheritdoc />
        public bool RequiresExclusiveDbAccess(TestMethod testMethod)
        {
            if (testMethod == null)
                throw new ArgumentNullException(nameof(testMethod));

            try
            {
                // Check if the test method has attributes indicating exclusive access
                foreach (var attribute in testMethod.TestAttributes)
                {
                    var attributeName = attribute.GetType().Name;
                    if (attributeName.Contains("Exclusive") || 
                        attributeName.Contains("Sequential") ||
                        attributeName.Contains("NonParallel"))
                    {
                        return true;
                    }
                }

                // Check method name patterns that typically indicate exclusive access
                var methodName = testMethod.MethodName.ToLowerInvariant();
                if (methodName.Contains("exclusive") ||
                    methodName.Contains("sequential") ||
                    methodName.Contains("migration") ||
                    methodName.Contains("schema") ||
                    methodName.Contains("database") && methodName.Contains("setup"))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                // Conservative approach - assume exclusive access if we can't determine
                return true;
            }
        }

        /// <inheritdoc />
        public bool SharesDatabaseDependency(
            TestMethod testA, 
            TestMethod testB, 
            IReadOnlyList<DataDependency> dependenciesA,
            IReadOnlyList<DataDependency> dependenciesB)
        {
            if (testA == null)
                throw new ArgumentNullException(nameof(testA));
            if (testB == null)
                throw new ArgumentNullException(nameof(testB));
            if (dependenciesA == null)
                throw new ArgumentNullException(nameof(dependenciesA));
            if (dependenciesB == null)
                throw new ArgumentNullException(nameof(dependenciesB));
            var dbDependenciesA = dependenciesA.Where(d => d.DependencyType == DataDependencyType.Database);
            var dbDependenciesB = dependenciesB.Where(d => d.DependencyType == DataDependencyType.Database);

            foreach (var depA in dbDependenciesA)
            {
                foreach (var depB in dbDependenciesB)
                {
                    // Check if they use the same resource identifier (context type)
                    if (depA.ResourceIdentifier == depB.ResourceIdentifier)
                        return true;

                    // Check if they share entity types
                    if (depA.EntityTypes.Intersect(depB.EntityTypes).Any())
                        return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<EF6ContextUsage>> DetectDbContextUsageAsync(MethodInfo method)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            var usages = new List<EF6ContextUsage>();

            try
            {
                // Check if method's declaring type or any field/property types derive from DbContext
                var contextTypes = FindDbContextTypes(method.DeclaringType!);

                foreach (var contextType in contextTypes)
                {
                    var entitySets = GetEntitySetsFromContextType(contextType);
                    var accessType = DetermineAccessType(method, contextType);

                    usages.Add(new EF6ContextUsage(contextType, entitySets, accessType));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting DbContext usage in {method.Name}: {ex.Message}");
            }

            return Task.FromResult<IReadOnlyList<EF6ContextUsage>>(usages);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<DataSeedingOperation>> DetectDataSeedingAsync(MethodInfo setupMethod)
        {
            if (setupMethod == null)
                throw new ArgumentNullException(nameof(setupMethod));

            var seedingOps = new List<DataSeedingOperation>();

            try
            {
                var methodBody = GetMethodBodySource(setupMethod);
                if (string.IsNullOrEmpty(methodBody))
                    return Task.FromResult<IReadOnlyList<DataSeedingOperation>>(seedingOps);

                // Look for common seeding patterns
                var lines = methodBody.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    
                    // Look for Add/AddRange operations
                    if (trimmedLine.Contains(".Add(") || trimmedLine.Contains(".AddRange("))
                    {
                        var entityType = ExtractEntityTypeFromLine(trimmedLine, setupMethod.DeclaringType!);
                        if (entityType != null)
                        {
                            var recordCount = EstimateRecordCount(trimmedLine);
                            seedingOps.Add(new DataSeedingOperation(entityType, "Insert", recordCount));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting data seeding in {setupMethod.Name}: {ex.Message}");
            }

            return Task.FromResult<IReadOnlyList<DataSeedingOperation>>(seedingOps);
        }

        private Task<IReadOnlyList<DataDependency>> AnalyzeSyntaxForEF6Patterns(
            SyntaxNode root, 
            TestMethod testMethod, 
            CancellationToken cancellationToken)
        {
            var dependencies = new List<DataDependency>();

            // Look for using statements with EF6 namespaces
            var usingDirectives = root.DescendantNodes().OfType<UsingDirectiveSyntax>();
            var hasEF6Using = usingDirectives.Any(u => 
                EF6Namespaces.Any(ns => u.Name?.ToString().StartsWith(ns) == true));

            if (!hasEF6Using)
                return Task.FromResult<IReadOnlyList<DataDependency>>(dependencies);

            // Look for variable declarations that might be DbContext
            var variableDeclarations = root.DescendantNodes().OfType<VariableDeclarationSyntax>();
            foreach (var declaration in variableDeclarations)
            {
                var typeName = declaration.Type.ToString();
                if (IsEF6ContextType(typeName))
                {
                    var dependency = new DataDependency(
                        testMethod.GetUniqueId(),
                        DataDependencyType.Database,
                        $"EF6:{typeName}",
                        DataAccessType.ReadWrite,
                        new[] { typeName });

                    dependencies.Add(dependency);
                }
            }

            return Task.FromResult<IReadOnlyList<DataDependency>>(dependencies);
        }

        private string GetMethodBodySource(MethodInfo method)
        {
            // This is a simplified approach - in a real implementation,
            // you might use debugging symbols or decompilation libraries
            // For now, return empty to avoid runtime errors
            return string.Empty;
        }

        private List<Type> FindDbContextTypes(Type declaringType)
        {
            var contextTypes = new List<Type>();

            try
            {
                // Check fields and properties for DbContext types
                var fields = declaringType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                var properties = declaringType.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                foreach (var field in fields)
                {
                    if (IsDbContextType(field.FieldType))
                        contextTypes.Add(field.FieldType);
                }

                foreach (var property in properties)
                {
                    if (IsDbContextType(property.PropertyType))
                        contextTypes.Add(property.PropertyType);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error finding DbContext types: {ex.Message}");
            }

            return contextTypes;
        }

        private bool IsDbContextType(Type type)
        {
            if (type == null) return false;

            // Check if type name contains DbContext
            if (type.Name.Contains("DbContext") || type.Name.Contains("Context"))
                return true;

            // Check base types
            var baseType = type.BaseType;
            while (baseType != null)
            {
                if (EF6ContextBaseTypes.Contains(baseType.Name) || 
                    EF6ContextBaseTypes.Contains(baseType.FullName))
                    return true;
                baseType = baseType.BaseType;
            }

            return false;
        }

        private bool IsEF6ContextType(string typeName)
        {
            return typeName.Contains("DbContext") || 
                   typeName.Contains("Context") ||
                   EF6ContextBaseTypes.Any(baseType => typeName.Contains(baseType));
        }

        private IReadOnlyList<string> GetEntitySetsFromContextType(Type contextType)
        {
            var entitySets = new List<string>();

            try
            {
                var properties = contextType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                
                foreach (var property in properties)
                {
                    // Look for DbSet<T> properties
                    if (property.PropertyType.IsGenericType)
                    {
                        var genericType = property.PropertyType.GetGenericTypeDefinition();
                        if (genericType.Name.Contains("DbSet") || genericType.Name.Contains("IDbSet"))
                        {
                            var entityType = property.PropertyType.GetGenericArguments().FirstOrDefault();
                            if (entityType != null)
                                entitySets.Add(entityType.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting entity sets from {contextType.Name}: {ex.Message}");
            }

            return entitySets;
        }

        private DataAccessType DetermineAccessType(MethodInfo method, Type contextType)
        {
            var methodName = method.Name.ToLowerInvariant();
            
            if (methodName.Contains("create") || methodName.Contains("add") || methodName.Contains("insert"))
                return DataAccessType.Create;
            
            if (methodName.Contains("update") || methodName.Contains("modify"))
                return DataAccessType.Update;
            
            if (methodName.Contains("delete") || methodName.Contains("remove"))
                return DataAccessType.Delete;
            
            if (methodName.Contains("read") || methodName.Contains("get") || methodName.Contains("find"))
                return DataAccessType.Read;

            // Default to ReadWrite for safety
            return DataAccessType.ReadWrite;
        }

        private Type? ExtractEntityTypeFromLine(string line, Type declaringType)
        {
            try
            {
                // Simple pattern matching - could be improved with more sophisticated parsing
                if (line.Contains(".Add<") && line.Contains(">("))
                {
                    var start = line.IndexOf(".Add<") + 5;
                    var end = line.IndexOf(">(", start);
                    if (end > start)
                    {
                        var typeName = line.Substring(start, end - start);
                        // Try to resolve type from declaring type's assembly
                        return declaringType.Assembly.GetType(typeName);
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return null;
        }

        private int EstimateRecordCount(string line)
        {
            // Simple estimation - look for obvious indicators
            if (line.Contains("AddRange"))
            {
                // Could contain multiple records
                return 5; // Conservative estimate
            }
            
            return 1; // Single record for Add operations
        }
    }
}