# Race server image. Build from the repository root (the protocol sources live in the Unity project):
#   docker build -f Server/Dockerfile -t unity-lab-race-server .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Server/Directory.Build.props Server/
COPY Server/src/UnityLab.Protocol Server/src/UnityLab.Protocol
COPY Server/src/RaceServer Server/src/RaceServer
COPY UnityProject/Assets/_Project/Racing/Scripts/Protocol UnityProject/Assets/_Project/Racing/Scripts/Protocol
RUN dotnet publish Server/src/RaceServer/RaceServer.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
# Run as the unprivileged user that ships with the .NET images.
USER app
EXPOSE 7777
ENTRYPOINT ["dotnet", "RaceServer.dll"]
