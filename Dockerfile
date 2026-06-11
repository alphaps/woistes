FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Woistes.Domain/Woistes.Domain.csproj src/Woistes.Domain/
COPY src/Woistes.CtfParser/Woistes.CtfParser.csproj src/Woistes.CtfParser/
COPY src/Woistes.Infrastructure/Woistes.Infrastructure.csproj src/Woistes.Infrastructure/
COPY src/Woistes.Api/Woistes.Api.csproj src/Woistes.Api/
RUN dotnet restore src/Woistes.Api/Woistes.Api.csproj

COPY src/ src/
RUN dotnet publish src/Woistes.Api/Woistes.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --start-period=5s \
    CMD curl -f http://localhost:8080/health || exit 1

USER $APP_UID
ENTRYPOINT ["dotnet", "Woistes.Api.dll"]
