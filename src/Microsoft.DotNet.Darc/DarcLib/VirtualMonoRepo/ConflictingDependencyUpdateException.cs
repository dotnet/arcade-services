// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class ConflictingDependencyUpdateException : Exception
{
    public ConflictingDependencyUpdateException(
            VersionFileProperty repoUpdate,
            VersionFileProperty vmrUpdate)
        : base(ConstructMessage(repoUpdate, vmrUpdate))
    {
    }

    private static string ConstructMessage(VersionFileProperty repoUpdate, VersionFileProperty vmrUpdate)
    {
        var message = new StringBuilder();
        message.AppendLine($"Conflicting updates of asset {repoUpdate.Name} found between a repo and a VMR");
        message.Append("Repo: ");
        message.AppendLine(ConstructMessageForUpdate(repoUpdate));
        message.Append("VMR: ");
        message.AppendLine(ConstructMessageForUpdate(vmrUpdate));
        return message.ToString();
    }

    private static string ConstructMessageForUpdate(VersionFileProperty update)
    {
        if (update.IsRemoved())
        {
            return "removed";
        }

        if (update.IsAdded())
        {
            return "added";
        }

        return $"updated to {update.Value}";
    }
}
