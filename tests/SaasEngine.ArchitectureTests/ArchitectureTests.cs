using System.Reflection;
using NetArchTest.Rules;
using Xunit;
using FluentAssertions;
using SaasEngine.Domain.Shared;

namespace SaasEngine.ArchitectureTests;

public class ArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(ISecretStore).Assembly;
    
    // We get the API assembly by using a type we know is in there. 
    // Usually Program is used, but we'll use a type from SaasEngine.Api.
    private static readonly Assembly ApiAssembly = typeof(SaasEngine.Api.Infrastructure.Security.JwtTokenGenerator).Assembly;

    [Fact]
    public void DomainLayer_ShouldNot_HaveDependenciesOnApiLayer()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("SaasEngine.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
