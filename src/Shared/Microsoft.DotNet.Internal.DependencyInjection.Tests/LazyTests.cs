using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.DotNet.Internal.DependencyInjection.Tests
{
    public class LazyTests
    {
        [Fact]
        public void UnresolvedNoCreation()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Assert.Equal(0, tracker.Created);
            }
        }

        [Fact]
        public void ResolvedNoValueNoCreation()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Lazy<Tracked> lazy = provider.GetRequiredService<Lazy<Tracked>>();
                Assert.Equal(0, tracker.Created);
            }
        }

        [Fact]
        public void ResolvedCreatesValue()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Lazy<Tracked> lazy = provider.GetRequiredService<Lazy<Tracked>>();
                Assert.Equal(0, tracker.Created);
                Tracked t = lazy.Value;
                Assert.Equal(1, tracker.Created);
                Assert.Equal(1, t.Id);
            }
        }

        [Fact]
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
                Assert.False(t.Disposed, "Not disposed before provider is");
            }
            Assert.True(t.Disposed, "Disposed with provider");
        }

        [Fact]
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
                    Assert.Equal(1, tracker.Created);
                }
                Assert.False(a1.Disposed);
            }
        }

        [Fact]
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
                    Assert.Equal(1, tracker.Created);
                }
                Assert.True(a1.Disposed);
            }
        }

        [Fact]
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
                Assert.NotSame(a, b);
            }
        }

        [Fact]
        public void LazyResolutionIsLazy()
        {
            Tracker tracker = new Tracker();
            ServiceCollection collection = new ServiceCollection();
            collection.AddSingleton(_ => tracker.Create());
            collection.EnableLazy();
            using (ServiceProvider provider = collection.BuildServiceProvider())
            {
                Lazy<Tracked> a = provider.GetRequiredService<Lazy<Tracked>>();
                Assert.False(a.IsValueCreated);
                var a1 = a.Value;
                var a2 = a.Value;
                Assert.Same(a1, a2);
                Assert.Equal(1, tracker.Created);
            }
        }

        [Fact]
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
                Assert.Same(a1, b1);
                Assert.Equal(1, tracker.Created);
            }
        }

        [Fact]
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
                    Assert.Equal(1, tracker.Created);
                }
                Assert.True(a1.Disposed);
                using (IServiceScope scope =provider.CreateScope())
                {
                    Lazy<Tracked> b = scope.ServiceProvider.GetRequiredService<Lazy<Tracked>>();
                    b1 = b.Value;
                    Assert.Equal(2, tracker.Created);
                }
                Assert.True(b1.Disposed);
                Assert.NotSame(a1, b1);
            }
        }

        [Fact]
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
                    Assert.Equal(1, tracker.Created);
                }
                using (IServiceScope scope =provider.CreateScope())
                {
                    Lazy<Tracked> b = scope.ServiceProvider.GetRequiredService<Lazy<Tracked>>();
                    b1 = b.Value;
                    Assert.Equal(1, tracker.Created);
                }
                Assert.Same(a1, b1);
            }
        }

        [Fact]
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
                    Assert.Equal(1, tracker.Created);
                    Lazy<Tracked> b = scope.ServiceProvider.GetRequiredService<Lazy<Tracked>>();
                    b1 = b.Value;
                    Assert.Equal(2, tracker.Created);
                    Assert.NotSame(a1,b1);
                }
                Assert.True(a1.Disposed);
                Assert.True(b1.Disposed);
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
