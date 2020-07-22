using System;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    [TestFixture, NonParallelizable]
    public class TemporaryFilesTest : IDisposable
    {
        [SetUp]        public void TemporaryFilesTest_SetUp()
        {
            CleanTempFolderForTest();
        }

        [TearDown]
        public void Dispose()
        {
            CleanTempFolderForTest();
        }

        private static void CleanTempFolderForTest()
        {
            using (var tempFiles = new TemporaryFiles(MockBuilder.StatelessServiceContext(),
                NullLogger<TemporaryFiles>.Instance))
            {
                tempFiles.Initialize();
                string testPath = tempFiles.GetFilePath("asdfpoiu");
                string parent = Path.GetDirectoryName(testPath);
                if (Directory.Exists(parent))
                {
                    Directory.Delete(parent);
                }
            }
        }


        [Test]
        public void CleanupResilientToOpenHandles()
        {
            StreamWriter writer = null;
            try
            {
                string parent;
                using (var tempFiles = new TemporaryFiles(MockBuilder.StatelessServiceContext(),
                    NullLogger<TemporaryFiles>.Instance))
                {
                    tempFiles.Initialize();
                    string testPath = tempFiles.GetFilePath("asdfpoiu");
                    writer = File.CreateText(testPath);
                    parent = Path.GetDirectoryName(testPath);
                }

                Directory.Exists(parent).Should().BeTrue();
            }
            finally
            {
                writer?.Dispose();
            }
        }

        [Test]
        public void DisposeCleansUp()
        {
            string parent;
            using (var tempFiles = new TemporaryFiles(MockBuilder.StatelessServiceContext(),
                NullLogger<TemporaryFiles>.Instance))
            {
                tempFiles.Initialize();
                string testPath = tempFiles.GetFilePath("asdfpoiu");
                File.WriteAllText(testPath, "Test content");
                parent = Path.GetDirectoryName(testPath);
            }

            Directory.Exists(parent).Should().BeFalse();
        }

        [Test]
        public void InitializeCreatedRoot()
        {
            using (var tempFiles = new TemporaryFiles(MockBuilder.StatelessServiceContext(),
                NullLogger<TemporaryFiles>.Instance))
            {
                tempFiles.Initialize();
                string testPath = tempFiles.GetFilePath("asdfpoiu");
                string parent = Path.GetDirectoryName(testPath);
                Directory.Exists(parent).Should().BeTrue();
            }
        }

        [Test]
        public void PreInitializeIsNoop()
        {
            using (var tempFiles = new TemporaryFiles(MockBuilder.StatelessServiceContext(),
                NullLogger<TemporaryFiles>.Instance))
            {
                string testPath = tempFiles.GetFilePath("asdfpoiu");
                string parent = Path.GetDirectoryName(testPath);
                Directory.Exists(parent).Should().BeFalse();
            }
        }

        [Test]
        public void SecondCleanupFixesBrokenFirstCleanup()
        {
            StreamWriter writer = null;
            string parent;
            try
            {
                using (var tempFiles = new TemporaryFiles(MockBuilder.StatelessServiceContext(),
                    NullLogger<TemporaryFiles>.Instance))
                {
                    tempFiles.Initialize();
                    string testPath = tempFiles.GetFilePath("asdfpoiu");
                    writer = File.CreateText(testPath);
                }
            }
            finally
            {
                writer?.Dispose();
            }

            using (var tempFiles = new TemporaryFiles(MockBuilder.StatelessServiceContext(),
                NullLogger<TemporaryFiles>.Instance))
            {
                tempFiles.Initialize();
                string testPath = tempFiles.GetFilePath("asdfpoiu");
                parent = Path.GetDirectoryName(testPath);
            }

            Directory.Exists(parent).Should().BeFalse();
        }
    }
}
