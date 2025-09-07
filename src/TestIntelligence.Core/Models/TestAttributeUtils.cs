using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TestIntelligence.Core.Models;

/// <summary>
/// Utility class for handling test framework attributes across different testing frameworks.
/// </summary>
public static class TestAttributeUtils
{
    private static readonly HashSet<string> TestMethodAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TestAttribute",
        "FactAttribute", 
        "TestMethodAttribute"
    };

    private static readonly HashSet<string> TestCaseAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TestCaseAttribute",
        "TheoryAttribute",
        "DataTestMethodAttribute"
    };

    private static readonly HashSet<string> SetupAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SetUpAttribute",
        "TestInitializeAttribute"
    };

    private static readonly HashSet<string> TeardownAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TearDownAttribute",
        "TestCleanupAttribute"
    };

    private static readonly HashSet<string> OneTimeSetupAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "OneTimeSetUpAttribute",
        "ClassInitializeAttribute"
    };

    private static readonly HashSet<string> OneTimeTeardownAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "OneTimeTearDownAttribute",
        "ClassCleanupAttribute"
    };

    private static readonly HashSet<string> FixtureAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TestFixtureAttribute",
        "SetUpFixtureAttribute",
        "TestClassAttribute"
    };

    private static readonly HashSet<string> TestRelatedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Test", "SetUp", "TearDown", "Ignore", "Category", "Fact", "Theory", 
        "Initialize", "Cleanup", "Trait", "Skip", "Collection", "Owner", "Priority", "Explicit"
    };

    /// <summary>
    /// Determines if an attribute indicates a test method.
    /// </summary>
    public static bool IsTestAttribute(string attributeName)
        => TestMethodAttributes.Contains(attributeName);

    /// <summary>
    /// Determines if an attribute indicates a test case (parameterized test).
    /// </summary>
    public static bool IsTestCaseAttribute(string attributeName)
        => TestCaseAttributes.Contains(attributeName);

    /// <summary>
    /// Determines if an attribute indicates a setup method.
    /// </summary>
    public static bool IsSetupAttribute(string attributeName)
        => SetupAttributes.Contains(attributeName);

    /// <summary>
    /// Determines if an attribute indicates a teardown method.
    /// </summary>
    public static bool IsTeardownAttribute(string attributeName)
        => TeardownAttributes.Contains(attributeName);

    /// <summary>
    /// Determines if an attribute indicates a one-time setup method.
    /// </summary>
    public static bool IsOneTimeSetupAttribute(string attributeName)
        => OneTimeSetupAttributes.Contains(attributeName);

    /// <summary>
    /// Determines if an attribute indicates a one-time teardown method.
    /// </summary>
    public static bool IsOneTimeTeardownAttribute(string attributeName)
        => OneTimeTeardownAttributes.Contains(attributeName);

    /// <summary>
    /// Determines if an attribute indicates a test fixture.
    /// </summary>
    public static bool IsFixtureAttribute(string attributeName)
        => FixtureAttributes.Contains(attributeName);

    /// <summary>
    /// Determines if an attribute is test-related and should be included in test analysis.
    /// </summary>
    public static bool IsTestRelatedAttribute(string attributeName)
        => TestRelatedAttributes.Any(keyword => attributeName.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the category names from Category attributes.
    /// </summary>
    public static IEnumerable<string> GetCategoryNames(IEnumerable<Attribute> attributes)
    {
        return attributes
            .Where(attr => attr.GetType().Name.Equals("CategoryAttribute", StringComparison.OrdinalIgnoreCase))
            .Select(attr => attr.GetType().GetProperty("Name")?.GetValue(attr) as string)
            .Where(name => !string.IsNullOrEmpty(name))!;
    }

    /// <summary>
    /// Gets test case arguments from TestCase attributes.
    /// </summary>
    public static IEnumerable<object[]> GetTestCaseArguments(IEnumerable<Attribute> attributes)
    {
        return attributes
            .Where(attr => IsTestCaseAttribute(attr.GetType().Name))
            .Select(attr => attr.GetType().GetProperty("Arguments")?.GetValue(attr) as object[])
            .Where(args => args != null)!;
    }

    /// <summary>
    /// Filters attributes to include only test-related ones.
    /// </summary>
    public static IEnumerable<Attribute> FilterTestRelatedAttributes(IEnumerable<object> attributes)
    {
        return attributes
            .Cast<Attribute>()
            .Where(attr => IsTestRelatedAttribute(attr.GetType().Name));
    }

    /// <summary>
    /// Gets a display-friendly attribute name by removing the "Attribute" suffix.
    /// </summary>
    public static string GetDisplayName(string attributeName)
    {
        return attributeName.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase)
            ? attributeName[..^9] // Remove "Attribute" suffix
            : attributeName;
    }
}