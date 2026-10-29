// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Maestro.Common;
using Microsoft.DotNet.Internal.Credentials;
using Microsoft.DotNet.Internal.AzureDevOps.Authentication;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Microsoft.DotNet.Darc.Options;

public abstract class CommandLineOptions<T> : CommandLineOptions where T : Operation
{
    public override Operation GetOperation(ServiceProvider sp)
    {
        return ActivatorUtilities.CreateInstance<T>(sp, this);
    }
}

public abstract class CommandLineOptions : ICommandLineOptions
{
    private readonly List<CliSymbol> _symbols = [];
    private readonly List<Action<CommandResult>> _validators = [];
    private bool _configured;

    [RedactFromLogging]
    public string BuildAssetRegistryToken { get; set; } = null;

    [RedactFromLogging]
    public string GitHubPat { get; set; }

    [RedactFromLogging]
    public string AzureDevOpsPat { get; set; }

    public string BuildAssetRegistryBaseUri { get; set; }

    public bool Verbose { get; set; }

    public bool Debug { get; set; }

    [RedactFromLogging]
    public string GitLocation { get; set; }

    public DarcOutputType OutputFormat
    {
        get;
        set
        {
            field = value;
            if (!IsOutputFormatSupported())
            {
                throw new ArgumentException($"Output format {field} is not supported by operation ${GetType().Name}");
            }
        }
    }

    /// <summary>
    /// Designates that darc is run from a CI environment.
    /// Some behaviours are disabled in CI such as the interactive browser sign-in to Maestro (and AzureCLI/federated token is used).
    /// </summary>
    public bool IsCi { get; set; }

    /// <summary>
    /// Name of the command as typed on the command line, e.g. "add-channel".
    /// </summary>
    public abstract string Verb { get; }

    /// <summary>
    /// Help text shown for the command in the command list and in its own help output.
    /// </summary>
    public abstract string VerbDescription { get; }

    /// <summary>
    /// Options and positional arguments the command accepts, in declaration order.
    /// </summary>
    internal IReadOnlyList<CliSymbol> Symbols
    {
        get
        {
            EnsureConfigured();
            return _symbols;
        }
    }

    /// <summary>
    /// Checks run against the parsed command before the operation is created.
    /// </summary>
    internal IReadOnlyList<Action<CommandResult>> Validators
    {
        get
        {
            EnsureConfigured();
            return _validators;
        }
    }

    /// <summary>
    /// Copies every parsed value onto this instance.
    /// </summary>
    internal void Bind(ParseResult parseResult)
    {
        EnsureConfigured();
        foreach (var symbol in _symbols)
        {
            symbol.Bind(parseResult);
        }
    }

    /// <summary>
    /// Declares the options of this command. Overrides must call the base implementation last so that
    /// the command's own options are listed before the ones shared by every darc command, the same
    /// order darc has always printed them in.
    /// </summary>
    protected virtual void Configure()
    {
        Add<string>("--password", "-p", "Token used to authenticate to BAR. When omitted, Azure CLI or an interactive browser login flow are used.", value => BuildAssetRegistryToken = value);
        Add<string>("--github-pat", "Token used to authenticate GitHub.", value => GitHubPat = value);
        Add<string>("--azdev-pat", "Optional token used to authenticate to Azure DevOps. When not provided, local credentials are used.", value => AzureDevOpsPat = value);
        Add<string>("--bar-uri", "URI of the build asset registry service to use.", value => BuildAssetRegistryBaseUri = value);
        Add<bool>("--verbose", "Turn on verbose output.", value => Verbose = value);
        Add<bool>("--debug", "Turn on debug output.", value => Debug = value);
        Add<string>("--git-location", "Location of git executable used for internal commands.", value => GitLocation = value, defaultValue: () => "git");
        AddEnum("--output-format", "Desired output type of darc. Valid values are 'json' and 'text'. Case sensitive.", DarcOutputType.text, value => OutputFormat = value);
        Add<bool>("--ci", "Designates that darc is run from a CI environment with some features disabled (e.g. interactive browser sign-in to Maestro)", value => IsCi = value);
    }

