// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.CommandLine;
using System.CommandLine.Help;
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
    public void ExplicitlyAssignedOptionLikeValuesAreKept()
    {
        // The old parser took '--search=--foo' literally, so this has to keep binding the value
        // rather than reporting '--foo' as an unknown option.
        Parse(new GetSubscriptionHistoryCommandLineOptions(), "get-subscription-history", "--search=--foo")
            .Search.Should().Be("--foo");

        Parse(new GetSubscriptionsCommandLineOptions(), "get-subscriptions", "--target-repo", "dotnet/runtime")
            .TargetRepository.Should().Be("dotnet/runtime");
    }

    [Test]
    public void RecognisedOptionsAreNotSwallowedAsValues()
    {
        // 'darc get-subscriptions --target-repo --disabled' used to bind '--disabled' as the
        // repository name, so the user silently got an unfiltered query instead of an error.
        ParseResultOf(new GetSubscriptionsCommandLineOptions(), "get-subscriptions", "--target-repo", "--disabled")
            .Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("--target-repo");

        ParseResultOf(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions", "--ids", "--quiet")
            .Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("--ids");
    }

    [Test]
    public void HelpStillWorksAfterAnOptionThatExpectsAValue()
    {
        // Binding '--help' as a value also leaves the parse error-free, so the action is what has to
        // be asserted here.
        ParseResultOf(new GetSubscriptionsCommandLineOptions(), "get-subscriptions", "--target-repo", "--help")
            .Action.Should().BeOfType<HelpAction>();

        ParseResultOf(new DeleteSubscriptionsCommandLineOptions(), "delete-subscriptions", "--ids", "--help")
            .Action.Should().BeOfType<HelpAction>();
    }

    [Test]
    public void ShortOptionsKeepTheirAttachedValues()
    {
        // '-n' is followed by '-p', the alias of --password, so without normalisation the token would
        // unbundle into '-n -p ackage' and leave --name without a value.
        Parse(new GetDependenciesCommandLineOptions(), "get-dependencies", "-npackage")
            .Name.Should().Be("package");

        // A token that could unbundle is still taken literally when the option in front of it is
        // waiting for its value, which is what the parser did before the arity was relaxed.
        Parse(new GetDependenciesCommandLineOptions(), "get-dependencies", "--name", "-npackage")
            .Name.Should().Be("-npackage");

        // Flags must keep bundling rather than swallowing the rest of the token as a value.
        var bundled = Parse(new GetDependencyGraphCommandLineOptions(), "get-dependency-graph", "-fl");
        bundled.Flat.Should().BeTrue();
        bundled.Local.Should().BeTrue();

        // A bundle may mix flags and a value-taking option, in which case the value belongs to the
        // first alias that takes one. '-p' of --password follows '-v' here.
        var mixed = Parse(new GetDependencyGraphCommandLineOptions(), "get-dependency-graph", "-fvpreview");
        mixed.Flat.Should().BeTrue();
        mixed.Version.Should().Be("preview");

        // The same holds when the value is attached to the trailing alias of the preceding bundle.
        Parse(new GetDependencyGraphCommandLineOptions(), "get-dependency-graph", "-fv", "-npackage")
            .Version.Should().Be("-npackage");

        // An assignment with nothing behind it is an empty value, not a missing one.
        var empty = Parse(new GetDependencyGraphCommandLineOptions(), "get-dependency-graph", "-fv=");
        empty.Flat.Should().BeTrue();
        empty.Version.Should().BeEmpty();
    }

    [Test]
    public void OptionsLeftWaitingByAnEmptyAssignmentStillClaimTheNextToken()
    {
        // '--name=' drops its empty remainder, so --name is still waiting for a value and takes the
        // token behind it literally, exactly as the parser did before the arity was relaxed.
        foreach (var dangling in new[] { "--name=", "--name:", "-n=", "-n:" })
        {
            Parse(new GetDependenciesCommandLineOptions(), "get-dependencies", dangling, "-ppackage")
                .Name.Should().Be("-ppackage", because: $"'{dangling}' assigns no value");
        }

        // Only an exact name or alias may be completed this way; a bundle keeps its explicit empty value.
        var bundled = Parse(new GetDependencyGraphCommandLineOptions(), "get-dependency-graph", "-fv=", "-psecret");
        bundled.Flat.Should().BeTrue();
        bundled.Version.Should().BeEmpty();
        bundled.BuildAssetRegistryToken.Should().Be("secret");
    }

    [Test]
    public void MissingValuesAreReportedUnderTheAliasThatWasUsed()
    {
        ParseResultOf(new GetDependencyGraphCommandLineOptions(), "get-dependency-graph", "-v")
            .Errors.Should().ContainSingle()
            .Which.Message.Should().Be("Required argument missing for option: '-v'.");

        ParseResultOf(new GetDependencyGraphCommandLineOptions(), "get-dependency-graph", "--version")
            .Errors.Should().ContainSingle()
            .Which.Message.Should().Be("Required argument missing for option: '--version'.");
    }

    [Test]
    public void OptionsWithDefaultsStayValidWhenOmitted()
    {
        // Options carrying a default produce an implicit result with no tokens, which must not be
        // mistaken for the user passing the option without a value.
        var options = Parse(new GetAssetCommandLineOptions(), "get-asset", "--name", "some.package");

        options.MaxAgeInDays.Should().Be(30);
        options.GitLocation.Should().Be("git");
    }

    [Test]
    public void EnumOptionsReportAMissingValueInsteadOfThrowing()
    {
        ParseResultOf(new GetChannelsCommandLineOptions(), "get-channels", "--output-format")
            .Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("--output-format");
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
