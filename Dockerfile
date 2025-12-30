FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["ConsoleApp2.csproj", "./"]
RUN dotnet restore "ConsoleApp2.csproj"
COPY . .
RUN dotnet publish "ConsoleApp2.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ConsoleApp2.dll"]
