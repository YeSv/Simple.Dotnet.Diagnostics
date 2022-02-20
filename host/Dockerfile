FROM mcr.microsoft.com/dotnet/sdk:6.0 AS publish

COPY ./src ./src

RUN dotnet publish src/Simple.Dotnet.Diagnostics.Host.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS final

COPY --from=publish /app .

EXPOSE 80

ENTRYPOINT ["dotnet", "Simple.Dotnet.Diagnostics.Host.dll"]
