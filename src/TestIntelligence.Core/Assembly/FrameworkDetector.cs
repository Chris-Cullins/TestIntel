using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace TestIntelligence.Core.Assembly
{
    /// <summary>
    /// Utility class for detecting the framework version of .NET assemblies.
    /// </summary>
    public static class FrameworkDetector
    {
        private static readonly Dictionary<string, FrameworkVersion> FrameworkMonikers = new(StringComparer.OrdinalIgnoreCase)
        {
            { ".NETFramework", FrameworkVersion.NetFramework48 },
            { ".NETCoreApp", FrameworkVersion.NetCore },
            { ".NETStandard", FrameworkVersion.NetStandard },
            { "net5.0", FrameworkVersion.Net5Plus },
            { "net6.0", FrameworkVersion.Net5Plus },
            { "net7.0", FrameworkVersion.Net5Plus },
            { "net8.0", FrameworkVersion.Net5Plus },
            { "net9.0", FrameworkVersion.Net5Plus },
        };

        /// <summary>
        /// Detects the framework version of the specified assembly.
        /// </summary>
        /// <param name="assemblyPath">Path to the assembly file.</param>
        /// <returns>The detected framework version.</returns>
        /// <exception cref="ArgumentNullException">Thrown when assemblyPath is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the assembly file does not exist.</exception>
        public static FrameworkVersion DetectFrameworkVersion(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new ArgumentNullException(nameof(assemblyPath));

            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException($"Assembly file not found: {assemblyPath}");

            try
            {
                // Try to detect using metadata reader first (most reliable)
                var frameworkFromMetadata = DetectFromMetadata(assemblyPath);
                if (frameworkFromMetadata != FrameworkVersion.Unknown)
                    return frameworkFromMetadata;

                // Fallback to reflection-based detection
                return DetectFromReflection(assemblyPath);
            }
            catch (Exception)
            {
                // If all else fails, try to infer from file location or name
                return DetectFromPath(assemblyPath);
            }
        }

        /// <summary>
        /// Detects framework version using metadata reader (preferred method).
        /// </summary>
        private static FrameworkVersion DetectFromMetadata(string assemblyPath)
        {
            try
            {
                using var fileStream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var peReader = new PEReader(fileStream);
                
                if (!peReader.HasMetadata)
                    return FrameworkVersion.Unknown;

                var metadataReader = peReader.GetMetadataReader();

                // Check for TargetFrameworkAttribute
                var targetFramework = GetTargetFrameworkFromMetadata(metadataReader);
                if (!string.IsNullOrEmpty(targetFramework))
                {
                    return ParseTargetFramework(targetFramework!);
                }

                // Check referenced assemblies for framework clues
                return DetectFromReferencedAssemblies(metadataReader);
            }
            catch
            {
                return FrameworkVersion.Unknown;
            }
        }

        /// <summary>
        /// Gets the target framework from assembly metadata.
        /// </summary>
        private static string? GetTargetFrameworkFromMetadata(MetadataReader metadataReader)
        {
            try
            {
                var assembly = metadataReader.GetAssemblyDefinition();
                
                foreach (var customAttributeHandle in assembly.GetCustomAttributes())
                {
                    var customAttribute = metadataReader.GetCustomAttribute(customAttributeHandle);
                    
                    if (customAttribute.Constructor.Kind == HandleKind.MemberReference)
                    {
                        var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)customAttribute.Constructor);
                        var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                        var typeName = metadataReader.GetString(typeRef.Name);
                        var namespaceName = metadataReader.GetString(typeRef.Namespace);
                        
                        if (namespaceName == "System.Runtime.Versioning" && typeName == "TargetFrameworkAttribute")
                        {
                            var value = customAttribute.DecodeValue(new CustomAttributeTypeProvider());
                            if (value.FixedArguments.Length > 0)
                            {
                                return value.FixedArguments[0].Value?.ToString();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors in metadata parsing
            }

            return null;
        }

        /// <summary>
        /// Detects framework version from referenced assemblies in metadata.
        /// </summary>
        private static FrameworkVersion DetectFromReferencedAssemblies(MetadataReader metadataReader)
        {
            try
            {
                foreach (var assemblyRefHandle in metadataReader.AssemblyReferences)
                {
                    var assemblyRef = metadataReader.GetAssemblyReference(assemblyRefHandle);
                    var name = metadataReader.GetString(assemblyRef.Name);
                    
                    switch (name)
                    {
                        case "mscorlib":
                            return FrameworkVersion.NetFramework48;
                        case "System.Runtime":
                        case "netstandard":
                            return FrameworkVersion.NetStandard;
                        case "System.Private.CoreLib":
                            var version = assemblyRef.Version;
                            return version.Major >= 5 ? FrameworkVersion.Net5Plus : FrameworkVersion.NetCore;
                    }
                }
            }
            catch
            {
                // Ignore errors in metadata parsing
            }

            return FrameworkVersion.Unknown;
        }

        /// <summary>
        /// Detects framework version using reflection as fallback.
        /// </summary>
        private static FrameworkVersion DetectFromReflection(string assemblyPath)
        {
            try
            {
                // Load assembly for inspection only - don't execute code
                var assembly = System.Reflection.Assembly.ReflectionOnlyLoadFrom(assemblyPath);
                
                // Check TargetFrameworkAttribute
                var targetFrameworkAttr = assembly.GetCustomAttributesData()
                    .FirstOrDefault(attr => attr.AttributeType.Name == "TargetFrameworkAttribute");
                
                if (targetFrameworkAttr?.ConstructorArguments.Count > 0)
                {
                    var targetFramework = targetFrameworkAttr.ConstructorArguments[0].Value?.ToString();
                    if (!string.IsNullOrEmpty(targetFramework))
                    {
                        return ParseTargetFramework(targetFramework!);
                    }
                }

                // Check referenced assemblies
                var referencedAssemblies = assembly.GetReferencedAssemblies();
                foreach (var refAssembly in referencedAssemblies)
                {
                    switch (refAssembly.Name)
                    {
                        case "mscorlib":
                            return FrameworkVersion.NetFramework48;
                        case "System.Runtime":
                        case "netstandard":
                            return FrameworkVersion.NetStandard;
                        case "System.Private.CoreLib":
                            return refAssembly.Version?.Major >= 5 ? FrameworkVersion.Net5Plus : FrameworkVersion.NetCore;
                    }
                }
            }
            catch
            {
                // ReflectionOnlyLoadFrom might fail, continue to path-based detection
            }

            return FrameworkVersion.Unknown;
        }

        /// <summary>
        /// Detects framework version from file path patterns.
        /// </summary>
        private static FrameworkVersion DetectFromPath(string assemblyPath)
        {
            var path = assemblyPath.ToLowerInvariant();
            
            if (path.Contains("net48") || path.Contains("net472") || path.Contains("net471") || path.Contains("net47"))
                return FrameworkVersion.NetFramework48;
            
            if (path.Contains("netcoreapp") || path.Contains("netcore"))
                return FrameworkVersion.NetCore;
            
            if (path.Contains("net5") || path.Contains("net6") || path.Contains("net7") || path.Contains("net8") || path.Contains("net9"))
                return FrameworkVersion.Net5Plus;
            
            if (path.Contains("netstandard"))
                return FrameworkVersion.NetStandard;

            // Default fallback
            return FrameworkVersion.NetStandard;
        }

        /// <summary>
        /// Parses a target framework string to determine the framework version.
        /// </summary>
        private static FrameworkVersion ParseTargetFramework(string targetFramework)
        {
            if (string.IsNullOrWhiteSpace(targetFramework))
                return FrameworkVersion.Unknown;

            // Handle common patterns
            foreach (var kvp in FrameworkMonikers)
            {
                if (targetFramework.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    // Special handling for .NET version numbers
                    if (kvp.Key == ".NETCoreApp" && targetFramework.Contains("5.") || targetFramework.Contains("6.") || 
                        targetFramework.Contains("7.") || targetFramework.Contains("8.") || targetFramework.Contains("9."))
                    {
                        return FrameworkVersion.Net5Plus;
                    }
                    return kvp.Value;
                }
            }

            // Handle specific version patterns
            if (targetFramework.StartsWith("net5") || targetFramework.StartsWith("net6") || 
                targetFramework.StartsWith("net7") || targetFramework.StartsWith("net8") || 
                targetFramework.StartsWith("net9"))
            {
                return FrameworkVersion.Net5Plus;
            }

            return FrameworkVersion.Unknown;
        }

        /// <summary>
        /// Custom attribute type provider for metadata decoding.
        /// </summary>
        private class CustomAttributeTypeProvider : ICustomAttributeTypeProvider<object>
        {
            public object GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode;
            public object GetSystemType() => typeof(Type);
            public object GetSZArrayType(object elementType) => elementType;
            public object GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => handle;
            public object GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => handle;
            public object GetTypeFromSerializedName(string name) => name;
            public PrimitiveTypeCode GetUnderlyingEnumType(object type) => PrimitiveTypeCode.Int32;
            public bool IsSystemType(object type) => type is Type;
        }
    }
}