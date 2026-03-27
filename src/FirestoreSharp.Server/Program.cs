using FirestoreSharp.Core;
using FirestoreSharp.Core.Listeners;
using FirestoreSharp.Core.Stores.InMemory;
using FirestoreSharp.Core.Transactions;
using FirestoreSharp.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();
builder.Services.AddSingleton<IDocumentService, DocumentService>();
builder.Services.AddSingleton<ITransactionManager, TransactionManager>();
builder.Services.AddSingleton<IListenerService, ListenerService>();

// IDocumentChangeNotifier provides just the methods required by services doing change notification. It's implemented
// by IListenerService, so we also inject it pointing to *the same singleton* because we need the communication
// to happen on the same instance.
builder.Services.AddSingleton<IDocumentChangeNotifier>(sp => sp.GetRequiredService<IListenerService>());

var app = builder.Build();

app.MapGrpcService<FirestoreGrpcService>();
app.MapGet("/", () => "FirestoreSharp gRPC emulator");

app.Run();
