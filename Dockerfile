FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR /app
COPY . .
ARG Version
ARG AssemblyVersion
ARG FileVersion
ARG InformationalVersion
RUN dotnet publish -c Release -o out -r linux-x64 \
    -p:Version=$Version \
    -p:AssemblyVersion=$AssemblyVersion \
    -p:FileVersion=$FileVersion \
    -p:InformationalVersion=$InformationalVersion

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build-env /app/out .
# supervisordを実行
ENTRYPOINT ["dotnet", "Mastfollow.dll"]
