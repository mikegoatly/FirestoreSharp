FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

RUN apt-get update && apt-get install -y git clang zlib1g-dev && rm -rf /var/lib/apt/lists/*

COPY .git/ /src/.git/
COPY Directory.Packages.props .
COPY .editorconfig .
COPY src/ .
RUN HUSKY=0 dotnet publish FirestoreSharp.Server/FirestoreSharp.Server.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["./FirestoreSharp.Server"]
CMD ["--store", "InMemory"]
