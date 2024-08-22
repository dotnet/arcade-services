// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using StackExchange.Redis;

namespace ProductConstructionService.Common;

public interface IStateManagerFactory
{
    public IStateManager CreateCodeFlowStateManager(string actorId);
    public IStateManager CreateInProgressPrStateManager(string actorId);
    public IStateManager CreateUpdateAssetsStateManager(string actorId);
}

public class StateManagerFactory: IStateManagerFactory
{
    private static readonly string CODEFLOW_PREFIX = "CodeFlow";
    private static readonly string IN_PROGRESS_PR_PREFIX = "InProgressPullRequest";
    private static readonly string UPDATE_ASSETS_PARAMETERS_PREFIX = "UpdateAssetsParameters";

    // State managers should store and re-use `ConnectionMultiplexer`, so we
    // only use one instance for every state manager we create
    // https://stackexchange.github.io/StackExchange.Redis/Basics
    private readonly IConnectionMultiplexer _connection;

    public StateManagerFactory(ConfigurationOptions options)
    {
        _connection = ConnectionMultiplexer.Connect(options);
    }

    public IStateManager CreateCodeFlowStateManager(string actorId)
    {
        return new StateManager(_connection, $"{CODEFLOW_PREFIX}_{actorId}");
    }

    public IStateManager CreateInProgressPrStateManager(string actorId)
    {
        return new StateManager(_connection, $"{IN_PROGRESS_PR_PREFIX}_{actorId}");
    }

    public IStateManager CreateUpdateAssetsStateManager(string actorId)
    {
        return new StateManager(_connection, $"{UPDATE_ASSETS_PARAMETERS_PREFIX}_{actorId}");
    }
}