    /// <summary>
    /// Declares an option of this command and how its parsed value reaches the options instance.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TValue"/> has to be given explicitly because it cannot be inferred from
    /// the setter lambda alone.
    /// </remarks>
    protected Option<TValue> Add<TValue>(
        string name,
        string description,
        Action<TValue> setter,
        bool required = false,
        Func<TValue> defaultValue = null,
        [CallerArgumentExpression(nameof(setter))] string bindingExpression = null)
        => Register(CreateOption(name, alias: null, description, required, defaultValue), setter, separator: null, bindingExpression);

    /// <param name="alias">Short form of the option, e.g. <c>-n</c>.</param>
    /// <inheritdoc cref="Add{TValue}(string, string, Action{TValue}, bool, Func{TValue}, string)"/>
    protected Option<TValue> Add<TValue>(
        string name,
        string alias,
        string description,
        Action<TValue> setter,
        bool required = false,
        Func<TValue> defaultValue = null,
        [CallerArgumentExpression(nameof(setter))] string bindingExpression = null)
        => Register(CreateOption(name, alias, description, required, defaultValue), setter, separator: null, bindingExpression);

    /// <summary>
    /// Declares an option that may be repeated or given several space-separated values.
    /// </summary>
    /// <inheritdoc cref="CliOptions.List(string, string, string[])" path="/remarks"/>
    protected Option<string[]> AddList(
        string name,
        string description,
        Action<string[]> setter,
        string alias = null,
        [CallerArgumentExpression(nameof(setter))] string bindingExpression = null)
        => Register(CliOptions.List(name, description, Aliases(alias)), setter, separator: null, bindingExpression);

    /// <summary>
    /// Declares an option whose values may additionally be packed into a single separated token,
    /// e.g. <c>--ids 1,2,3</c>.
    /// </summary>
    /// <inheritdoc cref="CliOptions.List(string, string, string[])" path="/remarks"/>
    protected Option<string[]> AddSeparatedList(
        char separator,
        string name,
        string description,
        Action<string[]> setter,
        Func<string[]> defaultValue = null,
        string alias = null,
        [CallerArgumentExpression(nameof(setter))] string bindingExpression = null)
    {
        Option<string[]> option = CliOptions.SeparatedList(separator, name, description, Aliases(alias));
        if (defaultValue is not null)
        {
            option.DefaultValueFactory = _ => defaultValue();
        }

        return Register(option, setter, separator, bindingExpression);
    }

    /// <inheritdoc cref="AddSeparatedList(char, string, string, Action{string[]}, Func{string[]}, string)"/>
    protected Option<int[]> AddSeparatedIntList(
        char separator,
        string name,
        string description,
        Action<int[]> setter,
        string alias = null,
        [CallerArgumentExpression(nameof(setter))] string bindingExpression = null)
        => Register(CliOptions.SeparatedIntList(separator, name, description, Aliases(alias)), setter, separator, bindingExpression);

    /// <summary>
    /// Declares an option that only accepts the exact casing of the enum members.
    /// </summary>
    protected Option<TEnum> AddEnum<TEnum>(
        string name,
        string description,
        TEnum defaultValue,
        Action<TEnum> setter,
        [CallerArgumentExpression(nameof(setter))] string bindingExpression = null)
        where TEnum : struct, Enum
        => Register(CliOptions.CaseSensitiveEnum(name, description, defaultValue), setter, separator: null, bindingExpression);

    /// <summary>
    /// Declares a positional argument taking a single value.
    /// </summary>
    protected Argument<string> AddValue(
        string name,
        string description,
        bool required,
        Action<string> setter,
        [CallerArgumentExpression(nameof(setter))] string bindingExpression = null)
        => Register(CliOptions.Value(name, description, required), setter, bindingExpression);

    /// <summary>
    /// Declares a positional argument taking any number of values.
    /// </summary>
    /// <inheritdoc cref="CliOptions.List(string, string, string[])" path="/remarks"/>
    protected Argument<string[]> AddValueList(
        string name,
        string description,
        bool required,
        Action<string[]> setter,
        [CallerArgumentExpression(nameof(setter))] string bindingExpression = null)
        => Register(CliOptions.ValueList(name, description, required), setter, bindingExpression);

    private static string[] Aliases(string alias) => alias is null ? [] : [alias];

    private static Option<TValue> CreateOption<TValue>(
        string name,
        string alias,
        string description,
        bool required,
        Func<TValue> defaultValue)
    {
        Option<TValue> option = alias is null ? new(name) : new(name, alias);
        option.Description = description;
        option.Required = required;
        if (defaultValue is not null)
        {
            option.DefaultValueFactory = _ => defaultValue();
        }

        switch (option)
        {
            case Option<string> text:
                text.CustomParser = CliOptions.SingleValue;
                break;
        }

        return option;
    }

