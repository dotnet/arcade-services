// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Internal.Helix.Utility.Azure;
using NUnit.Framework;

namespace Helix.Utility.Azure.Tests
{
    public class CloseOnDisposeTests
    {
        public class Markable
        {
            public int MarkCount { get; set; }
            public bool IsMarked => MarkCount == 1;
            public void Mark() => MarkCount++;
        }
        
        public class ValueTaskClose : Markable
        {
            public ValueTask CloseAsync()
            {
                Mark();
                return new ValueTask();
            }
        }
        public class TaskClose : Markable
        {
            public Task CloseAsync()
            {
                Mark();
                return Task.CompletedTask;
            }
        }
        public class SyncClose : Markable
        {
            public void Close()
            {
                Mark();
            }
        }
        
        [Test]
        public async Task SyncAsync()
        {
            var item = new SyncClose();
            var wrapped = CloseOnDispose.Wrap(item);
            await wrapped.DisposeAsync();
            item.IsMarked.Should().BeTrue();
        }

        [Test]
        public async Task ValueTaskAsync()
        {
            var item = new ValueTaskClose();
            var wrapped = CloseOnDispose.Wrap(item);
            await wrapped.DisposeAsync();
            item.IsMarked.Should().BeTrue();
        }

        [Test]
        public async Task TaskAsync()
        {
            var item = new TaskClose();
            var wrapped = CloseOnDispose.Wrap(item);
            await wrapped.DisposeAsync();
            item.IsMarked.Should().BeTrue();
        }
        
        [Test]
        public void SyncSync()
        {
            var item = new SyncClose();
            var wrapped = CloseOnDispose.Wrap(item);
            wrapped.Dispose();
            item.IsMarked.Should().BeTrue();
        }

        [Test]
        public void ValueTaskSync()
        {
            var item = new ValueTaskClose();
            var wrapped = CloseOnDispose.Wrap(item);
            wrapped.Dispose();
            item.IsMarked.Should().BeTrue();
        }

        [Test]
        public void TaskIsSync()
        {
            var item = new TaskClose();
            var wrapped = CloseOnDispose.Wrap(item);
            wrapped.Dispose();
            item.IsMarked.Should().BeTrue();
        }
    }
}
