using System;
using System.Collections.Generic;
using System.IO;

class Program 
{
    static void Main()
    {
        var solutionPath = "/Users/chriscullins/src/TestIntel/TestIntelligence.sln";
        var projects = DiscoverProjects(solutionPath);
        
        Console.WriteLine($"Discovered {projects.Count} projects:");
        foreach (var project in projects)
        {
            Console.WriteLine($"  - {project}");
            Console.WriteLine($"    Exists: {File.Exists(project)}");
        }
    }
    
    static List<string> DiscoverProjects(string solutionPath)
    {
        var projects = new List<string>();
        var solutionDirectory = Path.GetDirectoryName(solutionPath);

        if (string.IsNullOrEmpty(solutionDirectory))
            return projects;

        try
        {
            var solutionContent = File.ReadAllText(solutionPath);
            var lines = solutionContent.Split('\n');

            foreach (var line in lines)
            {
                Console.WriteLine($"Checking line: {line.Trim()}");
                if (line.StartsWith("Project(") && line.Contains(".csproj"))
                {
                    Console.WriteLine($"  -> Found project line: {line.Trim()}");
                    var parts = line.Split(',');
                    Console.WriteLine($"  -> Split into {parts.Length} parts");
                    if (parts.Length > 1)
                    {
                        var relativePath = parts[1].Trim(' ', '"');
                        Console.WriteLine($"  -> Relative path: '{relativePath}'");
                        var fullPath = Path.GetFullPath(Path.Combine(solutionDirectory, relativePath));
                        Console.WriteLine($"  -> Full path: '{fullPath}'");
                        Console.WriteLine($"  -> File exists: {File.Exists(fullPath)}");
                        if (File.Exists(fullPath))
                        {
                            projects.Add(fullPath);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        return projects;
    }
}