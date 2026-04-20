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

/// <summary>
/// In-memory call context store using ConcurrentDictionary.
/// 
/// SCALING LIMITATION: This store is local to a single process. Running multiple
/// replicas will cause WebSocket audio handlers to fail when the ACS callback
/// lands on a different replica than the one that placed the call.
/// 
/// For multi-replica scaling, replace with Azure Cache for Redis:
///   - Key: callConnectionId, Value: serialized CallContext, TTL: 1 hour
///   - Register as ICallContextStore interface for easy swap
/// </summary>
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
