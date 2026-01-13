// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Maestro.Data.Models;

namespace ProductConstructionService.DependencyFlow.Model;

public class UpdaterId
{
    public string Id { get; }

    public UpdaterId(string id)
    {
        Id = id;
    }

    public override string ToString() => Id.ToString();

    public override bool Equals(object? obj) => Id.Equals((obj as UpdaterId)?.Id);

    public override int GetHashCode() => Id.GetHashCode();
}

public class NonBatchedPullRequestUpdaterId : PullRequestUpdaterId
{
    public Guid SubscriptionId { get; }

    public NonBatchedPullRequestUpdaterId(Guid subscriptionId)
        : base(subscriptionId.ToString())
    {
        SubscriptionId = subscriptionId;
    }
}

public class BatchedPullRequestUpdaterId : PullRequestUpdaterId
{
    public string Repository { get; }
    public string Branch { get; }

    public BatchedPullRequestUpdaterId(string repository, string branch)
        : base(Encode(repository) + ":" + Encode(branch))
    {
        Repository = repository;
        Branch = branch;
    }
}

public abstract class PullRequestUpdaterId : UpdaterId
{
    protected PullRequestUpdaterId(string id)
        : base(id)
    {
    }

    /// <summary>
    ///     Parses an <see cref="UpdaterId" /> created by <see cref="Create(string, string)" /> into the (repository, branch)
    ///     pair that created it.
    /// </summary>
    public static PullRequestUpdaterId Parse(string id)
    {
        if (Guid.TryParse(id, out var guid))
        {
            return new NonBatchedPullRequestUpdaterId(guid);
        }

        var colonIndex = id.IndexOf(':');
        if (colonIndex == -1)
        {
            throw new ArgumentException("Updater ID not in correct format", nameof(id));
        }

        var repository = Decode(id.Substring(0, colonIndex));
        var branch = Decode(id.Substring(colonIndex + 1));
        return new BatchedPullRequestUpdaterId(repository, branch);
    }

    protected static string Encode(string repository)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(repository));
    }

    protected static string Decode(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    public static PullRequestUpdaterId CreateUpdaterId(Subscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        return subscription.PolicyObject.Batchable
            ? new BatchedPullRequestUpdaterId(subscription.TargetRepository, subscription.TargetBranch)
            : new NonBatchedPullRequestUpdaterId(subscription.Id);
    }
}
