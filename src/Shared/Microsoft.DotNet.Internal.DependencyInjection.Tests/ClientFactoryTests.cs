using System;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;

namespace Microsoft.DotNet.Internal.DependencyInjection.Tests
{
    [TestFixture]
    public sealed class ClientFactoryTests
    {
        private class Options
        {
            public string Data { get; set; }
        }

        private class Client : IDisposable
        {
            public Client(Options options)
            {
                Data = options.Data;
                Disposed = false;
            }
            public string Data { get; }
            public bool Disposed { get; private set; }
            public void Dispose()
            {
                Disposed = true;
            }
        }

        private class OptionsChangeSource : IOptionsChangeTokenSource<Options>
        {
            private CancellationTokenSource _cts;
            private IChangeToken _changeToken;
            public OptionsChangeSource(string name)
            {
                Name = name;
                _cts = new CancellationTokenSource();
                _changeToken = new CancellationChangeToken(_cts.Token);
            }

            public void TriggerChanged()
            {
                CancellationTokenSource old = _cts;
                _cts = new CancellationTokenSource();
                _changeToken = new CancellationChangeToken(_cts.Token);
                old.Cancel();
            }

            public IChangeToken GetChangeToken()
            {
                return _changeToken;
            }

            public string Name { get; }
        }

        private static ServiceProvider Setup(Action<IServiceCollection> setupAction)
        {
            var services = new ServiceCollection();
            services.AddOptions();
            setupAction(services);

            return services.BuildServiceProvider();
        }

        [Test]
        public void ReturnsSeparateClientForEachName()
        {
            using ServiceProvider provider = Setup(services =>
            {
                services.AddClientFactory<Options, Client, Client>();
                services.Configure<Options>("first", o => o.Data = "first");
                services.Configure<Options>("second", o => o.Data = "second");
            });
            IClientFactory<Client> factory = provider.GetRequiredService<IClientFactory<Client>>();
            using Reference<Client> firstClient = factory.GetClient("first");
            using Reference<Client> secondClient = factory.GetClient("second");
            secondClient.Value.Should().NotBeSameAs(firstClient.Value);
            firstClient.Value.Data.Should().Be("first");
            secondClient.Value.Data.Should().Be("second");
        }

        [Test]
        public void ReturnsSameClientForSameName()
        {
            using ServiceProvider provider = Setup(services =>
            {
                services.AddClientFactory<Options, Client, Client>();
                services.Configure<Options>("client", o => o.Data = "client");
            });
            IClientFactory<Client> factory = provider.GetRequiredService<IClientFactory<Client>>();
            using Reference<Client> firstClient = factory.GetClient("client");
            using Reference<Client> secondClient = factory.GetClient("client");
            secondClient.Value.Should().BeSameAs(firstClient.Value);
            firstClient.Value.Data.Should().Be("client");
            secondClient.Value.Data.Should().Be("client");
        }

        [Test]
        public void NullIsReturned()
        {
            using ServiceProvider provider = Setup(services =>
            {
                services.AddClientFactory<Options, Client>(o => null);
                services.Configure<Options>("client", o => o.Data = "client");
            });
            IClientFactory<Client> factory = provider.GetRequiredService<IClientFactory<Client>>();
            using Reference<Client> firstClient = factory.GetClient("client");
            firstClient.Value.Should().BeNull();
        }

        [Test]
        public void ReturnsNewClientForSameNameAfterOptionsChanged()
        {
            var changeTokenSource = new OptionsChangeSource("client");
            using ServiceProvider provider = Setup(services =>
            {
                services.AddClientFactory<Options, Client>(o => new Client(o));
                services.Configure<Options>("client", o => o.Data = "client");
                services.AddSingleton<IOptionsChangeTokenSource<Options>>(changeTokenSource);
            });
            IClientFactory<Client> factory = provider.GetRequiredService<IClientFactory<Client>>();
            using Reference<Client> firstClient = factory.GetClient("client");
            firstClient.Value.Data.Should().Be("client");
            changeTokenSource.TriggerChanged();

            using Reference<Client> secondClient = factory.GetClient("client");
            secondClient.Value.Data.Should().Be("client");
            secondClient.Value.Should().NotBeSameAs(firstClient.Value);
        }

        [Test]
        public void ClientNotDisposedWhenRequestIsDoneWithIt()
        {
            using ServiceProvider provider = Setup(services =>
            {
                services.AddClientFactory<Options, Client>(o => new Client(o));
                services.Configure<Options>("client", o => o.Data = "client");
            });
            IClientFactory<Client> factory = provider.GetRequiredService<IClientFactory<Client>>();
            Reference<Client> firstClient;
            using (firstClient = factory.GetClient("client"))
            {
                firstClient.Value.Data.Should().Be("client");
                firstClient.Value.Disposed.Should().BeFalse();
            }

            firstClient.Value.Disposed.Should().BeFalse();

            using (Reference<Client> secondClient = factory.GetClient("client"))
            {
                secondClient.Value.Should().BeSameAs(firstClient.Value);
            }
        }

        [Test]
        public void ClientNotDisposedAfterOptionsChangeUntilRequestIsDoneWithIt()
        {
            var changeTokenSource = new OptionsChangeSource("client");
            using ServiceProvider provider = Setup(services =>
            {
                services.AddClientFactory<Options, Client>(o => new Client(o));
                services.Configure<Options>("client", o => o.Data = "client");
                services.AddSingleton<IOptionsChangeTokenSource<Options>>(changeTokenSource);
            });
            IClientFactory<Client> factory = provider.GetRequiredService<IClientFactory<Client>>();
            Reference<Client> firstClient;
            using (firstClient = factory.GetClient("client"))
            {
                firstClient.Value.Data.Should().Be("client");
                firstClient.Value.Disposed.Should().BeFalse();

                changeTokenSource.TriggerChanged();

                firstClient.Value.Disposed.Should().BeFalse();
            }

            firstClient.Value.Disposed.Should().BeTrue();
        }
    }
}
