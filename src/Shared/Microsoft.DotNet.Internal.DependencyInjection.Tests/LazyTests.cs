using System;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.Internal.DependencyInjection.Tests
{
    [TestFixture]
    public class LazyTests
    {
        [Test]
        public void UnresolvedNoCreation()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                tracker.Created.Should().Be(0);
            }
        }

        [Test]
        public void ResolvedNoValueNoCreation()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Lazy<Tracked> lazy = provider.GetRequiredService<Lazy<Tracked>>();
                tracker.Created.Should().Be(0);
            }
        }

        [Test]
        public void ResolvedCreatesValue()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Lazy<Tracked> lazy = provider.GetRequiredService<Lazy<Tracked>>();
                tracker.Created.Should().Be(0);
                Tracked t = lazy.Value;
                tracker.Created.Should().Be(1);
                t.Id.Should().Be(1);
            }
        }

        [Test]
        public void SingletonDisposes()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            Tracked t;
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Lazy<Tracked> lazy = provider.GetRequiredService<Lazy<Tracked>>();
                t = lazy.Value;
                t.Disposed.Should().BeFalse();
            }
            t.Disposed.Should().BeTrue();
        }

        [Test]
        public void SingletonNotDisposedBetweenScopes()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Tracked a1;
                using (provider.CreateScope())
                {
                    Lazy<Tracked> a = provider.GetRequiredService<Lazy<Tracked>>();
                    a1 = a.Value;
                    tracker.Created.Should().Be(1);
                }
                a1.Disposed.Should().BeFalse();
            }
        }

        [Test]
        public void ScopedDisposedBetweenScopes()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddScoped(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Tracked a1;
                using (IServiceScope scope = provider.CreateScope())
                {
                    Lazy<Tracked> a = scope.ServiceProvider.GetRequiredService<Lazy<Tracked>>();
                    a1 = a.Value;
                    tracker.Created.Should().Be(1);
                }
                a1.Disposed.Should().BeTrue();
            }
        }

        [Test]
        public void ResolvesDifferentLazy()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Lazy<Tracked> a = provider.GetRequiredService<Lazy<Tracked>>();
                Lazy<Tracked> b = provider.GetRequiredService<Lazy<Tracked>>();
                b.Should().NotBeSameAs(a);
            }
        }

        [Test]
        public void LazyResolutionIsLazy()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Lazy<Tracked> a = provider.GetRequiredService<Lazy<Tracked>>();
                a.IsValueCreated.Should().BeFalse();
                var a1 = a.Value;
                var a2 = a.Value;
                a2.Should().BeSameAs(a1);
                tracker.Created.Should().Be(1);
            }
        }

        [Test]
        public void ResolvesSameObject()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Lazy<Tracked> a = provider.GetRequiredService<Lazy<Tracked>>();
                Tracked a1 = a.Value;
                Lazy<Tracked> b = provider.GetRequiredService<Lazy<Tracked>>();
                Tracked b1 = b.Value;
                b1.Should().BeSameAs(a1);
                tracker.Created.Should().Be(1);
            }
        }

        [Test]
        public void ScopeResolutionIsIndependent()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddScoped(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Tracked a1, b1;
                using (IServiceScope scope = provider.CreateScope())
                {
                    Lazy<Tracked> a = scope.ServiceProvider.GetRequiredService<Lazy<Tracked>>();
                    a1 = a.Value;
                    tracker.Created.Should().Be(1);
                }
                a1.Disposed.Should().BeTrue();
                using (IServiceScope scope =provider.CreateScope())
                {
                    Lazy<Tracked> b = scope.ServiceProvider.GetRequiredService<Lazy<Tracked>>();
                    b1 = b.Value;
                    tracker.Created.Should().Be(2);
                }
                b1.Disposed.Should().BeTrue();
                b1.Should().NotBeSameAs(a1);
            }
        }

        [Test]
        public void SingletonSharedBetweenScopes()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Tracked a1, b1;
                using (IServiceScope scope =provider.CreateScope())
                {
                    Lazy<Tracked> a = scope.ServiceProvider.GetRequiredService<Lazy<Tracked>>();
                    a1 = a.Value;
                    tracker.Created.Should().Be(1);
                }
                using (IServiceScope scope =provider.CreateScope())
                {
                    Lazy<Tracked> b = scope.ServiceProvider.GetRequiredService<Lazy<Tracked>>();
                    b1 = b.Value;
                    tracker.Created.Should().Be(1);
                }
                b1.Should().BeSameAs(a1);
            }
        }

        [Test]
        public void TransientIsUniqueAndDisposed()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddTransient(_ => tracker.Create());
            collection.EnableLazy();
            Tracked a1, b1;
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                using (IServiceScope scope =provider.CreateScope())
                {
                    Lazy<Tracked> a = scope.ServiceProvider.GetRequiredService<Lazy<Tracked>>();
                    a1 = a.Value;
                    tracker.Created.Should().Be(1);
                    Lazy<Tracked> b = scope.ServiceProvider.GetRequiredService<Lazy<Tracked>>();
                    b1 = b.Value;
                    tracker.Created.Should().Be(2);
                    b1.Should().NotBeSameAs(a1);
                }
                a1.Disposed.Should().BeTrue();
                b1.Disposed.Should().BeTrue();
            }
        }

        public class Tracker
        {
            public int Created;

            public Tracked Create()
            {
                int value = Interlocked.Increment(ref Created);
                return new Tracked(value);
            }
        }

        public class Tracked : IDisposable
        {
            public int Id { get; }
            public bool Disposed { get; private set; }

            public Tracked(int id)
            {
                Id = id;
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
