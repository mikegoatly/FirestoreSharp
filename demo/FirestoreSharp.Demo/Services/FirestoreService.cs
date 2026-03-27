using FirestoreSharp.Demo.Models;

using Google.Cloud.Firestore;

namespace FirestoreSharp.Demo.Services;

public sealed class FirestoreService
{
    private const string EmulatorHost = "localhost:5017";
    private const string ProjectId = "demo-project";
    private const string CollectionName = "todos";

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
    /// Starts a real-time listener on the todos collection.
    /// <paramref name="onChanged"/> is called each time a snapshot arrives.
    /// Returns the <see cref="FirestoreChangeListener"/> (dispose to stop listening).
    /// </summary>
    public FirestoreChangeListener Listen(Action<QuerySnapshot> onChanged)
    {
        return Collection.Listen(onChanged);
    }
}
