using FluentAssertions;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Timers;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace RolloutScorerAzureFunction.Tests
{
    public class RolloutScorerAzureFunctionTests
    {
        private RolloutScorerFunction _rolloutScorerFunction;

        [SetUp]
        public void Setup()
        {
            Mock<IKeyVaultClient> keyVaultClientMock = new Mock<IKeyVaultClient>();            

            var services = new ServiceCollection()
                .AddLogging(l => { l.AddProvider(new NUnitLogger()); })
                .AddSingleton(keyVaultClientMock.Object)
                .AddSingleton<RolloutScorerFunction>()
                .BuildServiceProvider();

            _rolloutScorerFunction = ActivatorUtilities.CreateInstance<RolloutScorerFunction>(services);
        }

        [Test]
        public void Test1()
        {
            var timerScheduleMock = new Mock<TimerSchedule>();
            var timerInfoMock = new TimerInfo(timerScheduleMock.Object, new ScheduleStatus(), true);

            _rolloutScorerFunction.Run(timerInfoMock);
        }
    }
}
