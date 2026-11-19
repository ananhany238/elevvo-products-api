# ---------- Stage 1: builder (full SDK, compiles and publishes the app) ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder
WORKDIR /src

# Copy only the project file first so the restore layer is cached until dependencies change
COPY src/ProductsApi/ProductsApi.csproj src/ProductsApi/
RUN dotnet restore src/ProductsApi/ProductsApi.csproj

COPY src/ProductsApi/ src/ProductsApi/
RUN dotnet publish src/ProductsApi/ProductsApi.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Stage 2: runner (small runtime-only image, only the published output) ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runner
WORKDIR /app

COPY --from=builder /app/publish .

# .NET 10 images listen on port 8080 and include a non-root user called "app"
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "ProductsApi.dll"]
