using FirestoreSharp.Core;
using FirestoreSharp.Core.Listeners;
using FirestoreSharp.Core.Stores.FileSystem;
using FirestoreSharp.Core.Stores.InMemory;
using FirestoreSharp.Core.Transactions;
using FirestoreSharp.Server.Services;
using FirestoreSharp.Server.UI;

var builder = WebApplication.CreateSlimBuilder(args);

builder.WebHost.UseKestrelHttpsConfiguration();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, FirestoreJsonContext.Default));

builder.Services.AddGrpc();
builder.Services.AddSingleton<IDocumentService, DocumentService>();
builder.Services.AddSingleton<ITransactionManager, TransactionManager>();
builder.Services.AddSingleton<IListenerService, ListenerService>();

// IDocumentChangeNotifier provides just the methods required by services doing change notification. It's implemented
// by IListenerService, so we also inject it pointing to *the same singleton* because we need the communication
// to happen on the same instance.
builder.Services.AddSingleton<IDocumentChangeNotifier>(sp => sp.GetRequiredService<IListenerService>());

var storeArg = args.SkipWhile(a => a != "--store").Skip(1).FirstOrDefault() ?? "InMemory";

if (string.Equals(storeArg, "FileSystem", StringComparison.OrdinalIgnoreCase))
{
    var storePath = args.SkipWhile(a => a != "--store-path").Skip(1).FirstOrDefault()
        ?? Directory.GetCurrentDirectory();

    builder.Services.Configure<FileSystemStorageOptions>(o => o.BasePath = storePath);
    builder.Services.AddSingleton<IDocumentStore, FileSystemDocumentStore>();
}
else if (string.Equals(storeArg, "InMemory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();
}
else
{
    Console.Error.WriteLine($"Unknown store type '{storeArg}'. Valid values: InMemory, FileSystem");
    return 1;
}

var app = builder.Build();

app.MapFirestoreUi();
app.MapGrpcService<FirestoreGrpcService>();
app.MapGet("/", () => Results.Redirect("/ui"));

app.Run();

return 0;
