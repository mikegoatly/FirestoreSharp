using FirestoreSharp.Core;
using FirestoreSharp.Core.Stores.InMemory;
using FirestoreSharp.Core.Transactions;
using FirestoreSharp.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();
builder.Services.AddSingleton<IDocumentService, DocumentService>();
builder.Services.AddSingleton<ITransactionManager, TransactionManager>();

var app = builder.Build();

app.MapGrpcService<FirestoreGrpcService>();
app.MapGet("/", () => "FirestoreSharp gRPC emulator");

app.Run();
