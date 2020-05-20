using System;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Moq;
using Xunit;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    public class AsyncInterceptorTests
    {
        [Fact]
        public async Task TestIntArg()
        {
            await TestInterception((impl, target) =>
            {
                Assert.Equal(Inter.IntReturn, impl.IntArgs(123, "TestParam"));
                Assert.Equal(123, target.IntParam);
                Assert.Equal("TestParam", target.StringParam);
                Assert.Equal(1, target.CallCount);
                return Task.CompletedTask;
            });
        }

        [Fact]
        public async Task TestIntNoArg()
        {
            await TestInterception((impl, target) =>
            {
                Assert.Equal(Inter.IntReturn,impl.IntNoArgs());
                Assert.Equal(0, target.IntParam);
                Assert.Null(target.StringParam);
                Assert.Equal(1, target.CallCount);
                return Task.CompletedTask;
            });
        }

        [Fact]
        public async Task TestIntTaskArg()
        {
            await TestInterception(async (impl, target) =>
            {
                Assert.Equal(Inter.IntReturn, await impl.IntTaskArgs(123, "TestParam"));
                Assert.Equal(123, target.IntParam);
                Assert.Equal("TestParam", target.StringParam);
                Assert.Equal(1, target.CallCount);
            });
        }

        [Fact]
        public async Task TestIntTaskNoArg()
        {
            await TestInterception(async (impl, target) =>
            {
                Assert.Equal(Inter.IntReturn, await impl.IntTaskNoArgs());
                Assert.Equal(0, target.IntParam);
                Assert.Null(target.StringParam);
                Assert.Equal(1, target.CallCount);
            });
        }

        [Fact]
        public async Task TestStringArg()
        {
            await TestInterception((impl, target) =>
            {
                Assert.Equal(Inter.StringReturn, impl.StringArgs(123, "TestParam"));
                Assert.Equal(123, target.IntParam);
                Assert.Equal("TestParam", target.StringParam);
                Assert.Equal(1, target.CallCount);
                return Task.CompletedTask;
            });
        }

        [Fact]
        public async Task TestStringNoArg()
        {
            await TestInterception((impl, target) =>
            {
                Assert.Equal(Inter.StringReturn, impl.StringNoArgs());
                Assert.Equal(0, target.IntParam);
                Assert.Null(target.StringParam);
                Assert.Equal(1, target.CallCount);
                return Task.CompletedTask;
            });
        }

        [Fact]
        public async Task TestStringTaskArg()
        {
            await TestInterception(async (impl, target) =>
            {
                Assert.Equal(Inter.StringReturn, await impl.StringTaskArgs(123, "TestParam"));
                Assert.Equal(123, target.IntParam);
                Assert.Equal("TestParam", target.StringParam);
                Assert.Equal(1, target.CallCount);
            });
        }

        [Fact]
        public async Task TestStringTaskNoArg()
        {
            await TestInterception(async (impl, target) =>
            {
                Assert.Equal(Inter.StringReturn, await impl.StringTaskNoArgs());
                Assert.Equal(0, target.IntParam);
                Assert.Null(target.StringParam);
                Assert.Equal(1, target.CallCount);
            });
        }

        [Fact]
        public async Task TestTaskArg()
        {
            await TestInterception(async (impl, target) =>
            {
                await impl.TaskArgs(123, "TestParam");
                Assert.Equal(123, target.IntParam);
                Assert.Equal("TestParam", target.StringParam);
                Assert.Equal(1, target.CallCount);
            });
        }

        [Fact]
        public async Task TestTaskNoArg()
        {
            await TestInterception(async (impl, target) =>
            {
                await impl.TaskNoArgs();
                Assert.Equal(0, target.IntParam);
                Assert.Null(target.StringParam);
                Assert.Equal(1, target.CallCount);
            });
        }

        [Fact]
        public async Task TestVoidArgs()
        {
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                TestInterception((impl, target) =>
                {
                    impl.VoidArgs(123, "TestParam");
                    return Task.CompletedTask;
                })
            );
        }

        [Fact]
        public async Task TestVoidNoArg()
        {
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                TestInterception((impl, target) =>
                {
                    impl.VoidNoArgs();
                    return Task.CompletedTask;
                })
            );
        }

        [Fact]
        public void IntThrowIsTransparent()
        {
            var mock = new Mock<IInter>();
            mock.Setup(i => i.IntNoArgs()).Throws(new InvalidOperationException("Test exception text"));
            var gen = new ProxyGenerator();
            var impl = (IInter) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IInter),
                new Type[0],
                mock.Object,
                new TestInterceptor());

            var ex = Assert.Throws<InvalidOperationException>(() => impl.IntNoArgs());
            Assert.Equal("Test exception text", ex.Message);
        }

        [Fact]
        public void StringThrowIsTransparent()
        {
            var mock = new Mock<IInter>();
            mock.Setup(i => i.StringNoArgs()).Throws(new InvalidOperationException("Test exception text"));
            var gen = new ProxyGenerator();
            var impl = (IInter) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IInter),
                new Type[0],
                mock.Object,
                new TestInterceptor());

            var ex = Assert.Throws<InvalidOperationException>(() => impl.StringNoArgs());
            Assert.Equal("Test exception text", ex.Message);
        }

        [Fact]
        public async Task TaskImmediateThrowIsTransparent()
        {
            var mock = new Mock<IInter>();
            mock.Setup(i => i.TaskNoArgs()).Throws(new InvalidOperationException("Test exception text"));
            var gen = new ProxyGenerator();
            var impl = (IInter) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IInter),
                new Type[0],
                mock.Object,
                new TestInterceptor());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => impl.TaskNoArgs());
            Assert.Equal("Test exception text", ex.Message);
        }

        [Fact]
        public async Task TaskDelayedThrowIsTransparent()
        {
            var mock = new Mock<IInter>();
            mock.Setup(i => i.TaskNoArgs()).Returns(Task.FromException(new InvalidOperationException("Test exception text")));
            var gen = new ProxyGenerator();
            var impl = (IInter) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IInter),
                new Type[0],
                mock.Object,
                new TestInterceptor());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => impl.TaskNoArgs());
            Assert.Equal("Test exception text", ex.Message);
        }

        [Fact]
        public async Task IntTaskImmediateThrowIsTransparent()
        {
            var mock = new Mock<IInter>();
            mock.Setup(i => i.IntTaskNoArgs()).Throws(new InvalidOperationException("Test exception text"));
            var gen = new ProxyGenerator();
            var impl = (IInter) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IInter),
                new Type[0],
                mock.Object,
                new TestInterceptor());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => impl.IntTaskNoArgs());
            Assert.Equal("Test exception text", ex.Message);
        }

        [Fact]
        public async Task IntTaskDelayedThrowIsTransparent()
        {
            var mock = new Mock<IInter>();
            mock.Setup(i => i.IntTaskNoArgs()).Returns(Task.FromException<int>(new InvalidOperationException("Test exception text")));
            var gen = new ProxyGenerator();
            var impl = (IInter) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IInter),
                new Type[0],
                mock.Object,
                new TestInterceptor());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => impl.IntTaskNoArgs());
            Assert.Equal("Test exception text", ex.Message);
        }

        [Fact]
        public async Task StringTaskImmediateThrowIsTransparent()
        {
            var mock = new Mock<IInter>();
            mock.Setup(i => i.StringTaskNoArgs()).Throws(new InvalidOperationException("Test exception text"));
            var gen = new ProxyGenerator();
            var impl = (IInter) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IInter),
                new Type[0],
                mock.Object,
                new TestInterceptor());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => impl.StringTaskNoArgs());
            Assert.Equal("Test exception text", ex.Message);
        }

        [Fact]
        public async Task StringTaskDelayedThrowIsTransparent()
        {
            var mock = new Mock<IInter>();
            mock.Setup(i => i.StringTaskNoArgs()).Returns(Task.FromException<string>(new InvalidOperationException("Test exception text")));
            var gen = new ProxyGenerator();
            var impl = (IInter) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IInter),
                new Type[0],
                mock.Object,
                new TestInterceptor());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => impl.StringTaskNoArgs());
            Assert.Equal("Test exception text", ex.Message);
        }

        private static async Task TestInterception(Func<IInter, Inter, Task> test)
        {
            var interceptor = new TestInterceptor();

            var inter = new Inter();
            var gen = new ProxyGenerator();
            var impl = (IInter) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IInter),
                new Type[0],
                inter,
                interceptor);

            await test(impl, inter);

            Assert.Equal(1, interceptor.Count);
        }

        // ReSharper disable once MemberCanBePrivate.Global This is Mocked, so much be public
        public interface IInter
        {
            void VoidNoArgs();
            void VoidArgs(int i, string s);
            int IntNoArgs();
            int IntArgs(int i, string s);
            string StringNoArgs();
            string StringArgs(int i, string s);
            Task TaskNoArgs();
            Task TaskArgs(int i, string s);
            Task<int> IntTaskNoArgs();
            Task<int> IntTaskArgs(int i, string s);
            Task<string> StringTaskNoArgs();
            Task<string> StringTaskArgs(int i, string s);
        }

        public class Inter : IInter
        {
            public const int IntReturn = 483;
            public const string StringReturn = "8f7ehc";
            public int CallCount;
            public int IntParam;
            public string StringParam;

            public void VoidNoArgs()
            {
                CallCount++;
            }

            public void VoidArgs(int i, string s)
            {
                CallCount++;
                IntParam = i;
                StringParam = s;
            }

            public int IntNoArgs()
            {
                CallCount++;
                return IntReturn;
            }

            public int IntArgs(int i, string s)
            {
                CallCount++;
                IntParam = i;
                StringParam = s;
                return IntReturn;
            }

            public string StringNoArgs()
            {
                CallCount++;
                return StringReturn;
            }

            public string StringArgs(int i, string s)
            {
                CallCount++;
                IntParam = i;
                StringParam = s;
                return StringReturn;
            }

            public Task TaskNoArgs()
            {
                CallCount++;
                return Task.CompletedTask;
            }

            public Task TaskArgs(int i, string s)
            {
                CallCount++;
                IntParam = i;
                StringParam = s;
                return Task.CompletedTask;
            }

            public Task<int> IntTaskNoArgs()
            {
                CallCount++;
                return Task.FromResult(IntReturn);
            }

            public Task<int> IntTaskArgs(int i, string s)
            {
                CallCount++;
                IntParam = i;
                StringParam = s;
                return Task.FromResult(IntReturn);
            }

            public Task<string> StringTaskNoArgs()
            {
                CallCount++;
                return Task.FromResult(StringReturn);
            }

            public Task<string> StringTaskArgs(int i, string s)
            {
                CallCount++;
                IntParam = i;
                StringParam = s;
                return Task.FromResult(StringReturn);
            }
        }

        public class TestInterceptor : AsyncInterceptor
        {
            public int Count;

            protected override Task<T> InterceptAsync<T>(IInvocation invocation, Func<Task<T>> call)
            {
                Count++;
                return call();
            }
        }
    }
}
