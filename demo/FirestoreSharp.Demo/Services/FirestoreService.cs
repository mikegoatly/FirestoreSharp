using FirestoreSharp.Demo.Models;

using Google.Cloud.Firestore;

namespace FirestoreSharp.Demo.Services;

public sealed class FirestoreService
{
    private const string EmulatorHost = "localhost:5017";
    private const string ProjectId = "local";
    private const string CollectionName = "todos";
    private const string SubTasksCollectionName = "SubTasks";

    private readonly FirestoreDb _db;

    public FirestoreService()
    {
        // Point the official Firestore client at our local emulator.
        Environment.SetEnvironmentVariable("FIRESTORE_EMULATOR_HOST", EmulatorHost);

        _db = new FirestoreDbBuilder
        {
            ProjectId = ProjectId,
            EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOnly,
        }.Build();
    }

    private CollectionReference Collection => _db.Collection(CollectionName);
    private CollectionReference SubTasksCollection(string todoId) =>
        Collection.Document(todoId).Collection(SubTasksCollectionName);

    public async Task<List<TodoItem>> GetAllAsync()
    {
        var snapshot = await Collection.GetSnapshotAsync().ConfigureAwait(false);
        return snapshot.Documents
            .Select(d => d.ConvertTo<TodoItem>())
            .ToList();
    }

    public async Task<string> CreateAsync(TodoItem item)
    {
        var docRef = await Collection.AddAsync(item).ConfigureAwait(false);
        return docRef.Id;
    }

    public async Task UpdateAsync(TodoItem item)
    {
        if (item.Id is null)
        {
            return;
        }

        var docRef = Collection.Document(item.Id);
        await docRef.SetAsync(item, SetOptions.Overwrite).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id)
    {
        var docRef = Collection.Document(id);
        await docRef.DeleteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Creates multiple todo items in a single atomic transaction.
    /// </summary>
    public async Task<IReadOnlyList<string>> CreateBatchAsync(IReadOnlyList<TodoItem> items)
    {
        var refs = items.Select(_ => Collection.Document()).ToList();

        await _db.RunTransactionAsync(transaction =>
        {
            foreach (var (item, docRef) in items.Zip(refs))
            {
                transaction.Create(docRef, item);
            }

            return Task.CompletedTask;
        }).ConfigureAwait(false);

        return refs.Select(r => r.Id).ToList();
    }

    /// <summary>
    /// Starts a real-time listener on the todos collection.
    /// <paramref name="onChanged"/> is called each time a snapshot arrives.
    /// Returns the <see cref="FirestoreChangeListener"/> (dispose to stop listening).
    /// </summary>
    public FirestoreChangeListener Listen(Action<QuerySnapshot> onChanged)
    {
        return Collection.Listen(onChanged);
    }

    // ── SubTask methods ──────────────────────────────────────────────────────

    public async Task<List<SubTaskItem>> GetSubTasksAsync(string todoId)
    {
        var snapshot = await SubTasksCollection(todoId).GetSnapshotAsync().ConfigureAwait(false);
        return snapshot.Documents
            .Select(d => d.ConvertTo<SubTaskItem>())
            .ToList();
    }

    public async Task<string> CreateSubTaskAsync(string todoId, SubTaskItem item)
    {
        var docRef = await SubTasksCollection(todoId).AddAsync(item).ConfigureAwait(false);
        return docRef.Id;
    }

    public async Task UpdateSubTaskAsync(string todoId, SubTaskItem item)
    {
        if (item.Id is null) return;
        var docRef = SubTasksCollection(todoId).Document(item.Id);
        await docRef.SetAsync(item, SetOptions.Overwrite).ConfigureAwait(false);
    }

    public async Task DeleteSubTaskAsync(string todoId, string subTaskId)
    {
        var docRef = SubTasksCollection(todoId).Document(subTaskId);
        await docRef.DeleteAsync().ConfigureAwait(false);
    }

    public FirestoreChangeListener ListenSubTasks(string todoId, Action<QuerySnapshot> onChanged)
    {
        return SubTasksCollection(todoId).Listen(onChanged);
    }
}
