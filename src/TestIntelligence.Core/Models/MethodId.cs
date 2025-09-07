using System;
using System.Diagnostics.CodeAnalysis;

namespace TestIntelligence.Core.Models;

/// <summary>
/// Value object representing a unique method identifier.
/// </summary>
public readonly record struct MethodId
{
    private readonly string _value;

    /// <summary>
    /// Initializes a new instance of MethodId.
    /// </summary>
    /// <param name="value">The method identifier string.</param>
    /// <exception cref="ArgumentException">Thrown when value is null or whitespace.</exception>
    public MethodId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Method identifier cannot be null or whitespace.", nameof(value));
        
        _value = value;
    }

    /// <summary>
    /// Gets the full method identifier.
    /// </summary>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// Gets the method name without parameters.
    /// </summary>
    public string MethodNameWithoutParameters
    {
        get
        {
            var parenIndex = Value.IndexOf('(');
            return parenIndex > 0 ? Value[..parenIndex] : Value;
        }
    }

    /// <summary>
    /// Gets just the method name (last part after final dot).
    /// </summary>
    public string MethodNameOnly
    {
        get
        {
            var methodWithoutParams = MethodNameWithoutParameters;
            var lastDotIndex = methodWithoutParams.LastIndexOf('.');
            return lastDotIndex >= 0 && lastDotIndex < methodWithoutParams.Length - 1
                ? methodWithoutParams[(lastDotIndex + 1)..]
                : methodWithoutParams;
        }
    }

    /// <summary>
    /// Gets the normalized method identifier (removes global:: prefix).
    /// </summary>
    public string NormalizedValue
    {
        get
        {
            return Value.StartsWith("global::", StringComparison.OrdinalIgnoreCase)
                ? Value[8..] // Remove "global::" prefix
                : Value;
        }
    }

    /// <summary>
    /// Determines if this method ID matches the given pattern.
    /// Supports exact match, class.method match, and method name only match.
    /// </summary>
    public bool Matches(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return false;

        // Exact match
        if (Value.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            return true;

        // Normalized exact match
        if (NormalizedValue.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            return true;

        // Method without parameters match
        if (MethodNameWithoutParameters.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            return true;

        // Method name only match
        if (MethodNameOnly.Equals(pattern, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Creates a MethodId from a type and method name.
    /// </summary>
    public static MethodId Create(Type type, string methodName)
    {
        ArgumentNullExceptionPolyfill.ThrowIfNull(type, nameof(type));
        if (string.IsNullOrWhiteSpace(methodName))
            throw new ArgumentException("Method name cannot be null or whitespace.", nameof(methodName));

        var fullName = type.FullName ?? type.Name;
        return new MethodId($"{fullName}.{methodName}");
    }

    /// <summary>
    /// Creates a MethodId from a full class name and method name.
    /// </summary>
    public static MethodId Create(string className, string methodName)
    {
        if (string.IsNullOrWhiteSpace(className))
            throw new ArgumentException("Class name cannot be null or whitespace.", nameof(className));
        if (string.IsNullOrWhiteSpace(methodName))
            throw new ArgumentException("Method name cannot be null or whitespace.", nameof(methodName));

        return new MethodId($"{className}.{methodName}");
    }

    /// <summary>
    /// Tries to parse a method identifier string.
    /// </summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out MethodId methodId)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            methodId = new MethodId(value);
            return true;
        }

        methodId = default;
        return false;
    }

    public static implicit operator string(MethodId methodId) => methodId.Value;
    public static implicit operator MethodId(string value) => new(value);

    public override string ToString() => Value;
}