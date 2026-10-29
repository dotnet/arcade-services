// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Options.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests;

/// <summary>
/// Covers the parsing behaviours that the CLI surface baseline cannot express, all of which had to
/// be preserved when darc moved from CommandLineParser to System.CommandLine.
/// </summary>
[TestFixture]
public class CommandLineParsingTests
{
    [Test]
    public void OmittedOptionsKeepTheirPropertyInitializers()
    {
        var options = Parse(new GetSubscriptionHistoryCommandLineOptions(), "get-subscription-history");

        options.Limit.Should().Be(10);
    }

    [Test]
    public void OmittedCollectionOptionsAreEmptyRatherThanNull()
    {
        var options = Parse(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions");

        // Neither property declares an initializer, yet darc has always handed the operations an
        // empty collection rather than null.
        options.Frequencies.Should().NotBeNull().And.BeEmpty();
        options.SubscriptionIds.Should().NotBeNull().And.BeEmpty();
    }

    [Test]
    public void OmittedVmrPathFallsBackToTheCurrentDirectory()
    {
        var options = Parse(new AddRepoCommandLineOptions(), "add-repo", "https://github.com/dotnet/runtime:abc123");

        options.VmrPath.Should().Be(Environment.CurrentDirectory);
    }

    [Test]
    public void SeparatedListOptionsPreserveEmptyEntries()
    {
        Parse(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions", "--ids", "a,,b")
            .SubscriptionIds.Should().Equal("a", string.Empty, "b");
    }

    [Test]
    public void CollectionOptionsRequireAtLeastOneValue()
    {
        ParseResultOf(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions", "--ids")
            .Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("--ids");
    }

    [Test]
    public void CollectionOptionsDoNotSwallowMistypedOptions()
    {
        ParseResultOf(new ForwardFlowCommandLineOptions(), "forwardflow", "--additional-remote", "--bogus")
            .Errors.Should().Contain(error => error.Message.Contains("--bogus"));

        ParseResultOf(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions", "--ids", "a", "--bogus")
            .Errors.Should().Contain(error => error.Message.Contains("--bogus"));
    }

    [Test]
    public void IntegerListOptionsRejectNonNumericValues()
    {
        ParseResultOf(new GatherDropCommandLineOptions(), "gather-drop", "--id", "1,x")
            .Errors.Should().Contain(error => error.Message.Contains("'x' is not a valid integer"));
    }

    [Test]
    public void SeparatedListOptionsAcceptPackedAndRepeatedValues()
    {
        var packed = Parse(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions", "--ids", "a,b,c");
        packed.SubscriptionIds.Should().Equal("a", "b", "c");

        var repeated = Parse(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions", "--ids", "a", "--ids", "b");
        repeated.SubscriptionIds.Should().Equal("a", "b");
    }

    [Test]
    public void ListOptionsAcceptRepeatedAndSpaceSeparatedValues()
    {
        var repeated = Parse(
            new ForwardFlowCommandLineOptions(),
            "forwardflow",
            "--additional-remote",
            "runtime:/one",
            "--additional-remote",
            "sdk:/two");

        repeated.AdditionalRemotes.Should().Equal("runtime:/one", "sdk:/two");

        var packed = Parse(
            new ForwardFlowCommandLineOptions(),
            "forwardflow",
            "--additional-remote",
            "runtime:/one",
            "sdk:/two");

        packed.AdditionalRemotes.Should().Equal("runtime:/one", "sdk:/two");
    }

    [Test]
    public void OutputFormatIsCaseSensitive()
    {
        Parse(new GetChannelsCommandLineOptions(), "get-channels", "--output-format", "json")
            .OutputFormat.Should().Be(DarcOutputType.json);

        ParseResultOf(new GetChannelsCommandLineOptions(), "get-channels", "--output-format", "JSON")
            .Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("case sensitive");
    }

    [Test]
    public async Task UnsupportedOutputFormatsFailAsCommandLineErrors()
    {
        RootCommand root = new();
        root.Add(Program.CreateCommand(new VerifyCommandLineOptions()));

        // The enum itself parses, so 'verify' only rejects json when the value reaches the options instance.
        ParseResult result = Program.Parse(root, "verify", "--output-format", "json");
        result.Errors.Should().BeEmpty();

        StringWriter error = new();
        TextWriter previousError = Console.Error;
        Console.SetError(error);

        int exitCode;
        try
        {
            exitCode = await result.InvokeAsync(new InvocationConfiguration
            {
                Output = TextWriter.Null,
                Error = TextWriter.Null,
            });
        }
        finally
        {
            Console.SetError(previousError);
        }

        exitCode.Should().Be(Constants.CommandLineErrorCode);
        error.ToString().Should().Contain("Output format json is not supported");
    }

    [Test]
    public void ValuesStartingWithAtAreNotExpandedAsResponseFiles()
    {
        var options = Parse(
            new UpdateSubscriptionCommandLineOptions(),
            "update-subscription",
            "--id",
            "00000000-0000-0000-0000-000000000000",
            "--failure-notification-tags",
            "@dotnet/some-team");

        options.FailureNotificationTags.Should().Be("@dotnet/some-team");
    }

    [Test]
    public void NullableBooleanOptionsActAsFlags()
    {
        // The old parser left a bare '--enabled' unset and silently dropped what the user asked for.
        // Treating it as 'true' is what both the help text and docs/Darc.md have always described.
        Parse(new UpdateSubscriptionCommandLineOptions(), "update-subscription", "--id", "abc", "--enabled")
            .Enabled.Should().BeTrue();

        Parse(new UpdateSubscriptionCommandLineOptions(), "update-subscription", "--id", "abc", "--enabled", "false")
            .Enabled.Should().BeFalse();

        // Leaving the option off has to stay distinguishable from passing it, because
        // update-subscription only touches the fields that were actually specified.
        Parse(new UpdateSubscriptionCommandLineOptions(), "update-subscription", "--id", "abc")
            .Enabled.Should().BeNull();
    }

    [Test]
    public void NullableBooleanFlagsDoNotConsumeTheFollowingOption()
    {
        // 'darc set-repository-policies --repo r --branch main --merge-prs --ignore-checks WIP -q'
        // is the documented sample, so the bare flag must not swallow '--ignore-checks'.
        SetRepositoryMergePoliciesCommandLineOptions options = Parse(
            new SetRepositoryMergePoliciesCommandLineOptions(),
            "set-repository-policies", "--repo", "r", "--branch", "main",
            "--merge-prs", "--ignore-checks", "WIP,license/cla", "-q");

        options.MergePrs.Should().BeTrue();
        options.IgnoredChecks.Should().BeEquivalentTo(["WIP", "license/cla"]);
        options.Quiet.Should().BeTrue();
    }

    [Test]
    public void PlainBooleanOptionsStayFlags()
    {
        // Non-nullable switches carry no 'unspecified' state, so they only ever need to be present.
        Parse(new GetSubscriptionsCommandLineOptions(), "get-subscriptions", "--verbose").Verbose.Should().BeTrue();
    }

    [Test]
    public void ScalarOptionsDoNotSwallowMistypedOptions()
    {
        // Without a guard the value-less '--target-repo' takes the next option as its value, which
        // would query for a repository literally named '--disabled'.
        ParseResultOf(new GetSubscriptionsCommandLineOptions(), "get-subscriptions", "--target-repo", "--disabled")
            .Errors.Should().Contain(error => error.Message.Contains("--disabled"));

        ParseResultOf(new GetSubscriptionsCommandLineOptions(), "get-subscriptions", "--target-repo", "--help")
            .Errors.Should().Contain(error => error.Message.Contains("--help"));

        Parse(new GetSubscriptionsCommandLineOptions(), "get-subscriptions", "--target-repo", "dotnet/runtime")
            .TargetRepository.Should().Be("dotnet/runtime");
    }

    [Test]
    public void ExactAndRegexCannotBeCombined()
    {
        ParseResultOf(new GetSubscriptionsCommandLineOptions(), "get-subscriptions", "--exact", "--regex")
            .Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("cannot be used together");
    }

    [Test]
    public void RedeclaredOptionsBindToTheDerivedProperty()
    {
        var options = Parse(new LoginCommandLineOptions(), "login", "--bar-uri", "https://example.test");

        options.BarUri.Should().Be("https://example.test");
        options.BuildAssetRegistryBaseUri.Should().BeNull();
    }

    [Test]
    public void PositionalArgumentsBindAlongsideOptions()
    {
        var options = Parse(new AddRepoCommandLineOptions(), "add-repo", "https://github.com/dotnet/runtime:abc123");

        options.Repositories.Should().Equal("https://github.com/dotnet/runtime:abc123");
    }

    private static T Parse<T>(T options, params string[] args) where T : CommandLineOptions
    {
        var result = ParseResultOf(options, args);
        result.Errors.Should().BeEmpty();
        options.Bind(result);
        return options;
    }

    private static ParseResult ParseResultOf(CommandLineOptions options, params string[] args)
    {
        RootCommand root = new();
        root.Add(Program.CreateCommand(options));
        return Program.Parse(root, args);
    }
}
