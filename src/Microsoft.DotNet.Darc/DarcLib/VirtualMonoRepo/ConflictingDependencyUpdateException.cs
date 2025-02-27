// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;
using Microsoft.DotNet.DarcLib.Models.Darc;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class ConflictingDependencyUpdateException : Exception
{
    public ConflictingDependencyUpdateException(
            DependencyUpdate repoUpdate,
            DependencyUpdate vmrUpdate)
        : base(ConstructMessage(repoUpdate, vmrUpdate))
    {
    }

    private static string ConstructMessage(DependencyUpdate repoUpdate, DependencyUpdate vmrUpdate)
    {
        var message = new StringBuilder();
        message.AppendLine($"Conflicting updates of asset {repoUpdate.From?.Name ?? repoUpdate.To?.Name} found between a repo and a VMR");
        message.Append("Repo: ");
        message.AppendLine(ConstructMessageForUpdate(repoUpdate));
        message.Append("VMR: ");
        message.AppendLine(ConstructMessageForUpdate(vmrUpdate));
        return message.ToString();
    }

    private static string ConstructMessageForUpdate(DependencyUpdate update)
    {
        if (update.To == null)
        {
            return "removed";
        }

        if (update.From == null)
        {
            return "added";
        }

        return $"updated to {update.To.Version}";
    }
}
