// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace ProductConstructionService.DependencyFlow.StateModel;

public class ActorId
{
    public string Id { get; }

    public ActorId(string id)
    {
        Id = id;
    }

    public override string ToString() => Id.ToString();
}

public class NonBatchedPullRequestActorId : PullRequestActorId
{
    public Guid SubscriptionId { get; }

    public NonBatchedPullRequestActorId(Guid subscriptionId)
        : base(subscriptionId.ToString())
    {
        SubscriptionId = subscriptionId;
    }
}

public class BatchedPullRequestActorId : PullRequestActorId
{
    public string Repository { get; }
    public string Branch { get; }

    /// <summary>
    ///     Creates an <see cref="ActorId" /> identifying the PullRequestActor responsible for pull requests for all batched
    ///     subscriptions
    ///     targeting the (<see paramref="repository" />, <see paramref="branch" />) pair.
    /// </summary>
    public BatchedPullRequestActorId(string repository, string branch)
        : base(Encode(repository) + ":" + Encode(branch))
    {
        Repository = repository;
        Branch = branch;
    }
}

public abstract class PullRequestActorId : ActorId
{
    protected PullRequestActorId(string id)
        : base(id)
    {
    }

    /// <summary>
    ///     Parses an <see cref="ActorId" /> created by <see cref="Create(string, string)" /> into the (repository, branch)
    ///     pair that created it.
    /// </summary>
    public static PullRequestActorId Parse(string id)
    {
        if (Guid.TryParse(id, out var guid))
        {
            return new NonBatchedPullRequestActorId(guid);
        }

        var colonIndex = id.IndexOf(":", StringComparison.Ordinal);
        if (colonIndex == -1)
        {
            throw new ArgumentException("Actor id not in correct format", nameof(id));
        }

        var repository = Decode(id.Substring(0, colonIndex));
        var branch = Decode(id.Substring(colonIndex + 1));
        return new BatchedPullRequestActorId(repository, branch);
    }

    protected static string Encode(string repository)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(repository));
    }

    protected static string Decode(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}

public enum ActorIdKind
{
    Guid,
    String
}
