using FluentAssertions;
using NUnit.Framework;
using System;

namespace Microsoft.DncEng.PatGenerator.Tests;

public class AzureDevOpsPATScopesFancyEnumTests
{
    [TestCase(AzureDevOpsPATScopes.build, "build-r")]
    [TestCase(AzureDevOpsPATScopes.build_execute, "build-re")]
    [TestCase(AzureDevOpsPATScopes.code, "code-r")]
    [TestCase(AzureDevOpsPATScopes.code_status, "code-s")]
    [TestCase(AzureDevOpsPATScopes.code_write, "code-rw")]
    [TestCase(AzureDevOpsPATScopes.code_manage, "code-m")]
    [TestCase(AzureDevOpsPATScopes.packaging, "package-r")]
    [TestCase(AzureDevOpsPATScopes.packaging_write, "package-rw")]
    [TestCase(AzureDevOpsPATScopes.packaging_manage, "package-m")]
    [TestCase(AzureDevOpsPATScopes.symbols, "symbols-r")]
    [TestCase(AzureDevOpsPATScopes.symbols_write, "symbols-rw")]
    [TestCase(AzureDevOpsPATScopes.symbols_manage, "symbols-m")]
    [TestCase(AzureDevOpsPATScopes.build_execute | AzureDevOpsPATScopes.code, "build-re-code-r")]
    [TestCase(AzureDevOpsPATScopes.packaging | AzureDevOpsPATScopes.code_write, "code-rw-package-r")]
    [TestCase(AzureDevOpsPATScopes.packaging | AzureDevOpsPATScopes.code_write | AzureDevOpsPATScopes.symbols_manage, "code-rw-package-r-symbols-m")]
    [TestCase(AzureDevOpsPATScopes.code | AzureDevOpsPATScopes.code_status | AzureDevOpsPATScopes.code_manage, "code-m")]
    [TestCase(AzureDevOpsPATScopes.code | AzureDevOpsPATScopes.code_status, "code-rs")]
    [TestCase(AzureDevOpsPATScopes.packaging | AzureDevOpsPATScopes.packaging_write | AzureDevOpsPATScopes.build_execute | AzureDevOpsPATScopes.code | AzureDevOpsPATScopes.code_status, "build-re-code-rs-package-rw")]
    [TestCase(AzureDevOpsPATScopes.test | AzureDevOpsPATScopes.test_write, "test-rw")]
    public void MinimalScopeStringTests(AzureDevOpsPATScopes scopes, string expectedString)
    {
        scopes.GetScopeString().Should().Be(expectedString);
    }
}
