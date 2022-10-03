using System;
using JetBrains.Annotations;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;

[AttributeUsage(AttributeTargets.Class)]
[MeansImplicitUse(ImplicitUseKindFlags.InstantiatedWithFixedConstructorSignature, ImplicitUseTargetFlags.WithMembers)]
public class TestDependencyInjectionSetupAttribute : Attribute
{
}
