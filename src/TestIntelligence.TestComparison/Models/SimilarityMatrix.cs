using System;
using System.Collections.Generic;
using System.Linq;

namespace TestIntelligence.TestComparison.Models;

/// <summary>
/// Represents a symmetric similarity matrix for clustering analysis.
/// Stores pairwise similarity scores between test methods.
/// </summary>
public class SimilarityMatrix
{
    private readonly double[,] _matrix;
    private readonly IReadOnlyList<string> _testIds;
    private readonly Dictionary<string, int> _testIdToIndex;

    /// <summary>
    /// Initializes a new similarity matrix for the given test identifiers.
    /// </summary>
    /// <param name="testIds">Collection of test method identifiers</param>
    public SimilarityMatrix(IReadOnlyList<string> testIds)
    {
        if (testIds == null) throw new ArgumentNullException(nameof(testIds));
        if (testIds.Count == 0) throw new ArgumentException("Test IDs collection cannot be empty", nameof(testIds));

        _testIds = testIds;
        _matrix = new double[testIds.Count, testIds.Count];
        _testIdToIndex = new Dictionary<string, int>();

        // Build index mapping
        for (int i = 0; i < testIds.Count; i++)
        {
            _testIdToIndex[testIds[i]] = i;
            _matrix[i, i] = 1.0; // Self-similarity is always 1.0
        }
    }

    /// <summary>
    /// Gets the number of tests in the matrix.
    /// </summary>
    public int Size => _testIds.Count;

    /// <summary>
    /// Gets the test identifiers for this matrix.
    /// </summary>
    public IReadOnlyList<string> TestIds => _testIds;

    /// <summary>
    /// Gets the similarity score between two tests by their indices.
    /// </summary>
    /// <param name="index1">Index of the first test</param>
    /// <param name="index2">Index of the second test</param>
    /// <returns>Similarity score between 0.0 and 1.0</returns>
    public double GetSimilarity(int index1, int index2)
    {
        ValidateIndices(index1, index2);
        return _matrix[Math.Min(index1, index2), Math.Max(index1, index2)];
    }

    /// <summary>
    /// Gets the similarity score between two tests by their identifiers.
    /// </summary>
    /// <param name="testId1">Identifier of the first test</param>
    /// <param name="testId2">Identifier of the second test</param>
    /// <returns>Similarity score between 0.0 and 1.0</returns>
    public double GetSimilarity(string testId1, string testId2)
    {
        var index1 = GetTestIndex(testId1);
        var index2 = GetTestIndex(testId2);
        return GetSimilarity(index1, index2);
    }

    /// <summary>
    /// Sets the similarity score between two tests by their indices.
    /// </summary>
    /// <param name="index1">Index of the first test</param>
    /// <param name="index2">Index of the second test</param>
    /// <param name="similarity">Similarity score between 0.0 and 1.0</param>
    public void SetSimilarity(int index1, int index2, double similarity)
    {
        ValidateIndices(index1, index2);
        ValidateSimilarityValue(similarity);

        // Store in upper triangle to maintain symmetry
        var minIndex = Math.Min(index1, index2);
        var maxIndex = Math.Max(index1, index2);
        _matrix[minIndex, maxIndex] = similarity;
        _matrix[maxIndex, minIndex] = similarity;
    }

    /// <summary>
    /// Sets the similarity score between two tests by their identifiers.
    /// </summary>
    /// <param name="testId1">Identifier of the first test</param>
    /// <param name="testId2">Identifier of the second test</param>
    /// <param name="similarity">Similarity score between 0.0 and 1.0</param>
    public void SetSimilarity(string testId1, string testId2, double similarity)
    {
        var index1 = GetTestIndex(testId1);
        var index2 = GetTestIndex(testId2);
        SetSimilarity(index1, index2, similarity);
    }

    /// <summary>
    /// Gets all pairwise similarities that meet or exceed the specified threshold.
    /// </summary>
    /// <param name="threshold">Minimum similarity threshold</param>
    /// <returns>Collection of high-similarity test pairs</returns>
    public IEnumerable<(int Index1, int Index2, double Similarity)> GetHighSimilarityPairs(double threshold)
    {
        ValidateSimilarityValue(threshold);

        for (int i = 0; i < _testIds.Count; i++)
        {
            for (int j = i + 1; j < _testIds.Count; j++)
            {
                var similarity = _matrix[i, j];
                if (similarity >= threshold)
                {
                    yield return (i, j, similarity);
                }
            }
        }
    }

    /// <summary>
    /// Gets all similarities for a specific test by its index.
    /// </summary>
    /// <param name="testIndex">Index of the test</param>
    /// <returns>Array of similarity scores to all other tests</returns>
    public double[] GetSimilaritiesForTest(int testIndex)
    {
        ValidateIndex(testIndex);

        var similarities = new double[_testIds.Count];
        for (int i = 0; i < _testIds.Count; i++)
        {
            similarities[i] = _matrix[Math.Min(testIndex, i), Math.Max(testIndex, i)];
        }
        return similarities;
    }

    /// <summary>
    /// Gets all similarities for a specific test by its identifier.
    /// </summary>
    /// <param name="testId">Identifier of the test</param>
    /// <returns>Array of similarity scores to all other tests</returns>
    public double[] GetSimilaritiesForTest(string testId)
    {
        var index = GetTestIndex(testId);
        return GetSimilaritiesForTest(index);
    }

