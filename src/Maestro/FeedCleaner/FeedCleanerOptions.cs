// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace FeedCleaner
{
    public class FeedCleanerOptions
    {
        public bool Enabled { get; set; } = false;

        public List<(string account, string project, string name)> ReleasePackageFeeds = new List<(string account, string project, string name)>();

        public List<string> AzdoAccounts = new List<string>();
    }
}
