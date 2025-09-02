// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.UnitTests;

/// <summary>
/// Tests for ITelemetryRecorder.RecordWorkItemCompletion via NoTelemetryRecorder implementation.
/// Focuses on ensuring a non-null ITelemetryScope is returned and that calling SetSuccess and Dispose do not throw
/// across boundary and special input values.
/// </summary>
public class NoTelemetryRecorderTests
{
    /// <summary>
    /// Ensures RecordWorkItemCompletion returns a non-null ITelemetryScope and that using the scope
    /// (calling SetSuccess and Dispose) does not throw, across edge inputs.
    /// Inputs:
    ///  - workItemName: normal, whitespace-only, very long, and special-character strings (non-null).
    ///  - attemptNumber: long.MinValue, -1, 0, 1, long.MaxValue.
    ///  - operationId: normal, whitespace-only, very long, and special-character strings (non-null).
    /// Expected:
    ///  - A non-null ITelemetryScope is returned.
    ///  - Calling SetSuccess and Dispose does not throw any exception.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(RecordWorkItemCompletion_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void RecordWorkItemCompletion_VariousInputs_ReturnsScopeAndAllowsSuccessAndDispose(string workItemName, long attemptNumber, string operationId)
    {
        // Arrange
        var recorder = new NoTelemetryRecorder();

        // Act
        ITelemetryScope scope = recorder.RecordWorkItemCompletion(workItemName, attemptNumber, operationId);

        // Assert
        scope.Should().NotBeNull();

        // Using the scope should not throw; if it does, the test will fail naturally.
        scope.SetSuccess();
        scope.Dispose();
    }

    private static IEnumerable RecordWorkItemCompletion_Cases()
    {
        yield return new TestCaseData("work", 0L, "op-0")
            .SetName("RecordWorkItemCompletion_ZeroAttempt_NormalStrings_ReturnsScope");
        yield return new TestCaseData("w", 1L, "op")
            .SetName("RecordWorkItemCompletion_PositiveAttempt_ShortStrings_ReturnsScope");
        yield return new TestCaseData("     ", -1L, "   ")
            .SetName("RecordWorkItemCompletion_NegativeAttempt_WhitespaceStrings_ReturnsScope");
        yield return new TestCaseData(new string('a', 1024), long.MaxValue, new string('b', 1024))
            .SetName("RecordWorkItemCompletion_LongAttemptMax_VeryLongStrings_ReturnsScope");
        yield return new TestCaseData("name-😊-特殊\t\n", long.MinValue, "op-#%&*()-_=+[]{};:'\",.<>/?|\\")
            .SetName("RecordWorkItemCompletion_LongAttemptMin_SpecialChars_ReturnsScope");
    }

    /// <summary>
    /// Provides diverse input combinations for work item name, attempt number, and operation id, including edge cases.
    /// </summary>
    public static IEnumerable RecordWorkItemCompletion_InputCases
    {
        get
        {
            yield return new TestCaseData("work", 1L, "op-1");
            yield return new TestCaseData(string.Empty, 0L, "op-0");
            yield return new TestCaseData("   ", -1L, "\t\n ");
            yield return new TestCaseData("🚀特殊文字", long.MaxValue, "ID:🔥");
            yield return new TestCaseData(new string('x', 1024), long.MinValue, new string('y', 2048));
            yield return new TestCaseData("name-with-specials-!@#$%^&*()", 42L, "op-!@#");
        }
    }

