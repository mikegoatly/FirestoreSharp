using FirestoreSharp.Core;
using FirestoreSharp.Server.Services;
using FirestoreSharp.Storage.InMemory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();

var app = builder.Build();

app.MapGrpcService<FirestoreService>();
app.MapGet("/", () => "FirestoreSharp gRPC emulator");

app.Run();
