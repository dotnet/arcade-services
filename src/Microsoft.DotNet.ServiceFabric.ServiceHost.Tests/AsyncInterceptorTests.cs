using System;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using FluentAssertions;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    [TestFixture]
    public class AsyncInterceptorTests
    {
        [Test]
        public async Task TestIntArg()
        {
            await TestInterception((impl, target) =>
            {
                impl.IntArgs(123, "TestParam").Should().Be(Inter.IntReturn);
                target.IntParam.Should().Be(123);
                target.StringParam.Should().Be("TestParam");
                target.CallCount.Should().Be(1);
                return Task.CompletedTask;
            });
        }

        [Test]
        public async Task TestIntNoArg()
        {
            await TestInterception((impl, target) =>
            {
                impl.IntNoArgs().Should().Be(Inter.IntReturn);
                target.IntParam.Should().Be(0);
                target.StringParam.Should().BeNull();
                target.CallCount.Should().Be(1);
                return Task.CompletedTask;
            });
        }

        [Test]
        public async Task TestIntTaskArg()
        {
            await TestInterception(async (impl, target) =>
            {
                (await impl.IntTaskArgs(123, "TestParam")).Should().Be(Inter.IntReturn);
                target.IntParam.Should().Be(123);
                target.StringParam.Should().Be("TestParam");
                target.CallCount.Should().Be(1);
            });
        }

        [Test]
        public async Task TestIntTaskNoArg()
        {
            await TestInterception(async (impl, target) =>
            {
                (await impl.IntTaskNoArgs()).Should().Be(Inter.IntReturn);
                target.IntParam.Should().Be(0);
                target.StringParam.Should().BeNull();
                target.CallCount.Should().Be(1);
            });
        }

        [Test]
        public async Task TestStringArg()
        {
            await TestInterception((impl, target) =>
            {
                impl.StringArgs(123, "TestParam").Should().Be(Inter.StringReturn);
                target.IntParam.Should().Be(123);
                target.StringParam.Should().Be("TestParam");
                target.CallCount.Should().Be(1);
                return Task.CompletedTask;
            });
        }

        [Test]
        public async Task TestStringNoArg()
        {
            await TestInterception((impl, target) =>
            {
                impl.StringNoArgs().Should().Be(Inter.StringReturn);
                target.IntParam.Should().Be(0);
                target.StringParam.Should().BeNull();
                target.CallCount.Should().Be(1);
                return Task.CompletedTask;
            });
        }

        [Test]
        public async Task TestStringTaskArg()
        {
            await TestInterception(async (impl, target) =>
            {
                (await impl.StringTaskArgs(123, "TestParam")).Should().Be(Inter.StringReturn);
                target.IntParam.Should().Be(123);
                target.StringParam.Should().Be("TestParam");
                target.CallCount.Should().Be(1);
            });
        }

        [Test]
        public async Task TestStringTaskNoArg()
        {
            await TestInterception(async (impl, target) =>
            {
                (await impl.StringTaskNoArgs()).Should().Be(Inter.StringReturn);
                target.IntParam.Should().Be(0);
                target.StringParam.Should().BeNull();
                target.CallCount.Should().Be(1);
            });
        }

        [Test]
        public async Task TestTaskArg()
        {
            await TestInterception(async (impl, target) =>
            {
                await impl.TaskArgs(123, "TestParam");
                target.IntParam.Should().Be(123);
                target.StringParam.Should().Be("TestParam");
                target.CallCount.Should().Be(1);
            });
        }

        [Test]
        public async Task TestTaskNoArg()
        {
            await TestInterception(async (impl, target) =>
            {
                await impl.TaskNoArgs();
                target.IntParam.Should().Be(0);
                target.StringParam.Should().BeNull();
                target.CallCount.Should().Be(1);
            });
        }

        [Test]
        public async Task TestVoidArgs()
        {
            await ((Func<Task>) (() =>
                        TestInterception((impl, target) =>
                        {
                            impl.VoidArgs(123, "TestParam");
                            return Task.CompletedTask;
                        })
                    ))
                .Should()
                .ThrowExactlyAsync<NotSupportedException>();
        }

        [Test]
        public async Task TestVoidNoArg()
        {
            await ((Func<Task>) (() =>
                        TestInterception((impl, target) =>
                        {
                            impl.VoidNoArgs();
                            return Task.CompletedTask;
                        })
                    ))
                .Should()
                .ThrowExactlyAsync<NotSupportedException>();
        }

        [Test]
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

            var ex = (((Func<object>)(() => impl.IntNoArgs()))).Should().Throw<InvalidOperationException>().Which;
            ex.Message.Should().Be("Test exception text");
        }

        [Test]
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

            var ex = (((Func<object>)(() => impl.StringNoArgs()))).Should().Throw<InvalidOperationException>().Which;
            ex.Message.Should().Be("Test exception text");
        }

        [Test]
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

            var ex = (await (((Func<Task>)(() => impl.TaskNoArgs()))).Should().ThrowAsync<InvalidOperationException>()).Which;
            ex.Message.Should().Be("Test exception text");
        }

        [Test]
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

            var ex = (await (((Func<Task>)(() => impl.TaskNoArgs()))).Should().ThrowAsync<InvalidOperationException>()).Which;
            ex.Message.Should().Be("Test exception text");
        }

        [Test]
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

            var ex = (await (((Func<Task>)(() => impl.IntTaskNoArgs()))).Should().ThrowAsync<InvalidOperationException>()).Which;
            ex.Message.Should().Be("Test exception text");
        }

        [Test]
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

            var ex = (await (((Func<Task>)(() => impl.IntTaskNoArgs()))).Should().ThrowAsync<InvalidOperationException>()).Which;
            ex.Message.Should().Be("Test exception text");
        }

        [Test]
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

            var ex = (await (((Func<Task>)(() => impl.StringTaskNoArgs()))).Should().ThrowAsync<InvalidOperationException>()).Which;
            ex.Message.Should().Be("Test exception text");
        }

        [Test]
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

            var ex = (await (((Func<Task>)(() => impl.StringTaskNoArgs()))).Should().ThrowAsync<InvalidOperationException>()).Which;
            ex.Message.Should().Be("Test exception text");
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

            interceptor.Count.Should().Be(1);
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
