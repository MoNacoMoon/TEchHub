# --- Этап сборки (Build) ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем проекты и восстанавливаем зависимости
COPY ["EduCabinet.Web/EduCabinet.Web.csproj", "EduCabinet.Web/"]
COPY ["EduCabinet.Tests/EduCabinet.Tests.csproj", "EduCabinet.Tests/"]
COPY ["EduCabinet.sln", "./"]
RUN dotnet restore "EduCabinet.Web/EduCabinet.Web.csproj"

# Копируем весь исходный код
COPY . .

# Собираем и публикуем веб-приложение
WORKDIR "/src/EduCabinet.Web"
RUN dotnet publish "EduCabinet.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# --- Финальный этап запуска (Runtime) ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Создаем папку для постоянного хранилища Amvera
RUN mkdir -p /data

EXPOSE 80
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "EduCabinet.Web.dll"]
