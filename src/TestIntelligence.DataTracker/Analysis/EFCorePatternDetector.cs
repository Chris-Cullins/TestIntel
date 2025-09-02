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
    /// Detects Entity Framework Core usage patterns in test methods.
    /// </summary>
    public class EFCorePatternDetector : IEFCorePatternDetector
    {
        private static readonly string[] EFCoreNamespaces = 
        {
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.InMemory",
            "Microsoft.EntityFrameworkCore.Sqlite"
        };

        private static readonly string[] EFCoreContextBaseTypes = 
        {
            "DbContext",
            "Microsoft.EntityFrameworkCore.DbContext"
        };

        private static readonly string[] InMemoryProviderIndicators = 
        {
            "UseInMemoryDatabase",
            "UseSqlite",
            ":memory:"
        };

        /// <inheritdoc />
        public IReadOnlyList<DatabaseFramework> SupportedFrameworks => 
            new[] { DatabaseFramework.EntityFrameworkCore };

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
                // Analyze the method using reflection to find EF Core patterns
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
                        $"EFCore:{usage.ContextType.Name}",
                        usage.AccessType,
                        usage.EntitySets);

                    dependencies.Add(dependency);
                }

                // Look for in-memory database usage
                var inMemoryUsages = await DetectInMemoryDatabaseUsageAsync(testMethod.MethodInfo);
                foreach (var usage in inMemoryUsages)
                {
                    var dependency = new DataDependency(
                        testMethod.GetUniqueId(),
                        DataDependencyType.Database,
                        $"EFCore:InMemory:{usage.DatabaseName}",
                        DataAccessType.ReadWrite,
                        new[] { usage.ContextType.Name });

                    dependencies.Add(dependency);
                }

                // Look for specific EF Core patterns in syntax
                dependencies.AddRange(await AnalyzeSyntaxForEFCorePatterns(root, testMethod, cancellationToken));
            }
            catch (Exception ex)
            {
                // Log error but don't throw - return partial results
                System.Diagnostics.Debug.WriteLine($"Error detecting EF Core operations in {testMethod.MethodName}: {ex.Message}");
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
                        "temp", TestIntelligence.Core.Assembly.FrameworkVersion.Net5Plus);

                    var dependencies = await AnalyzeSyntaxForEFCorePatterns(root, tempTestMethod, cancellationToken);
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
        public async Task<bool> RequiresExclusiveDbAccessAsync(TestMethod testMethod, CancellationToken cancellationToken = default)
        {
            if (testMethod == null)
                throw new ArgumentNullException(nameof(testMethod));

            try
            {
                // EF Core in-memory databases are typically isolated per test
                // Check if the test uses a real database connection
                var dependencies = await DetectDatabaseOperationsAsync(testMethod, cancellationToken)
                    .ConfigureAwait(false);
                
                foreach (var dependency in dependencies)
                {
                    // In-memory databases don't require exclusive access
                    if (dependency.ResourceIdentifier.Contains("InMemory"))
                        continue;

                    // Real database connections may require exclusive access
                    if (dependency.AccessType == DataAccessType.Write ||
                        dependency.AccessType == DataAccessType.ReadWrite ||
                        dependency.AccessType == DataAccessType.Delete)
                    {
                        return true;
                    }
                }

                // Check for attributes indicating exclusive access
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

                return false;
            }
            catch
            {
                // Conservative approach - assume no exclusive access for EF Core (due to in-memory defaults)
                return false;
            }
        }

        /// <inheritdoc />
        public bool RequiresExclusiveDbAccess(TestMethod testMethod)
        {
            return RequiresExclusiveDbAccessAsync(testMethod).ConfigureAwait(false).GetAwaiter().GetResult();
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
                    // In-memory databases with same name share state
                    if (depA.ResourceIdentifier.Contains("InMemory") && 
                        depB.ResourceIdentifier.Contains("InMemory"))
                    {
                        if (depA.ResourceIdentifier == depB.ResourceIdentifier)
                            return true;
                    }

                    // Check if they use the same context type
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
        public Task<IReadOnlyList<EFCoreContextUsage>> DetectDbContextUsageAsync(MethodInfo method)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            var usages = new List<EFCoreContextUsage>();

            try
            {
                // Check if method's declaring type or any field/property types derive from DbContext
                var contextTypes = FindDbContextTypes(method.DeclaringType!);

                foreach (var contextType in contextTypes)
                {
                    var entitySets = GetEntitySetsFromContextType(contextType);
                    var accessType = DetermineAccessType(method, contextType);

                    usages.Add(new EFCoreContextUsage(contextType, entitySets, accessType));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting DbContext usage in {method.Name}: {ex.Message}");
            }

            return Task.FromResult<IReadOnlyList<EFCoreContextUsage>>(usages);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<InMemoryDatabaseUsage>> DetectInMemoryDatabaseUsageAsync(MethodInfo method)
        {
            if (method == null)
                throw new ArgumentNullException(nameof(method));

            var usages = new List<InMemoryDatabaseUsage>();

            try
            {
                var methodBody = GetMethodBodySource(method);
                if (string.IsNullOrEmpty(methodBody))
                    return Task.FromResult<IReadOnlyList<InMemoryDatabaseUsage>>(usages);

                // Look for in-memory database patterns
                var lines = methodBody.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    
                    foreach (var indicator in InMemoryProviderIndicators)
                    {
                        if (trimmedLine.Contains(indicator))
                        {
                            var databaseName = ExtractDatabaseName(trimmedLine, indicator);
                            var contextType = method.DeclaringType!; // Simplified assumption
                            
                            usages.Add(new InMemoryDatabaseUsage(databaseName, contextType));
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting in-memory database usage in {method.Name}: {ex.Message}");
            }

            return Task.FromResult<IReadOnlyList<InMemoryDatabaseUsage>>(usages);
        }

        private Task<IReadOnlyList<DataDependency>> AnalyzeSyntaxForEFCorePatterns(
            SyntaxNode root, 
            TestMethod testMethod, 
            CancellationToken cancellationToken)
        {
            var dependencies = new List<DataDependency>();

            // Look for using statements with EF Core namespaces
            var usingDirectives = root.DescendantNodes().OfType<UsingDirectiveSyntax>();
            var hasEFCoreUsing = usingDirectives.Any(u => 
                EFCoreNamespaces.Any(ns => u.Name?.ToString().StartsWith(ns) == true));

            if (!hasEFCoreUsing)
                return Task.FromResult<IReadOnlyList<DataDependency>>(dependencies);

            // Look for variable declarations that might be DbContext
            var variableDeclarations = root.DescendantNodes().OfType<VariableDeclarationSyntax>();
            foreach (var declaration in variableDeclarations)
            {
                var typeName = declaration.Type.ToString();
                if (IsEFCoreContextType(typeName))
                {
                    var dependency = new DataDependency(
                        testMethod.GetUniqueId(),
                        DataDependencyType.Database,
                        $"EFCore:{typeName}",
                        DataAccessType.ReadWrite,
                        new[] { typeName });

                    dependencies.Add(dependency);
                }
            }

            // Look for method invocations that indicate database operations
            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                var methodName = invocation.Expression.ToString();
                
                if (InMemoryProviderIndicators.Any(indicator => methodName.Contains(indicator)))
                {
                    var dependency = new DataDependency(
                        testMethod.GetUniqueId(),
                        DataDependencyType.Database,
                        "EFCore:InMemory",
                        DataAccessType.ReadWrite,
                        new[] { "InMemoryDatabase" });

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
                if (EFCoreContextBaseTypes.Contains(baseType.Name) || 
                    EFCoreContextBaseTypes.Contains(baseType.FullName))
                    return true;
                baseType = baseType.BaseType;
            }

            return false;
        }

        private bool IsEFCoreContextType(string typeName)
        {
            return typeName.Contains("DbContext") || 
                   typeName.Contains("Context") ||
                   EFCoreContextBaseTypes.Any(baseType => typeName.Contains(baseType));
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
                        if (genericType.Name.Contains("DbSet"))
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

        private string ExtractDatabaseName(string line, string indicator)
        {
            try
            {
                if (indicator == "UseInMemoryDatabase" && line.Contains("\""))
                {
                    var startQuote = line.IndexOf('"');
                    var endQuote = line.IndexOf('"', startQuote + 1);
                    if (endQuote > startQuote)
                    {
                        return line.Substring(startQuote + 1, endQuote - startQuote - 1);
                    }
                }
                else if (indicator == ":memory:")
                {
                    return ":memory:";
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return $"Unknown_{Guid.NewGuid():N}";
        }
    }
}