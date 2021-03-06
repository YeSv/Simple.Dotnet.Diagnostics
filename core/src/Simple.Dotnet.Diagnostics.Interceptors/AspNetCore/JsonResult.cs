using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Simple.Dotnet.Utilities.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Simple.Dotnet.Diagnostics.Interceptors.AspNetCore;

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

        var jsonOptions = JsonResult.GetJsonOptions(httpContext.RequestServices);

        using var writer = BufferWriterPool<byte>.Shared.Get();
        JsonSerializer.Serialize(new Utf8JsonWriter(writer.Value, new() { Indented = jsonOptions?.WriteIndented ?? false, SkipValidation = false, Encoder = jsonOptions?.Encoder }), Value, jsonOptions);

        if (!httpContext.Response.HasStarted) await httpContext.Response.StartAsync();

        await httpContext.Response.BodyWriter.WriteAsync(writer.Value.WrittenMemory);
        await httpContext.Response.BodyWriter.FlushAsync();
    }
}