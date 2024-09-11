using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections;
using System.Diagnostics.Contracts;

namespace ChunkedList;

public static class ChunkedList
{
    public static ChunkedList<T> Create<T>(ReadOnlySpan<T> source) => Create<T>(null, source);
    public static ChunkedList<T> Create<T>(IEqualityComparer<T>? equalityComparer, ReadOnlySpan<T> source)
    {
        if (source.Length == 0)
        {
            return equalityComparer is null || ReferenceEquals(equalityComparer, ChunkedList<T>.Empty.Comparer) ? ChunkedList<T>.Empty : new EmptyChunkedList<T>(equalityComparer);
        }

        var list = new ChunkedList<T>(4, 0, equalityComparer ?? EqualityComparer<T>.Default);

        foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}

#if NET8_0_OR_GREATER
[CollectionBuilder(typeof(ChunkedList), nameof(ChunkedList.Create))]
#endif
[DebuggerDisplay($"Count = {{{nameof(Count)}}}")]
public class ChunkedList<T> : IList<T>, IReadOnlyList<T>, IList
{
    private readonly byte _chunkByteSize;
    private readonly int _chunkSize;
    private readonly int _indexInChunkMask;
    private T?[]?[] _chunks;    

    [ContractPublicPropertyName(nameof(Count))]
    private int _count;
    private int _version;
    private object? _syncRoot;
    private int _nextChunkIndex;
    private int _nextIndexInChunk;

    public ChunkedList(byte chunkByteSize, int initialCapacity)
        : this(chunkByteSize, initialCapacity, EqualityComparer<T>.Default) {}
    public ChunkedList(byte chunkByteSize, int initialCapacity, IEqualityComparer<T> comparer)
    {
        Comparer = comparer;
        _chunkByteSize = chunkByteSize;
        _chunkSize = 1 << chunkByteSize;
        _indexInChunkMask = (1 << chunkByteSize) -1;

        _chunks = initialCapacity == 0 ? [] : new T?[]?[GetChunkCount(initialCapacity, chunkByteSize)];
    }

    public ChunkedList(IEnumerable<T> collection, byte chunkByteSize, int initialCapacity, IEqualityComparer<T>? comparer = default) 
        : this(chunkByteSize, initialCapacity, comparer ?? EqualityComparer<T>.Default)
    {
        Debug.Assert(collection != null);
        
        using var enumerator = collection.GetEnumerator();
        while (enumerator.MoveNext())
        {
            Add(enumerator.Current);
        }
    }

    protected ChunkedList(byte chunkByteSize, int initialCapacity, bool readOnly, IEqualityComparer<T>? equalityComparer = null) 
        : this(chunkByteSize, initialCapacity, equalityComparer ?? EqualityComparer<T>.Default)
    {
        IsReadOnly = readOnly;
    }

    public static ChunkedList<T> Empty { get; } = new EmptyChunkedList<T>();

    public IEqualityComparer<T> Comparer { get; }

    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetAtIndex(index);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            GetAtIndex(index) = value;
            Interlocked.Increment(ref _version);
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set
        {
            if(value is null)
            {
                return;
            }

            try
            {
                GetAtIndex(index) = (T)value;
                Interlocked.Increment(ref _version);
            }
            catch (InvalidCastException)
            {
                // do nothing
            }
        }
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count;
    }

    public bool IsReadOnly { get; private set; } = false;

    public bool IsFixedSize => false;

    public bool IsSynchronized => false;

    public object SyncRoot
    {
        get
        {
            if (_syncRoot is null)
            {
                Interlocked.CompareExchange<object?>(ref _syncRoot, new object(), null);
            }

            return _syncRoot!;
        }
    }

    public void Add(T item) => AddItemCore(item);

    public int Add(object? value)
    {
        if (value is T typedValue)
        {
            Add(typedValue);
            return IndexOf(typedValue);
        }

        return -1;
    }

    public void Clear() => ClearItemsCore();

