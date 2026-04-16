using System.Collections.Concurrent;

namespace FabricVoiceCallAgent.Models;

public class CallContext
{
    public string CallConnectionId { get; set; } = string.Empty;
    public string ExecSummary { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public AlertPayload? Alert { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class CallContextStore
{
    private readonly ConcurrentDictionary<string, CallContext> _store = new();

    public void Set(string callConnectionId, CallContext context)
    {
        _store[callConnectionId] = context;
    }

    public CallContext? Get(string callConnectionId)
    {
        _store.TryGetValue(callConnectionId, out var context);
        return context;
    }

    public bool Remove(string callConnectionId)
    {
        return _store.TryRemove(callConnectionId, out _);
    }

    public IEnumerable<CallContext> GetAll()
    {
        return _store.Values;
    }

    public string? GetAnyActiveCallConnectionId()
    {
        return _store.Keys.FirstOrDefault();
    }
}
