# Build 階段
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# 先複製 csproj（build cache optimization🔥）
COPY ["DemoAPI.csproj", "./"]

RUN dotnet restore

# 再複製其他檔案
COPY . .

# publish
RUN dotnet publish -c Release -o /app/publish

# Runtime 階段
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 80

ENTRYPOINT ["dotnet", "DemoAPI.dll"]