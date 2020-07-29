using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Microsoft.DotNet.Internal.DependencyInjection.Testing.Tests
{
    [TestFixture]
    public class DependencyInjectionTestValidation
    {
        [Test]
        public void DelegateRequirementsMess_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddSingleton<ISimple, Simple>();
                    s.AddSingleton<IWithValue>(p => new WithValue(p.GetRequiredService<ISimple>().SimpleValue));
                    s.AddSingleton<NeedsValue>();
                },
                out string message);
            isCoherent.Should().BeTrue(message);
        }

        [Test]
        public void Empty_Pass()
        {
            DependencyInjectionValidation.IsDependencyResolutionCoherent(s => { }, out string message).Should().BeTrue();
        }

        [Test]
        public void InstanceRequirementsMess_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddSingleton<IWithValue>(new WithValue("Test1"));
                    s.AddSingleton<NeedsValue>();
                },
                out string message);
            isCoherent.Should().BeTrue(message);
        }

        [Test]
        public void MissingOptional_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(
                s => { s.AddSingleton<NeedsSimpleOptional>(); },
                out string message);
            isCoherent.Should().BeTrue(message);
        }

        [Test]
        public void MissingRequirements_Fail()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(
                s => { s.AddSingleton<NeedsSimple>(); },
                out string message);
            isCoherent.Should().BeFalse(message);
            message.Should().Contain(nameof(NeedsSimple));
        }

        [Test]
        public void MissingSome_Fail()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(
                s => { s.AddSingleton<NeedsSome>(); },
                out string message);
            isCoherent.Should().BeFalse(message);
            message.Should().Contain(nameof(NeedsSome));
        }

        [Test]
        public void PresentOptional_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddSingleton<ISimple, Simple>();
                    s.AddSingleton<NeedsSimpleOptional>();
                },
                out string message);
            isCoherent.Should().BeTrue(message);
        }

        [Test]
        public void SimpleRequirementsMess_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddSingleton<ISimple, Simple>();
                    s.AddSingleton<NeedsSimple>();
                },
                out string message);
            isCoherent.Should().BeTrue(message);
        }

        [Test]
        public void SimpleScopedRequirementsMess_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddScoped<ISimple, Simple>();
                    s.AddScoped<NeedsSimple>();
                },
                out string message);
            isCoherent.Should().BeTrue();
        }

        [Test]
        public void SimpleSome_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddSingleton<ISimple, Simple>();
                    s.AddSingleton<NeedsSome>();
                },
                out string message);
            isCoherent.Should().BeTrue();
        }

        [Test]
        public void ValueSome_Pass()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddSingleton<IWithValue>(new WithValue("Test1"));
                    s.AddSingleton<NeedsSome>();
                },
                out string message);
            isCoherent.Should().BeTrue();
        }

        [Test]
        public void MismatchedScopes_Fail()
        {
            bool isCoherent = DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
                {
                    s.AddScoped<ISimple, Simple>();
                    s.AddSingleton<NeedsSimple>();
                },
                out string message);
            isCoherent.Should().BeFalse();
            message.Should().Contain(nameof(NeedsSimple));
        }
    }
}