    public bool Contains(T item) => IndexOf(item) >= 0;
    public bool Contains(object? value) => value is T typedValue && Contains(typedValue);

    public void CopyTo(T[] array, int arrayIndex) => CopyToCore(array, arrayIndex);

    public void CopyTo(Array array, int index)
    {
        if (array is T[] typedArray)
        {
            CopyTo(typedArray, index);
        }

        throw new ArgumentException($"Array is not of type {typeof(T[]).Name}", nameof(array));
    }

    public IEnumerator<T> GetEnumerator() => GetEnumeratorCore();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumeratorCore();

    public int IndexOf(T item) => FindItemIndex(item);
    public int IndexOf(object? value) => value is T typedValue ? IndexOf(typedValue) : -1;

    public void Insert(int index, T item) => InsertAtCore(index, item);

    public void Insert(int index, object? value)
    {
        if (value is T typedValue)
        {
            Insert(index, typedValue);
        }

        throw new NotSupportedException($"{nameof(value)} is not of type: {typeof(T).Name}");
    }

    public bool Remove(T item)
    {
        Contract.Assert(item is not null);

        try
        {
            return RemoveByValueCore(item);
        }
        catch
        {
            return false;
        }
    }

    public void Remove(object? value)
    {
        if (value is T typedValue)
        {
            Remove(typedValue);
        }

        throw new NotSupportedException($"{nameof(value)} is not of type: {typeof(T).Name}");
    }