    /// <summary>
    /// Records an option and how its parsed value reaches the options instance.
    /// </summary>
    private Option<TValue> Register<TValue>(
        Option<TValue> option,
        Action<TValue> setter,
        char? separator,
        string bindingExpression)
    {
        // A derived options class may redeclare an option of its base class (e.g. 'login' narrows
        // --bar-uri). Since a command declares its own options before calling base.Configure(),
        // keeping the first declaration reproduces C# member hiding.
        if (_symbols.Any(symbol => symbol.Symbol is Option declared && Collides(declared, option)))
        {
            return option;
        }

        _symbols.Add(new CliSymbol(
            option,
            separator,
            option.Required,
            bindingExpression,
            parseResult =>
            {
                // Only options the user supplied (or that declare a default) are written back, so
                // that properties keeping their field initializer are left alone.
                if (parseResult.GetResult(option) is not null)
                {
                    setter(parseResult.GetValue(option));
                }
            }));

        return option;
    }

    /// <summary>
    /// Records a positional argument and how its parsed value reaches the options instance.
    /// </summary>
    private Argument<TValue> Register<TValue>(
        Argument<TValue> argument,
        Action<TValue> setter,
        string bindingExpression)
    {
        _symbols.Add(new CliSymbol(
            argument,
            null,
            argument.Arity.MinimumNumberOfValues > 0,
            bindingExpression,
            parseResult =>
            {
                var result = parseResult.GetResult(argument);
                if (result is not null && (result.Tokens.Count > 0 || argument.HasDefaultValue))
                {
                    setter(parseResult.GetValue(argument));
                }
            }));

        return argument;
    }

    /// <summary>
    /// Declares that two options cannot be supplied together.
    /// </summary>
    protected void AddMutuallyExclusive(Option first, Option second)
        => _validators.Add(result =>
        {
            if (result.GetResult(first) is not null && result.GetResult(second) is not null)
            {
                result.AddError($"Options '{first.Name}' and '{second.Name}' cannot be used together.");
            }
        });

    private static bool Collides(Option first, Option second)
    {
        if (first.Name == second.Name)
        {
            return true;
        }

        if (first.Aliases.Contains(second.Name)
            || second.Aliases.Contains(first.Name)
            || first.Aliases.Any(second.Aliases.Contains))
        {
            throw new InvalidOperationException(
                $"Options '{first.Name}' and '{second.Name}' share an alias. Only an option redeclared "
                + "under the same name may hide the one it inherits.");
        }

        return false;
    }

    private void EnsureConfigured()
    {
        if (!_configured)
        {
            Configure();
            _configured = true;
        }
    }

    /// <summary>
    /// Default log verbosity
    /// </summary>
    protected virtual LogLevel DefaultLogVerbosity => LogLevel.Warning;

    public abstract Operation GetOperation(ServiceProvider sp);

    public void InitializeFromSettings(ILogger logger)
    {
        var localSettings = LocalSettings.GetSettings(this, logger);
        AzureDevOpsPat ??= localSettings.AzureDevOpsToken;
        GitHubPat ??= localSettings.GitHubToken;
        BuildAssetRegistryBaseUri ??= localSettings.BuildAssetRegistryBaseUri;
    }

    /// <summary>
    ///  Indicates whether the requested output format is supported.
    /// </summary>
    public virtual bool IsOutputFormatSupported()
        => OutputFormat switch
        {
            DarcOutputType.text => true,
            _ => false
        };

