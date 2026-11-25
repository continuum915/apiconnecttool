using System;

namespace ApiConnectTool.Models;

public sealed record OAuthToken(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAtUtc)
{
    public OAuthContext? Context { get; init; }
}
