namespace ApiConnectTool.ViewModels;

/// <summary>Semantic outcome of the last send; views map it to theme tokens.</summary>
public enum ResponseStatusClass
{
    None,
    Sending,
    Informational,
    Success,
    Redirect,
    ClientError,
    ServerError,
    TransportError
}

public static class ResponseStatusClassifier
{
    public static ResponseStatusClass FromStatusCode(int statusCode) => statusCode switch
    {
        >= 100 and < 200 => ResponseStatusClass.Informational,
        >= 200 and < 300 => ResponseStatusClass.Success,
        >= 300 and < 400 => ResponseStatusClass.Redirect,
        >= 400 and < 500 => ResponseStatusClass.ClientError,
        >= 500 => ResponseStatusClass.ServerError,
        _ => ResponseStatusClass.None
    };
}
