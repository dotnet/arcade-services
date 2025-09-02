using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Moq;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.Tests;

public class NoOpRedisClientTests
{
    /// <summary>
    /// Verifies that TrySetAsync(string key, T value, TimeSpan? expiration) always returns false for NoOpRedisClient
    /// and does not throw, across representative edge-case keys and expiration values.
    /// Inputs:
    ///  - key: "", " ", "normal", long string (4096 'a's), and special characters including control chars.
    ///  - expiration: null, TimeSpan.Zero, negative ticks, a small positive span, and TimeSpan.MaxValue.
    /// Expected:
    ///  - The method completes without exception and returns false for all inputs.
    /// </summary>
    [Test]
    [TestCase("", null)]
    [TestCase(" ", 0L)]
    [TestCase("normal", 10000000L)] // 1 second
    [TestCase("special\n\t\r\0", -1L)]
    [TestCaseSource(nameof(StringKeyAndExpirationCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TrySetAsync_StringValue_EdgeCaseKeysAndExpirations_ReturnsFalse(string key, long? expirationTicks)
    {
        // Arrange
        var client = new NoOpRedisClient();
        var value = "some-non-empty-value";
        TimeSpan? expiration = expirationTicks.HasValue ? new TimeSpan(expirationTicks.Value) : (TimeSpan?)null;

        // Act
        var result = await client.TrySetAsync<string>(key, value, expiration);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that TrySetAsync works with a custom reference type for T and returns false consistently.
    /// Inputs:
    ///  - key: "obj-key"
    ///  - value: instance of a custom reference type with simple properties.
    ///  - expiration: null and TimeSpan.FromMinutes(5)
    /// Expected:
    ///  - The method completes without exception and returns false for both inputs.
    /// </summary>
    [Test]
    [TestCase(null)]
    [TestCase(3000000000L)] // 5 minutes
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TrySetAsync_CustomReferenceType_ReturnsFalse(long? expirationTicks)
    {
        // Arrange
        var client = new NoOpRedisClient();
        var key = "obj-key";
        var value = new SampleDto { Id = 42, Name = "answer" };
        TimeSpan? expiration = expirationTicks.HasValue ? new TimeSpan(expirationTicks.Value) : (TimeSpan?)null;

        // Act
        var result = await client.TrySetAsync<SampleDto>(key, value, expiration);

        // Assert
        result.Should().BeFalse();
    }

    // Helper case source to avoid an overly large set of [TestCase] attributes.
    private static System.Collections.Generic.IEnumerable<TestCaseData> StringKeyAndExpirationCases()
    {
        yield return new TestCaseData(new string('a', 4096), (long?)TimeSpan.MaxValue.Ticks);
    }

    // Helper class to exercise generic type parameter T : class
    private class SampleDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Verifies that IRedisCacheClient.TryGetAsync{T} returns null for various valid key strings when using the NoOpRedisClient implementation.
    /// Inputs:
    ///  - key: valid string values including empty, whitespace, normal, emoji, special characters, and a very long string.
    /// Expected:
    ///  - The returned value is null (Task completes successfully with null result).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("normal-key")]
    [TestCase("emoji-😀🚀")]
    [TestCase("special!@#$%^&*()_+-=[]{}|;':\",./<>?")]
    [TestCaseSource(nameof(VeryLongKeys))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task TryGetAsync_WithVariousKeyStrings_ReturnsNull(string key)
    {
        // Arrange
        IRedisCacheClient client = new NoOpRedisClient();

        // Act
        var value = await client.TryGetAsync<string>(key);

        // Assert
        value.Should().BeNull();
    }

    /// <summary>
    /// Ensures that IRedisCacheClient.TryGetAsync{T} returns null for a custom reference type T, validating generic handling.
    /// Inputs:
    ///  - key: "any-key"
    ///  - T: a custom reference type defined in this test class.
    /// Expected:
    ///  - The returned value is null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task TryGetAsync_WithCustomReferenceType_ReturnsNull()
    {
        // Arrange
        IRedisCacheClient client = new NoOpRedisClient();

        // Act
        var value = await client.TryGetAsync<CustomReferenceType>("any-key");

        // Assert
        value.Should().BeNull();
    }

    private static string[] VeryLongKeys()
    {
        return new[]
        {
                new string('a', 4096),
            };
    }

    private class CustomReferenceType
    {
        public string Name { get; set; }

        public CustomReferenceType()
        {
            Name = "x";
        }
    }

    /// <summary>
    /// Verifies that TrySetAsync always returns false for various key and expiration inputs
    /// when the value is a string.
    /// Inputs:
    ///  - key: empty, whitespace, control whitespace, typical, very long, and special-character strings.
    ///  - expiration: null, zero, positive, and negative durations.
    /// Expected:
    ///  - The returned Task completes successfully with result false and does not throw.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(KeysAndExpirations))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TrySetAsync_WithStringValue_VariousKeysAndExpirations_ReturnsFalse(string key, TimeSpan? expiration)
    {
        // Arrange
        var client = new NoOpRedisClient();
        var value = "some-value";

        // Act
        var result = await client.TrySetAsync<string>(key, value, expiration);

        // Assert
        result.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that TrySetAsync returns false for a non-string reference type value across
    /// several expiration inputs.
    /// Inputs:
    ///  - key: typical non-empty string.
    ///  - value: a non-null reference type instance.
    ///  - expiration: null, zero, positive, and negative durations.
    /// Expected:
    ///  - The returned Task completes successfully with result false and does not throw.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Expirations))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TrySetAsync_WithReferenceTypeValue_ReturnsFalse(TimeSpan? expiration)
    {
        // Arrange
        var client = new NoOpRedisClient();
        var key = "key";
        var value = new DummyRef { Id = 1, Name = "ref" };

        // Act
        var result = await client.TrySetAsync<DummyRef>(key, value, expiration);

        // Assert
        result.Should().BeFalse();
    }

    private static System.Collections.Generic.IEnumerable<TestCaseData> KeysAndExpirations()
    {
        var longKey = new string('a', 1024);

        yield return new TestCaseData(string.Empty, null).SetName("EmptyKey_NullExpiration");
        yield return new TestCaseData(" ", TimeSpan.Zero).SetName("WhitespaceKey_ZeroExpiration");
        yield return new TestCaseData("\t \n", TimeSpan.FromSeconds(1)).SetName("ControlWhitespaceKey_PositiveExpiration");
        yield return new TestCaseData("key", TimeSpan.FromSeconds(-1)).SetName("SimpleKey_NegativeExpiration");
        yield return new TestCaseData(longKey, null).SetName("VeryLongKey_NullExpiration");
        yield return new TestCaseData("key:!@#$%^&*()<>?/\\|\"'", TimeSpan.FromMinutes(5)).SetName("SpecialCharsKey_PositiveExpiration");
    }

    private static System.Collections.Generic.IEnumerable<TestCaseData> Expirations()
    {
        yield return new TestCaseData(null).SetName("Null");
        yield return new TestCaseData(TimeSpan.Zero).SetName("Zero");
        yield return new TestCaseData(TimeSpan.FromSeconds(1)).SetName("Positive");
        yield return new TestCaseData(TimeSpan.FromSeconds(-1)).SetName("Negative");
    }

    private class DummyRef
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Validates that TryGetAsync returns a completed task whose result is null for various valid key inputs.
    /// Inputs:
    ///  - key: empty, whitespace-only, very long, and special-character strings (non-null).
    /// Expected:
    ///  - The returned task is already completed and its result is null (since NoOpRedisClient is a no-op).
    /// </summary>
    [Test]
    [TestCaseSource(nameof(ValidKeys))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryGetAsync_VariousValidKeys_ReturnsNullAndCompletedTask(string key)
    {
        // Arrange
        var client = new NoOpRedisClient();

        // Act
        var task = client.TryGetAsync<string>(key);
        var result = await task;

        // Assert
        task.IsCompleted.Should().BeTrue();
        result.Should().BeNull();
    }

    /// <summary>
    /// Ensures that TryGetAsync returns null for a custom reference type T to verify generic behavior.
    /// Inputs:
    ///  - key: "sample-key".
    ///  - T: a custom reference type defined within the test class.
    /// Expected:
    ///  - The returned task is completed and its result is null.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task TryGetAsync_CustomReferenceType_ReturnsNull()
    {
        // Arrange
        var client = new NoOpRedisClient();
        const string key = "sample-key";

        // Act
        var task = client.TryGetAsync<SampleRefType>(key);
        var result = await task;

        // Assert
        task.IsCompleted.Should().BeTrue();
        result.Should().BeNull();
    }

    private static System.Collections.Generic.IEnumerable<string> ValidKeys()
    {
        yield return string.Empty;
        yield return " ";
        yield return " \t  ";
        yield return new string('a', 1024);
        yield return "key:with:special/\\chars?*&^%$#@!\0\u001F";
    }

    private class SampleRefType
    {
        public int Id { get; set; }
    }

    /// <summary>
    /// Verifies that DeleteAsync always returns a completed task with result false for a variety of key inputs.
    /// Inputs:
    ///  - key: a range of strings including empty, whitespace, special characters, control characters, and very long strings.
    /// Expected:
    ///  - The returned task is completed synchronously.
    ///  - The result is false.
    ///  - No exception is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(DeleteKeys))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task DeleteAsync_VariousKeys_ReturnsFalseAndCompletesSynchronously(string key)
    {
        // Arrange
        var sut = new NoOpRedisClient();

        // Act
        var task = sut.DeleteAsync(key);
        var isCompletedSynchronously = task.IsCompleted;
        var result = await task;

        // Assert
        isCompletedSynchronously.Should().BeTrue();
        result.Should().BeFalse();
    }

    private static System.Collections.Generic.IEnumerable<string> DeleteKeys()
    {
        yield return "key";
        yield return string.Empty;
        yield return " ";
        yield return "   ";
        yield return "🔥特殊字符";
        yield return "line\nbreak\tand\0control";
        yield return "\"quotes\" and 'single'";
        yield return "\\back\\slash\\";
        yield return new string('a', 4096);
    }
}
