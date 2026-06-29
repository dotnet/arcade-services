// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Moq;
using NUnit.Framework;
using ProductConstructionService.DependencyFlow.Tests.Mocks;

namespace ProductConstructionService.DependencyFlow.Tests;

[TestFixture]
public class TestsWithMocks
{
    private VerifyableMockRepository _mocks = null!;

    [SetUp]
    public void TestsWithMocks_SetUp()
    {
        _mocks = new VerifyableMockRepository(MockBehavior.Loose);
    }

    [TearDown]
    public void TestsWithMocks_TearDown()
    {
        _mocks.VerifyNoUnverifiedCalls();
    }

    protected Mock<T> CreateMock<T>(MockBehavior behavior = MockBehavior.Default) where T : class
    {
        return _mocks.Create<T>(behavior);
    }
}
