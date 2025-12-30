FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["ConsoleApp2.csproj", "./"]
RUN dotnet restore "ConsoleApp2.csproj"
COPY . .
RUN dotnet publish "ConsoleApp2.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
# Добавляем установку необходимых библиотек для сервера
RUN apt-get update && apt-get install -y libicu-dev
WORKDIR /app
COPY --from=build /app/publish .
# Render сам назначит PORT, наш код его подхватит
ENTRYPOINT ["dotnet", "ConsoleApp2.dll"]
