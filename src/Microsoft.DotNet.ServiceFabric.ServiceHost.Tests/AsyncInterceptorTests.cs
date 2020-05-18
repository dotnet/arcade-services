using System;
using System.Linq;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Moq;
using Xunit;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    public class AsyncInterceptorTests
    {
        [MemberData(nameof(CreateCalls))]
        [Theory]
        public void TestVoidNoArg(string method, object retValue, object [] args)
        {
            args ??= new object[0];
            var interceptor = new Mock<AsyncInterceptor>();

            if (ReferenceEquals(retValue, typeof(void)))
            {
                interceptor.Setup(i => i.Intercept(It.IsAny<IInvocation>()))
                    .Verifiable();
            }
            else
            {
                interceptor.Setup(i => i.Intercept(It.IsAny<IInvocation>()))
                    .Callback<IInvocation>(i => i.ReturnValue = retValue)
                    .Verifiable();
            }


            var gen = new ProxyGenerator();
            var impl = (IInter) gen.CreateInterfaceProxyWithTargetInterface(
                typeof(IInter),
                new Type[0],
                (object) null,
                interceptor.Object);

            var returned = impl.GetType().GetMethod(method).Invoke(impl, args);
            if (!ReferenceEquals(retValue, typeof(void)))
            {
                Assert.Equal(retValue, returned);
            }

            interceptor.Verify(i =>
                    i.Intercept(It.Is<IInvocation>(inv => inv.Method == typeof(IInter).GetMethod(nameof(method)))),
                Times.Once
            );
            interceptor.VerifyNoOtherCalls();
        }

        public static object[][] CreateCalls()
        {
            return new[]
            {
                new object[] {nameof(IInter.VoidNoArgs), typeof(void), null},
                new object[] {nameof(IInter.IntNoArgs), 7, null},
            };
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
    }
}
