FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Agent.Orchestrator.Api.csproj", "./"]
RUN dotnet restore "Agent.Orchestrator.Api.csproj"
COPY . .
RUN dotnet build "Agent.Orchestrator.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Agent.Orchestrator.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Agent.Orchestrator.Api.dll"]