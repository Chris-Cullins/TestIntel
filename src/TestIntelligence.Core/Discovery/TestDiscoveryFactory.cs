using System;

namespace TestIntelligence.Core.Discovery
{
    /// <summary>
    /// Factory for creating test discovery related objects.
    /// </summary>
    public static class TestDiscoveryFactory
    {
        /// <summary>
        /// Creates a new NUnit test discovery service.
        /// </summary>
        /// <returns>A new NUnit test discovery service instance.</returns>
        public static ITestDiscovery CreateNUnitTestDiscovery()
        {
            return new NUnitTestDiscovery();
        }

    }
}