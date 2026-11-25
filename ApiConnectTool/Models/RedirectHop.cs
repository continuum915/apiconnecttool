using System;
using System.Net;

namespace ApiConnectTool.Models;

public sealed class RedirectHop
{
    public required Uri RequestUri { get; init; }
    public required HttpStatusCode StatusCode { get; init; }
    public Uri? Location { get; init; }
}