    public void RemoveAt(int index) => RemoveAtIndexCore(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void AddItemCore(T item)
    {
        var chunkIndex = _nextChunkIndex;
        var indexInChunk = _nextIndexInChunk;
        var chunkCount = _chunks.Length;
        if(chunkIndex >= chunkCount)
            Array.Resize(ref _chunks, chunkCount == 0 ? 1 : chunkCount * 2);
        
        var chunk = _chunks[chunkIndex] ??= new T[_chunkSize];
        chunk[indexInChunk] = item;

        Interlocked.Increment(ref _count);
        Interlocked.Increment(ref _version);
        Interlocked.Increment(ref _nextIndexInChunk);
        if(_nextIndexInChunk == _chunkSize)
        {
            _nextIndexInChunk = 0;
            Interlocked.Increment(ref _nextChunkIndex);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected virtual void ClearItemsCore()
    {
        if(RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            var count = Count;
            if(count > 0)
            {
                var chunkCount = GetChunkCount(count, _chunkByteSize);
                for(var i = 0; i < chunkCount - 1; i++)
                    Array.Clear(_chunks[i]!, 0, _chunkSize);
                Array.Clear(_chunks[chunkCount - 1]!, 0, ((count - 1) & _indexInChunkMask) + 1);
            }
        }

        _count = 0;
        _nextChunkIndex = 0;
        _nextIndexInChunk = 0;
        Interlocked.Increment(ref _version);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected virtual void CopyToCore(T[] array, int arrayIndex)
    {
        Contract.Assert(arrayIndex >= 0 && arrayIndex < _count);

        using var enumerator = GetEnumerator();
        var skipped = 0;
        while (enumerator.MoveNext())
        {
            if (arrayIndex >= skipped)
            {
                array[arrayIndex - skipped] = enumerator.Current;
            }

            skipped++;
        }
    }

    private protected virtual IEnumerator<T> GetEnumeratorCore() => new ChunkedListEnumerator(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected virtual ref T GetAtIndex(int index)
    {
        Contract.Assert(index >= 0 && index < Count);

        var chunkIndex = index >> _chunkByteSize;
        var indexInChunk = index & _indexInChunkMask;

        return ref _chunks[chunkIndex]![indexInChunk]!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected virtual void InsertAtCore(int index, T item)
    {
        Contract.Assert(index >= 0 && index < Count);

        var chunkCount = GetChunkCount(Count, _chunkByteSize);
        var lastChunkSize = ((Count - 1) & _indexInChunkMask) + 1;
        var chunkIndex = index >> _chunkByteSize;
        var indexInChunk = index & _indexInChunkMask;
        var chunks = new Memory<T>[chunkCount];
        for(var i = 0; i < chunkCount - 1; i++)
        {
            chunks[i] = _chunks[i]!.AsMemory()!;
            _chunks[i] = new T[_chunkSize];
        }
        chunks[^1] = _chunks[chunkCount -1]!.AsMemory(0, lastChunkSize)!;
        _chunks[chunkCount - 1] = new T[_chunkSize];
        _count = 0;
        _nextChunkIndex = 0;
        _nextIndexInChunk = 0;

        for(var i = 0; i < chunks.Length; i++)
        {
            for(var j = 0; j < chunks[i].Span.Length; j++)
            {
                if(i == chunkIndex && j == indexInChunk)
                {
                    Add(item);
                }

                Add(chunks[i].Span[j]);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected virtual void RemoveAtIndexCore(int index)
    {
        Contract.Assert(index >= 0 && index < Count);

        var chunkCount = GetChunkCount(Count, _chunkByteSize);
        var lastChunkSize = ((Count - 1) & _indexInChunkMask) + 1;
        var chunkIndex = index >> _chunkByteSize;
        var indexInChunk = index & _indexInChunkMask;
        var chunks = new Memory<T>[chunkCount];
        for(var i = 0; i < chunkCount - 1; i++)
        {
            chunks[i] = _chunks[i]!.AsMemory()!;
            _chunks[i] = new T[_chunkSize];
        }
        chunks[^1] = _chunks[chunkCount -1]!.AsMemory(0, lastChunkSize)!;
        _chunks[chunkCount - 1] = new T[_chunkSize];
        _count = 0;
        _nextChunkIndex = 0;
        _nextIndexInChunk = 0;

        for(var i = 0; i < chunks.Length; i++)
        {
            for(var j = 0; j < chunks[i].Span.Length; j++)
            {
                if(i == chunkIndex && j == indexInChunk)
                {
                    continue;
                }

                Add(chunks[i].Span[j]);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected virtual bool RemoveByValueCore(T value)
    {
        if (value is null)
            return false;

        var currentIndex = FindItemIndex(value);
        var wasFound = currentIndex >= 0;

        if (!wasFound)
        {
            return false;
        }

        RemoveAtIndexCore(currentIndex);
        return true;
    }

    private protected virtual int CountCore => _count;

    private protected virtual int FindItemIndex(T item)
    {
        Debug.Assert(item is not null);

        var currentIndex = 0;
        using var enumerator = GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (Comparer.Equals(enumerator.Current, item))
            {
                return currentIndex;
            }

            currentIndex++;
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetChunkCount(int count, byte chunkByteSize) => ((count - 1) >> chunkByteSize) + 1;

    private struct ChunkedListEnumerator(ChunkedList<T> list) : IEnumerator<T>
    {
        private readonly int _chunkSize = list._chunkSize;
        private readonly T?[]?[] _chunks = list._chunks;
        private readonly int _count = list._count;
        private int _index = -1;
        private int _chunkIndex;
        private T?[]? _currentChunk = list._count == 0 ? null : list._chunks[0];
        private int _indexInChunk = -1;
        private readonly int _version = list._version;

        public readonly T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _currentChunk![_indexInChunk]!;
        }

        readonly object? IEnumerator.Current => Current;

        public readonly void Dispose() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            CheckVersion();
            
            if(_index++ >= _count)
                return false;
            
            if(_indexInChunk++ == _chunkSize)
            {
                _indexInChunk = 0;
                _currentChunk = _chunks[_chunkIndex++];
            }

            return true;
        }

        public void Reset()
        {
            _index = -1;
            _chunkIndex = 0;
            _currentChunk = list._count == 0 ? null : list._chunks[0];
            _indexInChunk = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly void CheckVersion()
        {
            if (_version != list._version)
            {
                throw new InvalidOperationException("Collection was modified during enumeration");
            }
        }
    }
}
