using System.Reflection;
using System.Runtime.Loader;

namespace Simple.Dotnet.Diagnostics.Host.Interceptors;

public sealed class InterceptorLoadContext : AssemblyLoadContext
{
    AssemblyDependencyResolver _resolver;

    public InterceptorLoadContext(string interceptorPath) => _resolver = new(interceptorPath);

    protected override Assembly? Load(AssemblyName assemblyName) => _resolver.ResolveAssemblyToPath(assemblyName) switch
    {
        null => null,
        var p => LoadFromAssemblyPath(p)
    };

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) => _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) switch
    {
        null => IntPtr.Zero,
        var p => LoadUnmanagedDllFromPath(p)
    };
}