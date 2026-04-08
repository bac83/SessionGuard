FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "SessionGuard.sln"
RUN dotnet publish "src/Server.Ui/Server.Ui.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Server.Ui.dll"]
