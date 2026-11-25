using System;

namespace ApiConnectTool.Services;

/// <summary>
/// The token endpoint answered with a non-success status. The message is already localized
/// and redacted; <see cref="StatusCode"/> lets callers tell a rejected grant (400/401) apart
/// from a connection failure, which is reported as a plain <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class OAuthTokenRejectedException(int statusCode, string message) : InvalidOperationException(message)
{
    public int StatusCode { get; } = statusCode;

    public bool IsGrantRejection => StatusCode is 400 or 401;
}
