FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/HundredPeaksTrackers.Api/HundredPeaksTrackers.Api.csproj", "src/HundredPeaksTrackers.Api/"]
RUN dotnet restore "src/HundredPeaksTrackers.Api/HundredPeaksTrackers.Api.csproj"

COPY src/ src/
RUN dotnet publish "src/HundredPeaksTrackers.Api/HundredPeaksTrackers.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "HundredPeaksTrackers.Api.dll"]
