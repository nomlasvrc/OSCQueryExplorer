namespace OSCQueryExplorer.Core.History;

public sealed class HistoryStore
{
    private readonly object _gate = new();
    private readonly HistoryEntry?[] _items;
    private int _start;
    private int _count;
    private long _nextId;
    private bool _recordOsc = true;
    public bool RecordOsc
    {
        get { lock (_gate) return _recordOsc; }
        set { lock (_gate) _recordOsc = value; }
    }
    public int Capacity => _items.Length;
    public event EventHandler? Changed;

    public HistoryStore(int capacity = 10_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _items = new HistoryEntry[capacity];
    }

    public HistoryEntry? Add(Func<long, HistoryEntry> factory)
    {
        HistoryEntry entry;
        lock (_gate)
        {
            entry = factory(_nextId + 1);
            if (entry.Kind == HistoryKind.Osc && !_recordOsc) return null;
            _nextId++;
            var index = (_start + _count) % Capacity;
            if (_count == Capacity) { index = _start; _start = (_start + 1) % Capacity; }
            else _count++;
            _items[index] = entry;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public IReadOnlyList<HistoryEntry> Snapshot()
    {
        lock (_gate)
        {
            var result = new HistoryEntry[_count];
            for (var i = 0; i < _count; i++) result[i] = _items[(_start + i) % Capacity]!;
            return result;
        }
    }

    public void Clear()
    {
        lock (_gate) { Array.Clear(_items); _start = 0; _count = 0; }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
