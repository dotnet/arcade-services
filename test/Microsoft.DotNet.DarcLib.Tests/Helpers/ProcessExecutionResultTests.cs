// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Helpers.UnitTests;

public class ProcessExecutionResultTests
{
    /// <summary>
    /// Verifies the Succeeded property logic across combinations of TimedOut and ExitCode, including boundary values.
    /// Inputs: timedOut flag and exitCode values (int.MinValue, -1, 0, 1, int.MaxValue).
    /// Expected: Succeeded is true only when TimedOut is false and ExitCode equals 0; false otherwise.
    /// </summary>
    /// <param name="timedOut">Whether the process timed out.</param>
    /// <param name="exitCode">The process exit code including boundary values.</param>
    /// <param name="expectedSucceeded">The expected result of the Succeeded property.</param>
    [TestCase(false, 0, true, TestName = "Succeeded_NotTimedOut_ExitCodeZero_ReturnsTrue")]
    [TestCase(true, 0, false, TestName = "Succeeded_TimedOut_ExitCodeZero_ReturnsFalse")]
    [TestCase(false, 1, false, TestName = "Succeeded_NotTimedOut_PositiveNonZeroExitCode_ReturnsFalse")]
    [TestCase(false, -1, false, TestName = "Succeeded_NotTimedOut_NegativeExitCode_ReturnsFalse")]
    [TestCase(true, 1, false, TestName = "Succeeded_TimedOut_PositiveNonZeroExitCode_ReturnsFalse")]
    [TestCase(false, int.MinValue, false, TestName = "Succeeded_NotTimedOut_IntMinValueExitCode_ReturnsFalse")]
    [TestCase(false, int.MaxValue, false, TestName = "Succeeded_NotTimedOut_IntMaxValueExitCode_ReturnsFalse")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Succeeded_TimedOutAndExitCode_ReturnsExpected(bool timedOut, int exitCode, bool expectedSucceeded)
    {
        // Arrange
        var result = new ProcessExecutionResult
        {
            TimedOut = timedOut,
            ExitCode = exitCode,
        };

        // Act
        var succeeded = result.Succeeded;

        // Assert
        succeeded.Should().Be(expectedSucceeded);
    }

    /// <summary>
    /// Ensures that when the execution did not succeed (either due to timeout or non-zero exit code),
    /// ThrowIfFailed throws ProcessFailedException and preserves the ExecutionResult instance.
    /// Inputs (parameterized):
    ///  - timedOut: true/false
    ///  - exitCode: int.MinValue, -1, 1, int.MaxValue, 0 (when timedOut == true)
    ///  - failureMessage: "failure-message"
    /// Expected:
    ///  - ProcessFailedException is thrown.
    ///  - Exception.ExecutionResult is the same instance as the ProcessExecutionResult under test.
    ///  - Exception.Message starts with the provided failureMessage.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(false, 1)]
    [TestCase(false, -1)]
    [TestCase(false, int.MinValue)]
    [TestCase(false, int.MaxValue)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ThrowIfFailed_NotSucceeded_ThrowsProcessFailedExceptionWithExecutionResult(bool timedOut, int exitCode)
    {
        // Arrange
        var failureMessage = "failure-message";
        var result = new ProcessExecutionResult
        {
            TimedOut = timedOut,
            ExitCode = exitCode,
            StandardOutput = "out",
            StandardError = "err"
        };

        // Act
        Action act = () => result.ThrowIfFailed(failureMessage);

        // Assert
        var exception = act.Should().Throw<ProcessFailedException>().Which;
        exception.ExecutionResult.Should().BeSameAs(result);
        exception.Message.Should().StartWith(failureMessage);
    }

    /// <summary>
    /// Provides diverse inputs covering empty outputs, single/multiple lines, various newline combinations,
    /// whitespace-only lines, leading/trailing separators, and long content. 
    /// Inputs:
    ///  - Different StandardOutput strings, including CR, LF, CRLF, duplicates, and whitespace.
    /// Expected:
    ///  - Returned lines are split on both '\r' and '\n', trimmed, and empty/whitespace-only lines are removed.
    /// </summary>
    public static IEnumerable GetOutputLines_Cases
    {
        get
        {
            yield return new TestCaseData(string.Empty, Array.Empty<string>())
                .SetName("GetOutputLines_EmptyOutput_ReturnsEmpty");

            yield return new TestCaseData("one", new[] { "one" })
                .SetName("GetOutputLines_SingleLine_NoChange");

            yield return new TestCaseData("  one  ", new[] { "one" })
                .SetName("GetOutputLines_SingleLineTrimmed_WhitespaceRemoved");

            yield return new TestCaseData("a\nb", new[] { "a", "b" })
                .SetName("GetOutputLines_UnixNewlines_SplitIntoTwoLines");

            yield return new TestCaseData("a\rb", new[] { "a", "b" })
                .SetName("GetOutputLines_OldMacNewlines_SplitIntoTwoLines");

            yield return new TestCaseData("a\r\nb", new[] { "a", "b" })
                .SetName("GetOutputLines_WindowsNewlines_SplitIntoTwoLines");

            yield return new TestCaseData("\nline1\n", new[] { "line1" })
                .SetName("GetOutputLines_LeadingAndTrailingNewlines_EmptyEntriesRemoved");

            yield return new TestCaseData("line1\r\n\r\nline2", new[] { "line1", "line2" })
                .SetName("GetOutputLines_ConsecutiveNewlines_EmptyEntriesRemoved");

            yield return new TestCaseData(" \r\n ", Array.Empty<string>())
                .SetName("GetOutputLines_WhitespaceOnlyLines_RemovedAfterTrim");

            yield return new TestCaseData("  x  \n  y ", new[] { "x", "y" })
                .SetName("GetOutputLines_LinesWithSurroundingSpaces_Trimmed");

            yield return new TestCaseData("a\r\n\nb", new[] { "a", "b" })
                .SetName("GetOutputLines_MixedCRLFAndLF_EmptyBetweenRemoved");

            yield return new TestCaseData(new string('x', 10000) + "\n" + new string('y', 5000),
                                          new[] { new string('x', 10000), new string('y', 5000) })
                .SetName("GetOutputLines_VeryLongLines_SplitCorrectly");
        }
    }

    /// <summary>
    /// Verifies that GetOutputLines splits by both '\r' and '\n', trims whitespace from each line,
    /// and omits empty or whitespace-only lines.
    /// Inputs:
    ///  - Various StandardOutput strings provided via GetOutputLines_Cases.
    /// Expected:
    ///  - The returned collection exactly equals the expected lines sequence (order and content).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(GetOutputLines_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetOutputLines_VariousInputs_SplitsTrimsAndOmitsEmpty(string standardOutput, string[] expected)
    {
        // Arrange
        var sut = new ProcessExecutionResult
        {
            StandardOutput = standardOutput
        };

        // Act
        var lines = sut.GetOutputLines();

        // Assert
        lines.Should().Equal(expected);
    }

    /// <summary>
    /// Validates ToString formats various combinations of StandardOutput and StandardError precisely.
    /// Inputs:
    ///  - exitCode: Representative exit code to include in the first line.
    ///  - standardOutput: Content for StandardOutput; empty string means the output section is omitted.
    ///  - standardError: Content for StandardError; empty string means the error section is omitted.
    /// Expected:
    ///  - The formatted string contains:
    ///    * "Exit code: {exitCode}" followed by a newline.
    ///    * If StandardOutput is not empty, a "Std out:" section followed by content and two trailing newlines.
    ///    * If StandardError is not empty, a "Std err:" section followed by content and two trailing newlines.
    ///    * Order is Std out first, then Std err.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(0, "", "", TestName = "ToString_VariousOutputCombinations_FormatsExactly_NoOutputNoError")]
    [TestCase(0, "hello", "", TestName = "ToString_VariousOutputCombinations_FormatsExactly_OutputOnly")]
    [TestCase(0, "", "oops", TestName = "ToString_VariousOutputCombinations_FormatsExactly_ErrorOnly")]
    [TestCase(0, "line1" + "\n" + "line2", "E1", TestName = "ToString_VariousOutputCombinations_FormatsExactly_MultiLineOutputAndError")]
    [TestCase(0, "  ", "\t", TestName = "ToString_VariousOutputCombinations_FormatsExactly_WhitespaceContentIncluded")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ToString_VariousOutputCombinations_FormatsExactly(int exitCode, string standardOutput, string standardError)
    {
        // Arrange
        var result = new ProcessExecutionResult
        {
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError
        };

        var expected = BuildExpected(exitCode, standardOutput, standardError);

        // Act
        var actual = result.ToString();

        // Assert
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Ensures ToString includes the exact exit code across boundary values without alteration.
    /// Inputs:
    ///  - exitCode: Uses int.MinValue, -1, 0, 1, int.MaxValue.
    /// Expected:
    ///  - Output starts with "Exit code: {exitCode}" followed by a newline and no additional sections when outputs are empty.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(int.MinValue, TestName = "ToString_ExitCodeBoundaryValues_ExitCodeLineMatches_IntMin")]
    [TestCase(-1, TestName = "ToString_ExitCodeBoundaryValues_ExitCodeLineMatches_NegativeOne")]
    [TestCase(0, TestName = "ToString_ExitCodeBoundaryValues_ExitCodeLineMatches_Zero")]
    [TestCase(1, TestName = "ToString_ExitCodeBoundaryValues_ExitCodeLineMatches_PositiveOne")]
    [TestCase(int.MaxValue, TestName = "ToString_ExitCodeBoundaryValues_ExitCodeLineMatches_IntMax")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void ToString_ExitCodeBoundaryValues_ExitCodeLineMatches(int exitCode)
    {
        // Arrange
        var result = new ProcessExecutionResult
        {
            ExitCode = exitCode,
            StandardOutput = string.Empty,
            StandardError = string.Empty
        };

        var expected = BuildExpected(exitCode, string.Empty, string.Empty);

        // Act
        var actual = result.ToString();

        // Assert
        actual.Should().Be(expected);
    }

    private static string BuildExpected(int exitCode, string standardOutput, string standardError)
    {
        var nl = Environment.NewLine;
        var sb = new StringBuilder();
        sb.AppendLine($"Exit code: {exitCode}");

        if (!string.IsNullOrEmpty(standardOutput))
        {
            sb.AppendLine($"Std out:{nl}{standardOutput}{nl}");
        }

        if (!string.IsNullOrEmpty(standardError))
        {
            sb.AppendLine($"Std err:{nl}{standardError}{nl}");
        }

        return sb.ToString();
    }
}
