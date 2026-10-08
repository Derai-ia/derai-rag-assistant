FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Derai.RagAssistant.Api/Derai.RagAssistant.Api.csproj
RUN dotnet publish src/Derai.RagAssistant.Api/Derai.RagAssistant.Api.csproj -c Release -o /out --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
RUN adduser --disabled-password --gecos '' appuser && apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
USER appuser
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s CMD curl -f http://localhost:8080/health/ready || exit 1
ENTRYPOINT ["dotnet","Derai.RagAssistant.Api.dll"]
