using Microsoft.AspNetCore.Builder;

namespace Simple.Dotnet.Diagnostics.Interceptors;

public interface IInterceptor
{
    public void Intercept(WebApplicationBuilder builder);

    public void Intercept(WebApplication application);
}