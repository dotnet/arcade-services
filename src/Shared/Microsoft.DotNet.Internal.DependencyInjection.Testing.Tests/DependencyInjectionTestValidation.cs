using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.DotNet.Internal.DependencyInjection.Testing.Tests
{
    public class DependencyInjectionTestValidation
    {
        [Fact]
        public void DelegateRequirementsMess_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddSingleton<ISimple, Simple>();
                    s.AddSingleton<IWithValue>(p => new WithValue(p.GetRequiredService<ISimple>().SimpleValue));
                    s.AddSingleton<NeedsValue>();
                },
                out string message);
            Assert.True(isCoherent, message);
        }

        [Fact]
        public void Empty_Pass()
        {
            Assert.True(DependencyInjectionValidation.IsDependencyResolutionCoherent(s => { }, out string message),
                message);
        }

        [Fact]
        public void InstanceRequirementsMess_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddSingleton<IWithValue>(new WithValue("Test1"));
                    s.AddSingleton<NeedsValue>();
                },
                out string message);
            Assert.True(isCoherent, message);
        }

        [Fact]
        public void MissingOptional_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(
                s => { s.AddSingleton<NeedsSimpleOptional>(); },
                out string message);
            Assert.True(isCoherent, message);
        }

        [Fact]
        public void MissingRequirements_Fail()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(
                s => { s.AddSingleton<NeedsSimple>(); },
                out string message);
            Assert.False(isCoherent);
            Assert.Contains(nameof(NeedsSimple), message);
        }

        [Fact]
        public void MissingSome_Fail()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(
                s => { s.AddSingleton<NeedsSome>(); },
                out string message);
            Assert.False(isCoherent);
            Assert.Contains(nameof(NeedsSome), message);
        }

        [Fact]
        public void PresentOptional_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddSingleton<ISimple, Simple>();
                    s.AddSingleton<NeedsSimpleOptional>();
                },
                out string message);
            Assert.True(isCoherent, message);
        }

        [Fact]
        public void SimpleRequirementsMess_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddSingleton<ISimple, Simple>();
                    s.AddSingleton<NeedsSimple>();
                },
                out string message);
            Assert.True(isCoherent, message);
        }

        [Fact]
        public void SimpleScopedRequirementsMess_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddScoped<ISimple, Simple>();
                    s.AddScoped<NeedsSimple>();
                },
                out string message);
            Assert.True(isCoherent, message);
        }

        [Fact]
        public void SimpleSome_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddSingleton<ISimple, Simple>();
                    s.AddSingleton<NeedsSome>();
                },
                out string message);
            Assert.True(isCoherent, message);
        }

        [Fact]
        public void ValueSome_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddSingleton<IWithValue>(new WithValue("Test1"));
                    s.AddSingleton<NeedsSome>();
                },
                out string message);
            Assert.True(isCoherent, message);
        }

        [Fact]
        public void MismatchedScopes_Fail()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddScoped<ISimple, Simple>();
                    s.AddSingleton<NeedsSimple>();
                },
                out string message);
            Assert.False(isCoherent);
            Assert.Contains(nameof(NeedsSimple), message);
        }
    }
}
