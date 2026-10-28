using DcLedger.Infrastructure.Storage;

namespace DcLedger.Infrastructure.Tests.Storage;

/// <summary>A bucket in a dictionary that records every operation, for layout and idempotency tests.</summary>
internal sealed class InMemoryObjectStore : IObjectStore
{
    private readonly Dictionary<string, StoredObject> _objects = new(StringComparer.Ordinal);
    private readonly List<string> _operations = [];

    public IReadOnlyDictionary<string, StoredObject> Objects => _objects;

    public IReadOnlyList<string> Operations => _operations;

    public void Seed(string key, string utf8) => _objects[key] = new StoredObject(System.Text.Encoding.UTF8.GetBytes(utf8), "application/json");

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        _operations.Add("HEAD " + key);
        return Task.FromResult(_objects.ContainsKey(key));
    }

    public Task<byte[]?> GetAsync(string key, int maxBytes, CancellationToken cancellationToken)
    {
        _operations.Add("GET " + key);
        return Task.FromResult(_objects.TryGetValue(key, out var stored) ? stored.Content.ToArray() : null);
    }

    public Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        _operations.Add("PUT " + key);
        _objects[key] = new StoredObject(content.ToArray(), contentType);
        return Task.CompletedTask;
    }
}

internal sealed record StoredObject(ReadOnlyMemory<byte> Content, string ContentType)
{
    public string Text => System.Text.Encoding.UTF8.GetString(Content.Span);
}
