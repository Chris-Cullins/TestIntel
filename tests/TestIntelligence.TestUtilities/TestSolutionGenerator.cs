using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestIntelligence.TestUtilities
{
    public class TestSolutionGenerator : IDisposable
    {
        private readonly string _basePath;
        private readonly List<string> _createdDirectories;
        private readonly Random _random;

        public TestSolutionGenerator(string? basePath = null)
        {
            _basePath = basePath ?? Path.Combine(Path.GetTempPath(), "TestSolutions", Guid.NewGuid().ToString());
            _createdDirectories = new List<string>();
            _random = new Random(42); // Fixed seed for reproducible tests
        }

        public async Task<GeneratedSolution> CreateSolutionAsync(SolutionConfiguration config)
        {
            var solutionDir = Path.Combine(_basePath, config.SolutionName);
            Directory.CreateDirectory(solutionDir);
            _createdDirectories.Add(solutionDir);

            var solution = new GeneratedSolution
            {
                Name = config.SolutionName,
                Path = Path.Combine(solutionDir, $"{config.SolutionName}.sln"),
                Directory = solutionDir,
                Projects = new List<GeneratedProject>()
            };

            var solutionContent = new StringBuilder();
            solutionContent.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
            solutionContent.AppendLine("# Visual Studio Version 17");
            solutionContent.AppendLine("VisualStudioVersion = 17.0.31903.59");
            solutionContent.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");

            // Generate projects
            for (int i = 0; i < config.ProjectCount; i++)
            {
                var projectConfig = new ProjectConfiguration
                {
                    ProjectName = $"{config.ProjectNamePrefix}{i + 1:D3}",
                    Namespace = $"{config.SolutionName}.{config.ProjectNamePrefix}{i + 1:D3}",
                    ProjectType = config.ProjectTemplate.ProjectType,
                    TargetFramework = config.ProjectTemplate.TargetFramework,
                    ClassCount = config.ProjectTemplate.ClassCount,
                    MethodsPerClass = config.ProjectTemplate.MethodsPerClass,
                    ClassNamePrefix = config.ProjectTemplate.ClassNamePrefix,
                    Nullable = config.ProjectTemplate.Nullable,
                    IncludeAsync = config.ProjectTemplate.IncludeAsync,
                    IncludeComplexity = config.ProjectTemplate.IncludeComplexity,
                    IncludeFields = config.ProjectTemplate.IncludeFields,
                    IncludeProperties = config.ProjectTemplate.IncludeProperties,
                    IncludeAssemblyInfo = config.ProjectTemplate.IncludeAssemblyInfo,
                    IncludeEntityFramework = config.ProjectTemplate.IncludeEntityFramework,
                    GenerateInterfaces = config.ProjectTemplate.GenerateInterfaces,
                    PackageReferences = new Dictionary<string, string>(config.ProjectTemplate.PackageReferences)
                };

                var project = await CreateProjectAsync(solutionDir, projectConfig);
                solution.Projects.Add(project);

                // Add to solution file
                var projectGuid = Guid.NewGuid().ToString().ToUpper();
                var projectTypeGuid = GetProjectTypeGuid(projectConfig.ProjectType);
                var relativePath = Path.GetRelativePath(solutionDir, project.Path);

                solutionContent.AppendLine($"Project(\"{projectTypeGuid}\") = \"{project.Name}\", \"{relativePath}\", \"{{{projectGuid}}}\"");
                solutionContent.AppendLine("EndProject");
            }

            // Add solution configuration
            solutionContent.AppendLine("Global");
            solutionContent.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
            solutionContent.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
            solutionContent.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
            solutionContent.AppendLine("\tEndGlobalSection");
            solutionContent.AppendLine("EndGlobal");

            await File.WriteAllTextAsync(solution.Path, solutionContent.ToString());

            return solution;
        }

        private async Task<GeneratedProject> CreateProjectAsync(string solutionDir, ProjectConfiguration config)
        {
            var projectDir = Path.Combine(solutionDir, config.ProjectName);
            Directory.CreateDirectory(projectDir);

            var project = new GeneratedProject
            {
                Name = config.ProjectName,
                Path = Path.Combine(projectDir, $"{config.ProjectName}.csproj"),
                Directory = projectDir,
                Namespace = config.Namespace,
                Classes = new List<GeneratedClass>()
            };

            // Create project file
            await File.WriteAllTextAsync(project.Path, GenerateProjectFileContent(config));

            // Generate classes
            for (int i = 0; i < config.ClassCount; i++)
            {
                var className = $"{config.ClassNamePrefix}{i + 1:D2}";
                var classFile = await CreateClassAsync(projectDir, config.Namespace, className, config);
                project.Classes.Add(classFile);
            }

            // Generate additional files if needed
            if (config.IncludeAssemblyInfo)
            {
                await CreateAssemblyInfoAsync(projectDir, config);
            }

            return project;
        }

        private async Task<GeneratedClass> CreateClassAsync(string projectDir, string namespaceName, string className, ProjectConfiguration config)
        {
            var classPath = Path.Combine(projectDir, $"{className}.cs");
            var generatedClass = new GeneratedClass
            {
                Name = className,
                Path = classPath,
                Namespace = namespaceName,
                Methods = new List<string>()
            };

            var content = new StringBuilder();
            
            // Add usings
            content.AppendLine("using System;");
            content.AppendLine("using System.Collections.Generic;");
            content.AppendLine("using System.Linq;");
            content.AppendLine("using System.Threading.Tasks;");
            
            if (config.IncludeEntityFramework)
            {
                content.AppendLine("using Microsoft.EntityFrameworkCore;");
            }
            
            content.AppendLine();
            
            // Add namespace
            content.AppendLine($"namespace {namespaceName}");
            content.AppendLine("{");
            
            // Add class documentation
            content.AppendLine("    /// <summary>");
            content.AppendLine($"    /// Generated class {className} for testing purposes");
            content.AppendLine("    /// </summary>");
            
            // Add class with appropriate modifiers
            var classModifier = config.GenerateInterfaces && _random.Next(100) < 30 ? "public class" : "public class";
            content.AppendLine($"    {classModifier} {className}");
            content.AppendLine("    {");
            
            // Add fields
            if (config.IncludeFields)
            {
                content.AppendLine($"        private readonly string _id = Guid.NewGuid().ToString();");
                content.AppendLine($"        private int _counter = 0;");
                content.AppendLine();
            }

            // Add constructor
            content.AppendLine($"        public {className}()");
            content.AppendLine("        {");
            content.AppendLine($"            Console.WriteLine(\"Creating {className}\");");
            content.AppendLine("        }");
            content.AppendLine();

            // Generate methods
            for (int i = 0; i < config.MethodsPerClass; i++)
            {
                var methodName = GenerateMethodName(i, config);
                generatedClass.Methods.Add(methodName);
                content.AppendLine(GenerateMethod(methodName, i, config, generatedClass.Methods));
            }

            // Add properties if needed
            if (config.IncludeProperties)
            {
                content.AppendLine("        public string Id => _id;");
                content.AppendLine("        public int Counter => _counter;");
                content.AppendLine($"        public string ClassName => \"{className}\";");
                content.AppendLine();
            }

            content.AppendLine("    }");
            content.AppendLine("}");

            await File.WriteAllTextAsync(classPath, content.ToString());
            return generatedClass;
        }

        private string GenerateMethod(string methodName, int methodIndex, ProjectConfiguration config, List<string> existingMethods)
        {
            var content = new StringBuilder();
            var returnType = GetRandomReturnType(methodIndex);
            var isAsync = config.IncludeAsync && _random.Next(100) < 30;
            
            if (isAsync && returnType == "void")
            {
                returnType = "async Task";
            }
            else if (isAsync)
            {
                returnType = $"async Task<{returnType}>";
            }

            content.AppendLine($"        /// <summary>");
            content.AppendLine($"        /// {methodName} method for testing");
            content.AppendLine($"        /// </summary>");
            content.AppendLine($"        public {returnType} {methodName}()");
            content.AppendLine("        {");

            // Method body with some complexity
            if (config.IncludeComplexity)
            {
                content.AppendLine("            _counter++;");
                content.AppendLine($"            var localVar = _counter * {methodIndex + 1};");
                
                // Add some conditional logic
                content.AppendLine("            if (localVar % 2 == 0)");
                content.AppendLine("            {");
                content.AppendLine($"                Console.WriteLine(\"{methodName}: Even number {{0}}\", localVar);");
                content.AppendLine("            }");
                content.AppendLine("            else");
                content.AppendLine("            {");
                content.AppendLine($"                Console.WriteLine(\"{methodName}: Odd number {{0}}\", localVar);");
                content.AppendLine("            }");

                // Add method calls to create call graph
                if (existingMethods.Any() && methodIndex > 0)
                {
                    var targetMethod = existingMethods[_random.Next(existingMethods.Count)];
                    if (!isAsync || !targetMethod.Contains("Async"))
                    {
                        content.AppendLine($"            {targetMethod}();");
                    }
                }

                if (isAsync)
                {
                    content.AppendLine($"            await Task.Delay({_random.Next(1, 10)});");
                }
            }

            // Return statement
            if (returnType.Contains("int"))
            {
                content.AppendLine("            return localVar;");
            }
            else if (returnType.Contains("string"))
            {
                content.AppendLine($"            return \"{methodName}_\" + localVar.ToString();");
            }
            else if (returnType.Contains("bool"))
            {
                content.AppendLine("            return localVar % 2 == 0;");
            }
            else if (!returnType.Contains("void") && !returnType.Contains("Task"))
            {
                content.AppendLine("            return default;");
            }

            content.AppendLine("        }");
            content.AppendLine();

            return content.ToString();
        }

        private string GenerateMethodName(int index, ProjectConfiguration config)
        {
            var methodTypes = new[] { "Process", "Calculate", "Execute", "Handle", "Validate", "Transform", "Analyze", "Generate" };
            var methodSuffixes = new[] { "Data", "Request", "Response", "Item", "Collection", "Entity", "Model", "Service" };
            
            var type = methodTypes[index % methodTypes.Length];
            var suffix = methodSuffixes[_random.Next(methodSuffixes.Length)];
            var methodName = $"{type}{suffix}{index + 1:D2}";

            if (config.IncludeAsync && _random.Next(100) < 30)
            {
                methodName += "Async";
            }

            return methodName;
        }

        private string GetRandomReturnType(int index)
        {
            var types = new[] { "void", "int", "string", "bool", "double" };
            return types[index % types.Length];
        }

        private string GenerateProjectFileContent(ProjectConfiguration config)
        {
            var content = new StringBuilder();
            content.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
            content.AppendLine();
            content.AppendLine("  <PropertyGroup>");
            content.AppendLine($"    <TargetFramework>{config.TargetFramework}</TargetFramework>");
            
            if (config.Nullable)
            {
                content.AppendLine("    <Nullable>enable</Nullable>");
            }
            
            content.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
            content.AppendLine("  </PropertyGroup>");
            
            if (config.PackageReferences.Any())
            {
                content.AppendLine();
                content.AppendLine("  <ItemGroup>");
                foreach (var package in config.PackageReferences)
                {
                    content.AppendLine($"    <PackageReference Include=\"{package.Key}\" Version=\"{package.Value}\" />");
                }
                content.AppendLine("  </ItemGroup>");
            }

            content.AppendLine();
            content.AppendLine("</Project>");
            
            return content.ToString();
        }

        private async Task CreateAssemblyInfoAsync(string projectDir, ProjectConfiguration config)
        {
            var propertiesDir = Path.Combine(projectDir, "Properties");
            Directory.CreateDirectory(propertiesDir);
            
            var assemblyInfoPath = Path.Combine(propertiesDir, "AssemblyInfo.cs");
            var content = new StringBuilder();
            
            content.AppendLine("using System.Reflection;");
            content.AppendLine("using System.Runtime.InteropServices;");
            content.AppendLine();
            content.AppendLine($"[assembly: AssemblyTitle(\"{config.ProjectName}\")]");
            content.AppendLine($"[assembly: AssemblyDescription(\"Generated test project\")]");
            content.AppendLine("[assembly: AssemblyConfiguration(\"\")]");
            content.AppendLine("[assembly: AssemblyCompany(\"TestIntelligence\")]");
            content.AppendLine($"[assembly: AssemblyProduct(\"{config.ProjectName}\")]");
            content.AppendLine("[assembly: AssemblyCopyright(\"Copyright © TestIntelligence 2025\")]");
            content.AppendLine("[assembly: AssemblyTrademark(\"\")]");
            content.AppendLine("[assembly: ComVisible(false)]");
            content.AppendLine("[assembly: AssemblyVersion(\"1.0.0.0\")]");
            content.AppendLine("[assembly: AssemblyFileVersion(\"1.0.0.0\")]");
            
            await File.WriteAllTextAsync(assemblyInfoPath, content.ToString());
        }

        private string GetProjectTypeGuid(ProjectType projectType)
        {
            return projectType switch
            {
                ProjectType.ClassLibrary => "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}",
                ProjectType.ConsoleApp => "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}",
                ProjectType.WebApi => "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}",
                ProjectType.TestProject => "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}",
                _ => "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"
            };
        }

        public void Dispose()
        {
            foreach (var directory in _createdDirectories.AsEnumerable().Reverse())
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
    }

    public class SolutionConfiguration
    {
        public string SolutionName { get; set; } = "TestSolution";
        public int ProjectCount { get; set; } = 5;
        public string ProjectNamePrefix { get; set; } = "Project";
        public ProjectConfiguration ProjectTemplate { get; set; } = new();
    }

    public class ProjectConfiguration
    {
        public string ProjectName { get; set; } = "TestProject";
        public string Namespace { get; set; } = "TestNamespace";
        public ProjectType ProjectType { get; set; } = ProjectType.ClassLibrary;
        public string TargetFramework { get; set; } = "net8.0";
        public int ClassCount { get; set; } = 5;
        public int MethodsPerClass { get; set; } = 8;
        public string ClassNamePrefix { get; set; } = "TestClass";
        public bool Nullable { get; set; } = true;
        public bool IncludeAsync { get; set; } = true;
        public bool IncludeComplexity { get; set; } = true;
        public bool IncludeFields { get; set; } = true;
        public bool IncludeProperties { get; set; } = true;
        public bool IncludeAssemblyInfo { get; set; } = false;
        public bool IncludeEntityFramework { get; set; } = false;
        public bool GenerateInterfaces { get; set; } = false;
        public Dictionary<string, string> PackageReferences { get; set; } = new();
    }

    public enum ProjectType
    {
        ClassLibrary,
        ConsoleApp,
        WebApi,
        TestProject
    }

    public class GeneratedSolution
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public List<GeneratedProject> Projects { get; set; } = new();
    }

    public class GeneratedProject
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Directory { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public List<GeneratedClass> Classes { get; set; } = new();
    }

    public class GeneratedClass
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public List<string> Methods { get; set; } = new();
    }
}