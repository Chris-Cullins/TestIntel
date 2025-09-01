using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TestIntelligence.Core.Caching
{
    /// <summary>
    /// Utilities for compressing and decompressing cache data.
    /// </summary>
    public static class CacheCompressionUtilities
    {
        private static readonly JsonSerializerOptions DefaultJsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Compresses an object to JSON and then GZip format.
        /// </summary>
        /// <typeparam name="T">Type of object to compress.</typeparam>
        /// <param name="obj">Object to compress.</param>
        /// <param name="compressionLevel">Compression level to use.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Compressed data and compression statistics.</returns>
        public static async Task<CompressedData> CompressAsync<T>(
            T obj, 
            CompressionLevel compressionLevel = CompressionLevel.Optimal,
            CancellationToken cancellationToken = default)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(obj, DefaultJsonOptions);
            var uncompressedSize = jsonBytes.Length;

            using var outputStream = new MemoryStream();
            using (var gzipStream = new GZipStream(outputStream, compressionLevel))
            {
                await gzipStream.WriteAsync(jsonBytes, 0, jsonBytes.Length, cancellationToken);
            }

            var compressedBytes = outputStream.ToArray();
            var compressionRatio = 1.0 - (double)compressedBytes.Length / uncompressedSize;

            return new CompressedData
            {
                Data = compressedBytes,
                UncompressedSize = uncompressedSize,
                CompressedSize = compressedBytes.Length,
                CompressionRatio = compressionRatio,
                CompressedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Decompresses GZip data and deserializes from JSON.
        /// </summary>
        /// <typeparam name="T">Type of object to deserialize.</typeparam>
        /// <param name="compressedData">Compressed data to decompress.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Decompressed and deserialized object.</returns>
        public static async Task<T?> DecompressAsync<T>(
            CompressedData compressedData,
            CancellationToken cancellationToken = default) where T : class
        {
            if (compressedData?.Data == null)
                return null;

            using var inputStream = new MemoryStream(compressedData.Data);
            using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();

            await gzipStream.CopyToAsync(outputStream);
            var decompressedBytes = outputStream.ToArray();

            return JsonSerializer.Deserialize<T>(decompressedBytes, DefaultJsonOptions);
        }

        /// <summary>
        /// Compresses data directly to a stream.
        /// </summary>
        /// <typeparam name="T">Type of object to compress.</typeparam>
        /// <param name="obj">Object to compress.</param>
        /// <param name="outputStream">Stream to write compressed data to.</param>
        /// <param name="compressionLevel">Compression level to use.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Compression statistics.</returns>
        public static async Task<CompressionStats> CompressToStreamAsync<T>(
            T obj,
            Stream outputStream,
            CompressionLevel compressionLevel = CompressionLevel.Optimal,
            CancellationToken cancellationToken = default)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            if (outputStream == null)
                throw new ArgumentNullException(nameof(outputStream));

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(obj, DefaultJsonOptions);
            var uncompressedSize = jsonBytes.Length;

            var startPosition = outputStream.Position;
            using (var gzipStream = new GZipStream(outputStream, compressionLevel, leaveOpen: true))
            {
                await gzipStream.WriteAsync(jsonBytes, 0, jsonBytes.Length, cancellationToken);
            }

            var compressedSize = outputStream.Position - startPosition;
            var compressionRatio = 1.0 - (double)compressedSize / uncompressedSize;

            return new CompressionStats
            {
                UncompressedSize = uncompressedSize,
                CompressedSize = compressedSize,
                CompressionRatio = compressionRatio
            };
        }

        /// <summary>
        /// Decompresses data directly from a stream.
        /// </summary>
        /// <typeparam name="T">Type of object to deserialize.</typeparam>
        /// <param name="inputStream">Stream containing compressed data.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Decompressed and deserialized object.</returns>
        public static async Task<T?> DecompressFromStreamAsync<T>(
            Stream inputStream,
            CancellationToken cancellationToken = default) where T : class
        {
            if (inputStream == null)
                throw new ArgumentNullException(nameof(inputStream));

            using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream();

            await gzipStream.CopyToAsync(outputStream);
            var decompressedBytes = outputStream.ToArray();

            return JsonSerializer.Deserialize<T>(decompressedBytes, DefaultJsonOptions);
        }

        /// <summary>
        /// Estimates the compression ratio for a given object without actually compressing it.
        /// Uses sampling for large objects to provide fast estimates.
        /// </summary>
        /// <typeparam name="T">Type of object to estimate.</typeparam>
        /// <param name="obj">Object to estimate compression for.</param>
        /// <returns>Estimated compression ratio (0-1, where 0.7 means 70% compression).</returns>
        public static double EstimateCompressionRatio<T>(T obj)
        {
            if (obj == null)
                return 0;

            try
            {
                var json = JsonSerializer.Serialize(obj, DefaultJsonOptions);
                
                // For small objects, compress a sample
                if (json.Length < 1024)
                {
                    return EstimateFromSample(json);
                }

                // For larger objects, sample different parts
                var sampleSize = Math.Min(1024, json.Length / 10);
                var samples = new[]
                {
                    json.Substring(0, sampleSize),
                    json.Substring(json.Length / 2, sampleSize),
                    json.Substring(json.Length - sampleSize, sampleSize)
                };

                var totalRatio = 0.0;
                foreach (var sample in samples)
                {
                    totalRatio += EstimateFromSample(sample);
                }

                return totalRatio / samples.Length;
            }
            catch
            {
                // Default estimate for JSON data
                return 0.7;
            }
        }

        private static double EstimateFromSample(string sample)
        {
            var originalBytes = Encoding.UTF8.GetBytes(sample);
            using var outputStream = new MemoryStream();
            using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal))
            {
                gzipStream.Write(originalBytes, 0, originalBytes.Length);
            }

            var compressedSize = outputStream.ToArray().Length;
            return 1.0 - (double)compressedSize / originalBytes.Length;
        }
    }

    /// <summary>
    /// Represents compressed data with metadata.
    /// </summary>
    public class CompressedData
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public int UncompressedSize { get; set; }
        public int CompressedSize { get; set; }
        public double CompressionRatio { get; set; }
        public DateTime CompressedAt { get; set; }
    }

    /// <summary>
    /// Statistics about compression operation.
    /// </summary>
    public class CompressionStats
    {
        public long UncompressedSize { get; set; }
        public long CompressedSize { get; set; }
        public double CompressionRatio { get; set; }
    }
}