namespace TestIntelligence.Core.Models
{
    /// <summary>
    /// Categories for test classification.
    /// </summary>
    public enum TestCategory
    {
        /// <summary>
        /// Fast, isolated unit tests.
        /// </summary>
        Unit,

        /// <summary>
        /// Integration tests that test component interactions.
        /// </summary>
        Integration,

        /// <summary>
        /// Tests that interact with databases.
        /// </summary>
        Database,

        /// <summary>
        /// Tests that make HTTP/API calls.
        /// </summary>
        API,

        /// <summary>
        /// UI automation tests.
        /// </summary>
        UI,

        /// <summary>
        /// End-to-end tests that test complete workflows.
        /// </summary>
        EndToEnd,

        /// <summary>
        /// Performance or load tests.
        /// </summary>
        Performance,

        /// <summary>
        /// Security tests.
        /// </summary>
        Security,

        /// <summary>
        /// Unknown or unclassified tests.
        /// </summary>
        Unknown
    }
}