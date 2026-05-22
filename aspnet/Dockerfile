# ==========================================
# 階段一：Build 階段 (使用肥大但必要的 SDK)
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1. 精準複製 csproj，這樣只要沒加新套件，下一次 build 就會直接跳過 restore 步驟
COPY DemoAPI.csproj ./
RUN dotnet restore

# 2. 複製剩餘原始碼 (會被 .dockerignore 過濾掉 bin/obj)
COPY . .

# 3. 執行發布，並加上 --no-restore 減少重複開銷
RUN dotnet publish "DemoAPI.csproj" -c Release -o /app/publish --no-restore

# ==========================================
# 階段二：Runtime 階段 (改用極小的執行環境，僅約 200MB)
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# 從 build 階段把打包好的成品拿過來
COPY --from=build /app/publish .

EXPOSE 80

ENTRYPOINT ["dotnet", "DemoAPI.dll"]