using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Simple.Dotnet.Utilities.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Simple.Dotnet.Diagnostics.Host.HttpResults;

public static class JsonResult
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsonResult<T> Create<T>(T? value, int? statusCode = default) => new(value, statusCode);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JsonSerializerOptions? GetJsonOptions(this IServiceProvider? provider) => provider?.GetService<IOptions<JsonOptions>>()?.Value?.SerializerOptions;
}

public sealed record class JsonResult<T>(T? Value, int? StatusCode) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode ?? 200;
        httpContext.Response.ContentType = "application/json";

        using var rent = BufferWriterPool<byte>.Shared.Get();
        using var writer = new Utf8JsonWriter(rent.Value);
        JsonSerializer.Serialize(writer, Value, JsonResult.GetJsonOptions(httpContext.RequestServices));

        if (!httpContext.Response.HasStarted) await httpContext.Response.StartAsync();

        await httpContext.Response.BodyWriter.WriteAsync(rent.Value.WrittenMemory);
        await httpContext.Response.BodyWriter.FlushAsync();
    }
}

