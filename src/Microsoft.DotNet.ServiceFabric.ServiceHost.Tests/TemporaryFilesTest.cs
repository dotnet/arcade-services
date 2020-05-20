using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    public class TemporaryFilesTest : IDisposable
    {
        public TemporaryFilesTest()
        {
            CleanTempFolderForTest();
        }

        public void Dispose()
        {
            CleanTempFolderForTest();
        }

        private static void CleanTempFolderForTest()
        {
            using (var tempFiles = new TemporaryFiles(MockBuilder.MockServiceContext().Object,
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


        [Fact]
        public void CleanupResilientToOpenHandles()
        {
            StreamWriter writer = null;
            try
            {
                string parent;
                using (var tempFiles = new TemporaryFiles(MockBuilder.MockServiceContext().Object,
                    NullLogger<TemporaryFiles>.Instance))
                {
                    tempFiles.Initialize();
                    string testPath = tempFiles.GetFilePath("asdfpoiu");
                    writer = File.CreateText(testPath);
                    parent = Path.GetDirectoryName(testPath);
                }

                Assert.True(Directory.Exists(parent));
            }
            finally
            {
                writer?.Dispose();
            }
        }

        [Fact]
        public void DisposeCleansUp()
        {
            string parent;
            using (var tempFiles = new TemporaryFiles(MockBuilder.MockServiceContext().Object,
                NullLogger<TemporaryFiles>.Instance))
            {
                tempFiles.Initialize();
                string testPath = tempFiles.GetFilePath("asdfpoiu");
                File.WriteAllText(testPath, "Test content");
                parent = Path.GetDirectoryName(testPath);
            }

            Assert.False(Directory.Exists(parent));
        }

        [Fact]
        public void InitializeCreatedRoot()
        {
            using (var tempFiles = new TemporaryFiles(MockBuilder.MockServiceContext().Object,
                NullLogger<TemporaryFiles>.Instance))
            {
                tempFiles.Initialize();
                string testPath = tempFiles.GetFilePath("asdfpoiu");
                string parent = Path.GetDirectoryName(testPath);
                Assert.True(Directory.Exists(parent));
            }
        }

        [Fact]
        public void PreInitializeIsNoop()
        {
            using (var tempFiles = new TemporaryFiles(MockBuilder.MockServiceContext().Object,
                NullLogger<TemporaryFiles>.Instance))
            {
                string testPath = tempFiles.GetFilePath("asdfpoiu");
                string parent = Path.GetDirectoryName(testPath);
                Assert.False(Directory.Exists(parent));
            }
        }

        [Fact]
        public void SecondCleanupFixesBrokenFirstCleanup()
        {
            StreamWriter writer = null;
            string parent;
            try
            {
                using (var tempFiles = new TemporaryFiles(MockBuilder.MockServiceContext().Object,
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

            using (var tempFiles = new TemporaryFiles(MockBuilder.MockServiceContext().Object,
                NullLogger<TemporaryFiles>.Instance))
            {
                tempFiles.Initialize();
                string testPath = tempFiles.GetFilePath("asdfpoiu");
                parent = Path.GetDirectoryName(testPath);
            }

            Assert.False(Directory.Exists(parent));
        }
    }
}
