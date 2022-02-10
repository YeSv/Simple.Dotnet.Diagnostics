using Simple.Dotnet.Utilities.Results;

namespace Simple.Dotnet.Diagnostics.Host;

public readonly record struct Error(int Code, string Message);

public readonly record struct Response<T>(bool IsOk, T? Data, Error? Error);

public static class ResponseMapper
{
    public static Response<TOk> ToResponse<TOk, TError>(
        in UniResult<TOk, TError> result, 
        Func<TError, Error>? errorMapper = null) where TOk : class where TError : class => result switch
        {
            { IsOk: true } => new(true, result.Ok!, default),
            _ => new(false, default, errorMapper!(result.Error!))
        };

    public static Response<TOk> ToResponse<TOk, TError>(in Result<TOk, TError> result, Func<TError, Error>? errorMapper) => result switch
    {
        { IsOk: true } => new(true, result.Ok!, default),
        _ => new(false, default, errorMapper!(result.Error!))
    };
}

