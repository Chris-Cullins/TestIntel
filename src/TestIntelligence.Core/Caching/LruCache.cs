using System;
using System.Collections.Generic;

namespace TestIntelligence.Core.Caching
{
    /// <summary>
    /// Simple thread-safe LRU cache with fixed capacity and optional per-entry TTL.
    /// Optimized for frequent reads and moderate writes.
    /// </summary>
    public class LruCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly TimeSpan? _ttl;
        private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
        private readonly LinkedList<Entry> _list;
        private readonly object _lock = new();
        private readonly IEqualityComparer<TKey> _comparer;

        private class Entry
        {
            public TKey Key = default!;
            public TValue? Value;
            public DateTime? ExpiresAt;
        }

        public LruCache(int capacity, TimeSpan? ttl = null, IEqualityComparer<TKey>? comparer = null)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _ttl = ttl;
            _comparer = comparer ?? EqualityComparer<TKey>.Default;
            _map = new Dictionary<TKey, LinkedListNode<Entry>>(_comparer);
            _list = new LinkedList<Entry>();
        }

        public int Count
        {
            get
            {
                lock (_lock) { return _map.Count; }
            }
        }

        public bool TryGetValue(TKey key, out TValue? value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    if (node.Value.ExpiresAt.HasValue && node.Value.ExpiresAt.Value < DateTime.UtcNow)
                    {
                        // Expired
                        _list.Remove(node);
                        _map.Remove(key);
                        value = default;
                        return false;
                    }

                    // Move to front (most recently used)
                    _list.Remove(node);
                    _list.AddFirst(node);
                    value = node.Value.Value!;
                    return true;
                }
                value = default;
                return false;
            }
        }

        public void Set(TKey key, TValue? value)
        {
            lock (_lock)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    node.Value.Value = value;
                    node.Value.ExpiresAt = _ttl.HasValue ? DateTime.UtcNow.Add(_ttl.Value) : null;
                    _list.Remove(node);
                    _list.AddFirst(node);
                    return;
                }

                var entry = new Entry
                {
                    Key = key,
                    Value = value,
                    ExpiresAt = _ttl.HasValue ? DateTime.UtcNow.Add(_ttl.Value) : null
                };
                var newNode = new LinkedListNode<Entry>(entry);
                _list.AddFirst(newNode);
                _map[key] = newNode;

                if (_map.Count > _capacity)
                {
                    // Evict LRU
                    var tail = _list.Last;
                    if (tail != null)
                    {
                        _list.RemoveLast();
                        _map.Remove(tail.Value.Key);
                    }
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _map.Clear();
                _list.Clear();
            }
        }
    }
}

