using System;

namespace ApiConnectTool.Models;

/// <summary>Display-only projection of a <see cref="System.Net.Cookie"/> for the response viewer's
/// Cookies tab - never persisted itself (the jar persists the real cookies).</summary>
public class CookieItem
{
    public static CookieItem FromCookie(System.Net.Cookie cookie) => new()
    {
        Name = cookie.Name,
        Value = cookie.Value,
        Domain = cookie.Domain,
        Path = cookie.Path,
        Expires = cookie.Expires,
        Secure = cookie.Secure,
        HttpOnly = cookie.HttpOnly
    };

    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime Expires { get; set; }
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }
}
