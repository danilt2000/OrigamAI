FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY OrigamAI/OrigamAI.csproj OrigamAI/
RUN dotnet restore OrigamAI/OrigamAI.csproj

COPY OrigamAI/ OrigamAI/
RUN dotnet publish OrigamAI/OrigamAI.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080
ENTRYPOINT ["dotnet", "OrigamAI.dll"]