    /// <summary>
    /// Ensures that for a variety of inputs, RecordWorkItemCompletion returns a non-null, reusable no-op scope,
    /// and that calling SetSuccess() followed by Dispose() (even multiple times) does not throw.
    /// Inputs:
    ///  - workItemName: includes empty, whitespace, long, and special-character strings (non-null).
    ///  - attemptNumber: includes long.MinValue, -1, 0, 1, and long.MaxValue.
    ///  - operationId: includes whitespace, special characters, and very long strings (non-null).
    /// Expected:
    ///  - The returned ITelemetryScope is non-null and the same instance on subsequent calls.
    ///  - SetSuccess() and Dispose() calls do not throw exceptions.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(RecordWorkItemCompletion_InputCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RecordWorkItemCompletion_VariousInputs_ReturnsReusableNoopScope(string workItemName, long attemptNumber, string operationId)
    {
        // Arrange
        var recorder = new NoTelemetryRecorder();

        // Act
        var scope = recorder.RecordWorkItemCompletion(workItemName, attemptNumber, operationId);
        var scopeAgain = recorder.RecordWorkItemCompletion("alt-" + workItemName, attemptNumber + 1, operationId + "-2");

        // Assert
        scope.Should().NotBeNull();
        scopeAgain.Should().BeSameAs(scope);

        Action act = () =>
        {
            scope.SetSuccess();
            scope.Dispose();
            scope.Dispose();
        };

        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies that even when using different NoTelemetryRecorder instances, RecordWorkItemCompletion
    /// returns the same singleton ITelemetryScope instance.
    /// Inputs:
    ///  - Two different NoTelemetryRecorder instances.
    ///  - Arbitrary non-null input strings and attempt numbers.
    /// Expected:
    ///  - The returned scopes are the same reference instance.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RecordWorkItemCompletion_DifferentRecorderInstances_ReturnsSameScope()
    {
        // Arrange
        var recorder1 = new NoTelemetryRecorder();
        var recorder2 = new NoTelemetryRecorder();

        // Act
        var scope1 = recorder1.RecordWorkItemCompletion("task-1", 10L, "op-a");
        var scope2 = recorder2.RecordWorkItemCompletion("task-2", 20L, "op-b");

        // Assert
        scope1.Should().NotBeNull();
        scope2.Should().BeSameAs(scope1);
    }

    /// <summary>
    /// Verifies that RecordGitOperation returns the same singleton scope instance for various TrackedGitOperation values,
    /// including defined enum values and out-of-range values cast to the enum.
    /// Inputs:
    ///  - operation: Clone, Fetch, Push, -1 (invalid), int.MaxValue (invalid).
    ///  - repoUri: a valid non-empty string.
    /// Expected:
    ///  - Returned ITelemetryScope is not null.
    ///  - Returned instance is of type NoTelemetryRecorder.NoTelemetryScope.
    ///  - Subsequent calls return the same instance (reference equality).
    ///  - Using the scope (SetSuccess, Dispose) does not throw.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(TrackedGitOperation.Clone)]
    [TestCase(TrackedGitOperation.Fetch)]
    [TestCase(TrackedGitOperation.Push)]
    [TestCase((TrackedGitOperation)(-1))]
    [TestCase((TrackedGitOperation)int.MaxValue)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RecordGitOperation_OperationVariants_ReturnsSingletonScope(TrackedGitOperation operation)
    {
        // Arrange
        var sut = new NoTelemetryRecorder();
        var repoUri = "https://example.org/repo";

        // Act
        var scope1 = sut.RecordGitOperation(operation, repoUri);
        var scope2 = sut.RecordGitOperation(TrackedGitOperation.Push, "https://another/repo");

        // Assert
        scope1.Should().NotBeNull();
        scope1.Should().BeOfType<NoTelemetryRecorder.NoTelemetryScope>();
        scope1.Should().BeSameAs(scope2);

        Action useScope = () =>
        {
            using (scope1)
            {
                scope1.SetSuccess();
            }
        };
        useScope.Should().NotThrow();
    }

    /// <summary>
    /// Verifies that RecordGitOperation returns the same singleton scope instance for various repoUri edge cases,
    /// including empty, whitespace, typical URL, long string, and strings with special/control characters.
    /// Inputs:
    ///  - operation: Push (constant).
    ///  - repoUri: provided by RepoUriCases TestCaseSource.
    /// Expected:
    ///  - Returned ITelemetryScope is not null.
    ///  - Returned instance is of type NoTelemetryRecorder.NoTelemetryScope.
    ///  - Subsequent calls with different repoUris return the same instance (reference equality).
    ///  - Using the scope (SetSuccess, Dispose) does not throw.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(RepoUriCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RecordGitOperation_RepoUriEdgeCases_ReturnsSingletonScope(string repoUri)
    {
        // Arrange
        var sut = new NoTelemetryRecorder();

        // Act
        var scope1 = sut.RecordGitOperation(TrackedGitOperation.Push, repoUri);
        var scope2 = sut.RecordGitOperation(TrackedGitOperation.Fetch, "https://another.example/repo");

        // Assert
        scope1.Should().NotBeNull();
        scope1.Should().BeOfType<NoTelemetryRecorder.NoTelemetryScope>();
        scope1.Should().BeSameAs(scope2);

        Action useScope = () =>
        {
            using (scope1)
            {
                scope1.SetSuccess();
            }
        };
        useScope.Should().NotThrow();
    }

    // Test data sources

    private static IEnumerable RepoUriCases()
    {
        yield return string.Empty; // empty
        yield return "   "; // whitespace
        yield return "https://repo/with/path";
        yield return new string('a', 2048); // long string
        yield return "file://C:\\path with spaces\\file.txt?x=1&y=✓\t\n";
    }

    /// <summary>
    /// Ensures that RecordCustomEvent does not throw for diverse inputs.
    /// Inputs:
    ///  - customEvent: all defined enum values and out-of-range enum values (via casting).
    ///  - customProperties: empty, single-item, whitespace keys, special characters, and very long strings.
    /// Expected:
    ///  - No exception is thrown for any input since the method is a no-op.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(CustomEventTestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void RecordCustomEvent_VariedInputs_DoesNotThrow(CustomEventType customEvent, Dictionary<string, string> customProperties)
    {
        // Arrange
        var recorder = new NoTelemetryRecorder();

        // Act
        recorder.RecordCustomEvent(customEvent, customProperties);

        // Assert
        // No assertions required: success criterion is absence of exceptions from the no-op implementation.
    }

    private static IEnumerable CustomEventTestCases()
    {
        // Case 1: Defined enum + empty dictionary
        yield return new TestCaseData(
            CustomEventType.PullRequestUpdateFailed,
            new Dictionary<string, string>()
        ).SetName("RecordCustomEvent_DefinedEnum_EmptyDictionary");

        // Case 2: Out-of-range negative enum + single item
        yield return new TestCaseData(
            (CustomEventType)(-1),
            new Dictionary<string, string> { { "key", "value" } }
        ).SetName("RecordCustomEvent_NegativeEnum_SingleItem");

        // Case 3: Out-of-range large enum + whitespace key and empty value
        yield return new TestCaseData(
            (CustomEventType)int.MaxValue,
            new Dictionary<string, string> { { "   ", "" } }
        ).SetName("RecordCustomEvent_LargeEnum_WhitespaceKeyEmptyValue");

        // Case 4: Defined enum + special characters and control characters
        yield return new TestCaseData(
            CustomEventType.PullRequestUpdateFailed,
            new Dictionary<string, string>
            {
                    { "newline\nkey", "tab\tvalue" },
                    { "unicode-π", "emoji-😊" },
                    { "path-like", @"C:\temp\file.txt" }
            }
        ).SetName("RecordCustomEvent_DefinedEnum_SpecialCharacters");

        // Case 5: Out-of-range enum + very long strings
        var longKey = new string('k', 4096);
        var longValue = new string('v', 8192);
        yield return new TestCaseData(
            (CustomEventType)123456,
            new Dictionary<string, string> { { longKey, longValue } }
        ).SetName("RecordCustomEvent_OutOfRangeEnum_VeryLongStrings");

        // Case 6: Defined enum + many key-value pairs
        var many = new Dictionary<string, string>();
        for (int i = 0; i < 100; i++)
        {
            many.Add("k" + i, "v" + i);
        }
        yield return new TestCaseData(
            CustomEventType.PullRequestUpdateFailed,
            many
        ).SetName("RecordCustomEvent_DefinedEnum_ManyEntries");
    }
}


/// <summary>
/// Tests for NoTelemetryRecorder.NoTelemetryScope.SetSuccess.
/// Focuses on ensuring no exceptions are thrown given the no-op implementation.
/// </summary>
public class NoTelemetryScopeTests
{
    /// <summary>
    /// Ensures that calling SetSuccess multiple times on the no-op scope does not throw any exceptions.
    /// Inputs:
    ///  - A new NoTelemetryRecorder.NoTelemetryScope instance.
    ///  - Multiple invocations of SetSuccess.
    /// Expected:
    ///  - No exception is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void SetSuccess_MultipleInvocations_DoesNotThrow()
    {
        // Arrange
        var scope = new NoTelemetryRecorder.NoTelemetryScope();

        // Act
        Action act = () =>
        {
            scope.SetSuccess();
            scope.SetSuccess();
            scope.SetSuccess();
        };

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies that SetSuccess can be called prior to disposing the scope without throwing exceptions.
    /// Inputs:
    ///  - A new NoTelemetryRecorder.NoTelemetryScope instance.
    ///  - Call SetSuccess once, then Dispose.
    /// Expected:
    ///  - No exception is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void SetSuccess_BeforeDispose_DoesNotThrow()
    {
        // Arrange
        var scope = new NoTelemetryRecorder.NoTelemetryScope();

        // Act
        Action act = () =>
        {
            scope.SetSuccess();
            scope.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies that calling Dispose once on NoTelemetryScope completes without throwing any exception.
    /// Inputs:
    ///  - A new instance of NoTelemetryRecorder.NoTelemetryScope.
    /// Expected:
    ///  - No exception is thrown when Dispose is invoked.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Dispose_SingleCall_DoesNotThrow()
    {
        // Arrange
        var scope = new NoTelemetryRecorder.NoTelemetryScope();

        // Act
        Action act = () => scope.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies that calling Dispose multiple times on the same NoTelemetryScope instance does not throw.
    /// Inputs:
    ///  - A new instance of NoTelemetryRecorder.NoTelemetryScope.
    ///  - Repeated calls to Dispose based on provided test case counts.
    /// Expected:
    ///  - No exception is thrown for any number of consecutive Dispose calls.
    /// </summary>
    [TestCase(2)]
    [TestCase(5)]
    [TestCase(10)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Dispose_MultipleCalls_DoesNotThrow(int disposeCalls)
    {
        // Arrange
        var scope = new NoTelemetryRecorder.NoTelemetryScope();

        // Act
        Action act = () =>
        {
            for (int i = 0; i < disposeCalls; i++)
            {
                scope.Dispose();
            }
        };

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies that using NoTelemetryScope in a using-statement, which implicitly calls Dispose,
    /// completes without throwing any exception.
    /// Inputs:
    ///  - A using-statement scope with a new NoTelemetryRecorder.NoTelemetryScope instance.
    /// Expected:
    ///  - No exception is thrown when the using block exits (Dispose is called).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Dispose_UsingStatement_DoesNotThrow()
    {
        // Arrange, Act
        Action act = () =>
        {
            using (var scope = new NoTelemetryRecorder.NoTelemetryScope())
            {
                // No operation inside; Dispose will be called on exit.
            }
        };

        // Assert
        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies that SetSuccess does not throw when invoked repeatedly.
    /// Inputs:
    ///  - repeatCount: number of times SetSuccess is called on the same scope instance.
    /// Expected:
    ///  - No exception is thrown for any invocation count.
    /// </summary>
    /// <param name="repeatCount">How many times to call SetSuccess on the same instance.</param>
    [Test]
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(10)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SetSuccess_CalledRepeatedly_DoesNotThrow(int repeatCount)
    {
        // Arrange
        var scope = new NoTelemetryRecorder.NoTelemetryScope();

        // Act
        Action act = () =>
        {
            for (int i = 0; i < repeatCount; i++)
            {
                scope.SetSuccess();
            }
        };

        // Assert
        act.Should().NotThrow();
    }

}
