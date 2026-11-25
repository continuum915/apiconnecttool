using System.Collections.Generic;
using ApiConnectTool.Models;
using Xunit;

namespace ApiConnectTool.Tests.Models;

public class RequestEnvironmentTests
{
    [Fact]
    public void BuildVariableMap_CurrentValueTakesPrecedenceOverInitialValue()
    {
        var environment = new RequestEnvironment
        {
            Variables =
            {
                new EnvironmentVariable { Key = "token", InitialValue = new EncryptedString("initial"), CurrentValue = new EncryptedString("override") }
            }
        };

        var map = environment.BuildVariableMap();

        Assert.Equal("override", map["token"]);
    }

    [Fact]
    public void BuildVariableMap_FallsBackToInitialValueWhenCurrentValueEmpty()
    {
        var environment = new RequestEnvironment
        {
            Variables = { new EnvironmentVariable { Key = "token", InitialValue = new EncryptedString("initial") } }
        };

        var map = environment.BuildVariableMap();

        Assert.Equal("initial", map["token"]);
    }

    [Fact]
    public void BuildVariableMap_ExcludesDisabledAndKeylessRows()
    {
        var environment = new RequestEnvironment
        {
            Variables =
            {
                new EnvironmentVariable { Key = "disabled", InitialValue = new EncryptedString("x"), IsEnabled = false },
                new EnvironmentVariable { Key = "", InitialValue = new EncryptedString("y") }
            }
        };

        var map = environment.BuildVariableMap();

        Assert.Empty(map);
    }

    [Fact]
    public void BuildSecretVariableNames_ReturnsOnlyEnabledSecretFlaggedKeys()
    {
        var environment = new RequestEnvironment
        {
            Variables =
            {
                new EnvironmentVariable { Key = "apiKey", IsSecret = true },
                new EnvironmentVariable { Key = "baseUrl", IsSecret = false },
                new EnvironmentVariable { Key = "disabledSecret", IsSecret = true, IsEnabled = false },
                new EnvironmentVariable { Key = "", IsSecret = true }
            }
        };

        var names = environment.BuildSecretVariableNames();

        Assert.Equal(new HashSet<string> { "apiKey" }, names);
    }
}