    public virtual IServiceCollection RegisterServices(IServiceCollection services)
    {
        // Because the internal logging in DarcLib tends to be chatty and non-useful,
        // we remap the --verbose switch onto 'info', --debug onto highest level, and the
        // default level onto warning
        LogLevel level;
        if (Debug)
        {
            level = LogLevel.Debug;
        }
        else if (Verbose)
        {
            level = LogLevel.Information;
        }
        else
        {
            level = DefaultLogVerbosity;
        }

        services ??= new ServiceCollection();
        services.AddLogging(b => b
            .AddConsole(o => o.FormatterName = CompactConsoleLoggerFormatter.FormatterName)
            .AddConsoleFormatter<CompactConsoleLoggerFormatter, SimpleConsoleFormatterOptions>()
            .SetMinimumLevel(level)
            .AddFilter("Microsoft.DotNet.DarcLib.VirtualMonoRepo." + nameof(VmrPatchHandler), LogLevel.Warning));

        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton<IRemoteFactory, RemoteFactory>();
        services.TryAddSingleton<ILocalGitRepoFactory, LocalGitRepoFactory>();
        services.TryAddTransient<ILocalFactory, LocalFactory>();
        services.TryAddTransient<IDependencyFileManagerFactory, DependencyFileManagerFactory>();
        services.TryAddTransient<ILocalGitClient, LocalGitClient>();
        services.TryAddSingleton<IVersionDetailsParser, VersionDetailsParser>();
        services.TryAddSingleton<IAssetLocationResolver, AssetLocationResolver>();
        services.TryAddTransient<IProcessManager>(sp => new ProcessManager(sp.GetRequiredService<ILogger<ProcessManager>>(), GitLocation));
        RegisterPcsClient(services);
        services.TryAddSingleton<IBarApiClient>(sp => new BarApiClient(
            sp.GetRequiredService<IProductConstructionServiceApi>()));
        services.TryAddSingleton<IBasicBarClient>(sp => sp.GetRequiredService<IBarApiClient>());
        services.TryAddTransient<ICoherencyUpdateResolver, CoherencyUpdateResolver>();
        services.TryAddTransient<ILogger>(sp => sp.GetRequiredService<ILogger<Operation>>());
        services.TryAddTransient<ITelemetryRecorder, NoTelemetryRecorder>();
        services.TryAddTransient<IGitRepoFactory>(sp => ActivatorUtilities.CreateInstance<GitRepoFactory>(sp, Path.GetTempPath()));
        services.Configure<AzureDevOpsTokenProviderOptions>(o =>
        {
            o["default"] = new AzureDevOpsCredentialResolverOptions
            {
                Token = AzureDevOpsPat,
                DisableInteractiveAuth = IsCi,
            };
        });
        services.TryAddSingleton<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();
        services.TryAddSingleton<IAzureDevOpsClient, AzureDevOpsClient>();
        services.TryAddSingleton<IRemoteTokenProvider>(s => new RemoteTokenProvider(
            s.GetRequiredKeyedService<IRemoteTokenProvider>("azdo"),
            s.GetRequiredKeyedService<IRemoteTokenProvider>("github")));
        services.TryAddKeyedSingleton<IRemoteTokenProvider>("github", (s, _) => s.GetRequiredService<GitHubCliTokenProvider>());
        services.TryAddKeyedSingleton<IRemoteTokenProvider>("azdo", (s, _) =>
        {
            var azdoOptions = new AzureDevOpsTokenProviderOptions
            {
                ["default"] = new AzureDevOpsCredentialResolverOptions
                {
                    Token = AzureDevOpsPat,
                    DisableInteractiveAuth = IsCi,
                }
            };
            return AzureDevOpsTokenProvider.FromStaticOptions(azdoOptions);
        });
        services.TryAddSingleton(s => new GitHubCliTokenProvider(
            s.GetRequiredService<IProcessManager>(),
            s.GetRequiredService<ILogger<GitHubCliTokenProvider>>(),
            GitHubPat));
        services.TryAddSingleton<ICommandLineOptions>(_ => this);
        // Add add an empty VmrInfo that won't actually be used in non VMR commands
        services.TryAddSingleton<ISourceMappingParser, SourceMappingParser>();
        services.TryAddSingleton<IVmrInfo>(sp =>
        {
            return new VmrInfo(string.Empty, string.Empty);
        });
        services.TryAddSingleton<ICache, MemoryCache>();
        services.TryAddScoped<ICommentCollector, CommentCollector>();

        return services;
    }

    private void RegisterPcsClient(IServiceCollection sp) =>
        sp.TryAddSingleton(sp =>
            !string.IsNullOrEmpty(BuildAssetRegistryBaseUri)
                ? PcsApiFactory.GetAuthenticated(BuildAssetRegistryBaseUri, BuildAssetRegistryToken, managedIdentityId: null, IsCi, sp.GetRequiredService<ILoggerFactory>())
                : PcsApiFactory.GetAuthenticated(BuildAssetRegistryToken, managedIdentityId: null, IsCi, sp.GetRequiredService<ILoggerFactory>()));
}