    /// <summary>
    /// Finds the most similar test to the specified test.
    /// </summary>
    /// <param name="testIndex">Index of the test</param>
    /// <returns>Index and similarity score of the most similar test</returns>
    public (int MostSimilarIndex, double Similarity) FindMostSimilar(int testIndex)
    {
        ValidateIndex(testIndex);

        var maxSimilarity = -1.0;
        var mostSimilarIndex = -1;

        for (int i = 0; i < _testIds.Count; i++)
        {
            if (i == testIndex) continue; // Skip self

            var similarity = _matrix[Math.Min(testIndex, i), Math.Max(testIndex, i)];
            if (similarity > maxSimilarity)
            {
                maxSimilarity = similarity;
                mostSimilarIndex = i;
            }
        }

        return (mostSimilarIndex, maxSimilarity);
    }

    /// <summary>
    /// Calculates the average similarity for a specific test.
    /// </summary>
    /// <param name="testIndex">Index of the test</param>
    /// <returns>Average similarity to all other tests</returns>
    public double CalculateAverageSimilarity(int testIndex)
    {
        ValidateIndex(testIndex);

        var sum = 0.0;
        var count = 0;

        for (int i = 0; i < _testIds.Count; i++)
        {
            if (i == testIndex) continue; // Skip self

            sum += _matrix[Math.Min(testIndex, i), Math.Max(testIndex, i)];
            count++;
        }

        return count > 0 ? sum / count : 0.0;
    }

    /// <summary>
    /// Gets the overall statistics for the similarity matrix.
    /// </summary>
    /// <returns>Statistical summary of all pairwise similarities</returns>
    public SimilarityMatrixStatistics GetStatistics()
    {
        var similarities = new List<double>();

        for (int i = 0; i < _testIds.Count; i++)
        {
            for (int j = i + 1; j < _testIds.Count; j++)
            {
                similarities.Add(_matrix[i, j]);
            }
        }

        if (!similarities.Any())
        {
            return new SimilarityMatrixStatistics
            {
                Count = 0,
                Mean = 0.0,
                StandardDeviation = 0.0,
                Minimum = 0.0,
                Maximum = 0.0,
                Median = 0.0
            };
        }

        similarities.Sort();

        return new SimilarityMatrixStatistics
        {
            Count = similarities.Count,
            Mean = similarities.Average(),
            StandardDeviation = CalculateStandardDeviation(similarities),
            Minimum = similarities.First(),
            Maximum = similarities.Last(),
            Median = CalculateMedian(similarities)
        };
    }

    /// <summary>
    /// Creates a copy of the similarity matrix with only values above the threshold.
    /// </summary>
    /// <param name="threshold">Minimum similarity threshold</param>
    /// <returns>New similarity matrix with filtered values</returns>
    public SimilarityMatrix FilterByThreshold(double threshold)
    {
        ValidateSimilarityValue(threshold);

        var filtered = new SimilarityMatrix(_testIds);

        for (int i = 0; i < _testIds.Count; i++)
        {
            for (int j = i + 1; j < _testIds.Count; j++)
            {
                var similarity = _matrix[i, j];
                if (similarity >= threshold)
                {
                    filtered.SetSimilarity(i, j, similarity);
                }
            }
        }

        return filtered;
    }

    #region Private Helper Methods

    private int GetTestIndex(string testId)
    {
        if (!_testIdToIndex.TryGetValue(testId, out var index))
        {
            throw new ArgumentException($"Test ID '{testId}' not found in matrix", nameof(testId));
        }
        return index;
    }

    private void ValidateIndex(int index)
    {
        if (index < 0 || index >= _testIds.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), 
                $"Index {index} is out of range [0, {_testIds.Count - 1}]");
        }
    }

    private void ValidateIndices(int index1, int index2)
    {
        ValidateIndex(index1);
        ValidateIndex(index2);
    }

    private static void ValidateSimilarityValue(double similarity)
    {
        if (similarity < 0.0 || similarity > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(similarity), 
                $"Similarity value {similarity} must be between 0.0 and 1.0");
        }
    }

    private static double CalculateStandardDeviation(List<double> values)
    {
        var mean = values.Average();
        var sumOfSquaredDifferences = values.Sum(x => Math.Pow(x - mean, 2));
        return Math.Sqrt(sumOfSquaredDifferences / values.Count);
    }

    private static double CalculateMedian(List<double> sortedValues)
    {
        var count = sortedValues.Count;
        if (count % 2 == 0)
        {
            return (sortedValues[count / 2 - 1] + sortedValues[count / 2]) / 2.0;
        }
        else
        {
            return sortedValues[count / 2];
        }
    }

    #endregion
}

/// <summary>
/// Statistical summary of a similarity matrix.
/// </summary>
public class SimilarityMatrixStatistics
{
    /// <summary>
    /// Number of pairwise similarity values.
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// Mean similarity across all pairs.
    /// </summary>
    public double Mean { get; init; }

    /// <summary>
    /// Standard deviation of similarity values.
    /// </summary>
    public double StandardDeviation { get; init; }

    /// <summary>
    /// Minimum similarity value.
    /// </summary>
    public double Minimum { get; init; }

    /// <summary>
    /// Maximum similarity value.
    /// </summary>
    public double Maximum { get; init; }

    /// <summary>
    /// Median similarity value.
    /// </summary>
    public double Median { get; init; }
}