using System.Collections.ObjectModel;
using System.Diagnostics;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FirestoreSharp.Demo.Models;
using FirestoreSharp.Demo.Services;

using Google.Cloud.Firestore;

namespace FirestoreSharp.Demo.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly FirestoreService _firestore;
    private FirestoreChangeListener? _listener;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private TodoItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _newTitle = string.Empty;

    [ObservableProperty]
    private string _statusText = "Starting…";

    public ObservableCollection<TodoItemViewModel> Items { get; } = [];
    public ObservableCollection<string> ActivityLog { get; } = [];

    public MainViewModel(FirestoreService firestore)
    {
        _firestore = firestore;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            StatusText = "Loading todos…";
            var stopwatch = Stopwatch.StartNew();
            var items = await _firestore.GetAllAsync().ConfigureAwait(false);
            var elapsed = stopwatch.ElapsedMilliseconds;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var item in items)
                {
                    Items.Add(new TodoItemViewModel(item));
                }
                StatusText = $"Loaded {items.Count} todo(s) in {elapsed}ms. Listener active.";
            });

            StartListener();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText = $"Error: {ex.Message}";
                LogActivity($"ERROR: {ex.Message}");
            });
        }
    }

    private void StartListener()
    {
        _listener = _firestore.Listen(snapshot =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var change in snapshot.Changes)
                {
                    var item = change.Document.ConvertTo<TodoItem>();
                    var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

                    switch (change.ChangeType)
                    {
                        case DocumentChange.Type.Added:
                            if (FindById(change.Document.Id) is null)
                            {
                                Items.Add(new TodoItemViewModel(item));
                            }
                            LogActivity($"[{timestamp}] ADDED: {item.Title} ({change.Document.Id})");
                            break;

                        case DocumentChange.Type.Modified:
                            var existing = FindById(change.Document.Id);
                            if (existing is not null)
                            {
                                existing.UpdateFrom(item);
                            }
                            LogActivity($"[{timestamp}] MODIFIED: {item.Title} ({change.Document.Id})");
                            break;

                        case DocumentChange.Type.Removed:
                            var removed = FindById(change.Document.Id);
                            if (removed is not null)
                            {
                                Items.Remove(removed);
                            }
                            LogActivity($"[{timestamp}] REMOVED: {item.Title} ({change.Document.Id})");
                            break;
                    }
                }
            });
        });
    }

    [RelayCommand]
    private async Task AddBatchAsync()
    {
        var now = DateTime.Now;
        var items = Enumerable.Range(1, 5)
            .Select(i => new TodoItem { Title = $"Todo {now:HH:mm:ss} #{i}" })
            .ToList();

        try
        {
            var ids = await _firestore.CreateBatchAsync(items).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                for (var i = 0; i < items.Count; i++)
                {
                    items[i].Id = ids[i];
                    Items.Add(new TodoItemViewModel(items[i]));
                }
                LogActivity($"[{DateTime.Now:HH:mm:ss.fff}] BATCH: created {items.Count} todos in one transaction");
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => LogActivity($"ERROR batch create: {ex.Message}"));
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var title = NewTitle.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return;
        }

        var item = new TodoItem { Title = title };
        try
        {
            var id = await _firestore.CreateAsync(item).ConfigureAwait(false);
            item.Id = id;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Items.Add(new TodoItemViewModel(item));
                NewTitle = string.Empty;
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => LogActivity($"ERROR creating: {ex.Message}"));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task SaveAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        try
        {
            await _firestore.UpdateAsync(SelectedItem.ToModel()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => LogActivity($"ERROR saving: {ex.Message}"));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (SelectedItem?.Id is null)
        {
            return;
        }

        try
        {
            var id = SelectedItem.Id;
            await _firestore.DeleteAsync(id).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var toRemove = FindById(id);
                if (toRemove is not null)
                {
                    Items.Remove(toRemove);
                }
                SelectedItem = null;
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => LogActivity($"ERROR deleting: {ex.Message}"));
        }
    }

    public bool HasSelection => SelectedItem is not null;

    private TodoItemViewModel? FindById(string id) =>
        Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));

    private void LogActivity(string message)
    {
        ActivityLog.Insert(0, message);

        // Keep a reasonable cap
        while (ActivityLog.Count > 200)
        {
            ActivityLog.RemoveAt(ActivityLog.Count - 1);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_listener is { } listener)
        {
            await listener.StopAsync();
        }
    }
}

/// <summary>
/// Wraps a <see cref="TodoItem"/> for data binding with change notification.
/// </summary>
public sealed partial class TodoItemViewModel : ObservableObject
{
    [ObservableProperty] private string? _id;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _body = string.Empty;
    [ObservableProperty] private bool _completed;

    public TodoItemViewModel(TodoItem model)
    {
        Id = model.Id;
        Title = model.Title;
        Body = model.Body;
        Completed = model.Completed;
    }

    public void UpdateFrom(TodoItem model)
    {
        Title = model.Title;
        Body = model.Body;
        Completed = model.Completed;
    }

    public TodoItem ToModel() => new()
    {
        Id = Id,
        Title = Title,
        Body = Body,
        Completed = Completed,
    };
}
