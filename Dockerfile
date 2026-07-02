FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Middleware.Api/Middleware.Api.csproj", "src/Middleware.Api/"]
COPY ["src/Middleware.Application/Middleware.Application.csproj", "src/Middleware.Application/"]
COPY ["src/Middleware.Domain/Middleware.Domain.csproj", "src/Middleware.Domain/"]
COPY ["src/Middleware.Infrastructure/Middleware.Infrastructure.csproj", "src/Middleware.Infrastructure/"]
RUN dotnet restore "./src/Middleware.Api/Middleware.Api.csproj"
COPY ["global.json", "Directory.Build.props", "./"]
COPY ["src/", "src/"]
WORKDIR "/src/src/Middleware.Api"
RUN dotnet build "./Middleware.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Middleware.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
USER $APP_UID
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Middleware.Api.dll"]
