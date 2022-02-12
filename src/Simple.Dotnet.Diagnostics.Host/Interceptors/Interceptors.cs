using Simple.Dotnet.Diagnostics.Interceptors;
using Simple.Dotnet.Utilities.Buffers;
using Simple.Dotnet.Utilities.Results;
using System.Reflection;

namespace Simple.Dotnet.Diagnostics.Host.Interceptors;

public readonly record struct LoadInterceptorsCommand(string BasePath);

public static class Interceptors
{
    public static readonly string InterceptorsDir = "interceptors";

    public static IInterceptor[] Load(in LoadInterceptorsCommand command)
    {
        var interceptorsDirs = Directory.GetDirectories(command.BasePath);
        if (interceptorsDirs is null or { Length: 0 })
        {
            //logger.LogInformation("Did not find any interceptor. Skipping. SearchPath: {InterceptorsPath}", Path.Combine(command.BasePath, InterceptorsDir));
            return Array.Empty<IInterceptor>();
        }

        using var rent = new Rent<IInterceptor>();
        foreach (var interceptorDir in interceptorsDirs)
        {
            var (assembly, assemblyError) = GetAssemblyFromContext(interceptorDir);
            if (assemblyError != null)
            {
                //logger.LogWarning(assemblyError, "Failed to extract interceptor's assembly. Interceptors path: {InterceptorsPath}", interceptorDir);
                continue;
            }

            if (assembly is null)
            {
                //logger.LogWarning("Failed to retrieve assembly from load context. Interceptors path: {InterceptorsPath}", interceptorDir);
                continue;
            }

            var (interceptors, interceptorsError) = LoadFromAssembly(assembly);
            if (interceptorsError != null)
            {
                //logger.LogWarning(interceptorsError, "Failed to extract interceptors from assembly. Interceptors path: {InterceptorsPath}", interceptorDir);
                continue;
            }

            foreach (var interceptor in interceptors ?? Array.Empty<IInterceptor>()) rent.Append(interceptor);
        }

        return rent.WrittenSpan.ToArray();
    }

    static UniResult<IInterceptor[], Exception> LoadFromAssembly(Assembly assembly)
    {
        try
        {
            var interceptors = assembly.GetTypes()
                .Where(t => typeof(IInterceptor).IsAssignableFrom(t))
                .Select(t => t.GetConstructor(Array.Empty<Type>())?.Invoke(Array.Empty<object>()) as IInterceptor)
                .Where(i => i != null)
                .ToArray();

            return new(interceptors!);
        }
        catch (Exception ex)
        {
            return new(ex);
        }
    }

    static UniResult<Assembly?, Exception> GetAssemblyFromContext(string interceptorDir)
    {
        try
        {
            var dllName = Path.GetDirectoryName(interceptorDir);
            if (string.IsNullOrWhiteSpace(dllName)) return new(new Exception($"Failed to extract interceptor name from path: {interceptorDir}"));

            var dllPath = Path.Combine(interceptorDir, $"{dllName}.dll");
            if (!File.Exists(dllPath)) return new(new Exception($"Did not find interceptor's dll file. Searched location: {dllPath}"));

            return new(new InterceptorLoadContext(dllPath).LoadFromAssemblyName(new AssemblyName(dllName)));
        }
        catch (Exception ex)
        {
            return new(ex);
        }
    }

}
