using System;
using Microsoft.Extensions.DependencyInjection;

namespace TestIntelligence.Categorizer
{
    /// <summary>
    /// Extension methods for registering categorization services.
    /// </summary>
    public static class TestCategorizerExtensions
    {
        /// <summary>
        /// Registers the default test categorizer implementation.
        /// </summary>
        public static IServiceCollection AddTestCategorizer(this IServiceCollection services)
        {
            return services.AddSingleton<ITestCategorizer, DefaultTestCategorizer>();
        }

        /// <summary>
        /// Registers a custom test categorizer implementation.
        /// </summary>
        public static IServiceCollection AddTestCategorizer<T>(this IServiceCollection services)
            where T : class, ITestCategorizer
        {
            return services.AddSingleton<ITestCategorizer, T>();
        }
    }
}