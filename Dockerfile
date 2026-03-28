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

# 5017: gRPC (HTTP/2)
# 5018: Web UI (HTTP/1.1)
EXPOSE 5017
EXPOSE 5018

ENV ASPNETCORE_Kestrel__Endpoints__Grpc__Url=http://+:5017
ENV ASPNETCORE_Kestrel__Endpoints__Grpc__Protocols=Http2
ENV ASPNETCORE_Kestrel__Endpoints__Ui__Url=http://+:5018
ENV ASPNETCORE_Kestrel__Endpoints__Ui__Protocols=Http1

ENTRYPOINT ["./FirestoreSharp.Server"]
CMD ["--store", "InMemory"]
