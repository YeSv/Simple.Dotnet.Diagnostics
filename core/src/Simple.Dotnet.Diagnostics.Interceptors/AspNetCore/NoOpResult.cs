using Microsoft.AspNetCore.Http;

namespace Simple.Dotnet.Diagnostics.Interceptors.AspNetCore;

public sealed record NoOpResult : IResult
{
    static readonly NoOpResult Shared = new();

    public static NoOpResult Create() => Shared;

    public Task ExecuteAsync(HttpContext httpContext) => Task.CompletedTask;
}
