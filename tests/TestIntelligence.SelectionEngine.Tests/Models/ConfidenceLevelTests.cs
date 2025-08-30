using System;
using FluentAssertions;
using TestIntelligence.SelectionEngine.Models;
using Xunit;

namespace TestIntelligence.SelectionEngine.Tests.Models
{
    public class ConfidenceLevelTests
    {
        [Theory]
        [InlineData(ConfidenceLevel.Fast, 0.7)]
        [InlineData(ConfidenceLevel.Medium, 0.85)]
        [InlineData(ConfidenceLevel.High, 0.95)]
        [InlineData(ConfidenceLevel.Full, 0.99)]
        public void GetConfidenceScore_ShouldReturnCorrectScore(ConfidenceLevel level, double expected)
        {
            var score = level.GetConfidenceScore();
            score.Should().Be(expected);
        }

        [Theory]
        [InlineData(ConfidenceLevel.Fast, 30)] // 30 seconds
        [InlineData(ConfidenceLevel.Medium, 300)] // 5 minutes
        [InlineData(ConfidenceLevel.High, 900)] // 15 minutes
        [InlineData(ConfidenceLevel.Full, 3600)] // 1 hour
        public void GetEstimatedDuration_ShouldReturnCorrectTimeSpan(ConfidenceLevel level, int expectedSeconds)
        {
            var duration = level.GetEstimatedDuration();
            duration.TotalSeconds.Should().Be(expectedSeconds);
        }

        [Theory]
        [InlineData(ConfidenceLevel.Fast, 50)]
        [InlineData(ConfidenceLevel.Medium, 200)]
        [InlineData(ConfidenceLevel.High, 1000)]
        [InlineData(ConfidenceLevel.Full, int.MaxValue)]
        public void GetMaxTestCount_ShouldReturnCorrectLimit(ConfidenceLevel level, int expected)
        {
            var maxCount = level.GetMaxTestCount();
            maxCount.Should().Be(expected);
        }

        [Fact]
        public void GetConfidenceScore_WithInvalidLevel_ShouldThrowArgumentOutOfRangeException()
        {
            var invalidLevel = (ConfidenceLevel)999;
            
            Action act = () => invalidLevel.GetConfidenceScore();
            
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void GetEstimatedDuration_WithInvalidLevel_ShouldThrowArgumentOutOfRangeException()
        {
            var invalidLevel = (ConfidenceLevel)999;
            
            Action act = () => invalidLevel.GetEstimatedDuration();
            
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void GetMaxTestCount_WithInvalidLevel_ShouldThrowArgumentOutOfRangeException()
        {
            var invalidLevel = (ConfidenceLevel)999;
            
            Action act = () => invalidLevel.GetMaxTestCount();
            
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}