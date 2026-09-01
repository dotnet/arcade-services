// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class Subscription
    {
        public Subscription(
            Guid id,
            bool enabled,
            bool sourceEnabled,
            string sourceRepository,
            string targetRepository,
            string targetBranch,
            string sourceDirectory,
            string targetDirectory,
            string pullRequestFailureNotificationTags,
            List<string> excludedAssets,
            bool autoApprove = false)
            : this(
                id: id,
                mergePrs: false,
                enabled: enabled,
                sourceEnabled: sourceEnabled,
                autoApprove: autoApprove,
                sourceRepository: sourceRepository,
                targetRepository: targetRepository,
                targetBranch: targetBranch,
                ignoredChecks: new List<string>(),
                sourceDirectory: sourceDirectory,
                targetDirectory: targetDirectory,
                pullRequestFailureNotificationTags: pullRequestFailureNotificationTags,
                excludedAssets: excludedAssets)
        {
        }

        public bool IsBackflow() => SourceEnabled && !string.IsNullOrEmpty(SourceDirectory);
        public bool IsForwardFlow() => SourceEnabled && !string.IsNullOrEmpty(TargetDirectory);
    }
}
