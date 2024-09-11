using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections;
using System.Diagnostics.Contracts;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

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

        var list = new ChunkedList<T>(equalityComparer ?? EqualityComparer<T>.Default);

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
    private const int ChunkSize = 8192;
    private const int InitialChunkSize = 4;
    private readonly List<List<T>> _chunks = [];

    [ContractPublicPropertyName(nameof(Count))]
    private int _count;
    private int _version;
    private object? _syncRoot;

    public ChunkedList() => Comparer = EqualityComparer<T>.Default;
    public ChunkedList(IEqualityComparer<T> comparer) => Comparer = comparer;
    public ChunkedList(IEnumerable<T> collection, IEqualityComparer<T>? comparer = default)
    {
        Debug.Assert(collection != null);

        Comparer = comparer ?? EqualityComparer<T>.Default;

        if (collection is ICollection<T> typedCollection)
        {
            var count = typedCollection.Count;
            if (count == 0)
            {
                return;
            }

            if (count <= ChunkSize)
            {
                _chunks.Add(new List<T>(typedCollection));
                Interlocked.Add(ref _count, count);
                Interlocked.Increment(ref _version);
                return;
            }
        }

        _count = 0;
        using var enumerator = collection.GetEnumerator();
        while (enumerator.MoveNext())
        {
            Add(enumerator.Current);
        }
    }

    protected ChunkedList(bool readOnly, IEqualityComparer<T>? equalityComparer = null)
    {
        Comparer = equalityComparer ?? EqualityComparer<T>.Default;
        IsReadOnly = readOnly;
    }

    public static ChunkedList<T> Empty { get; } = new EmptyChunkedList<T>();

    public IEqualityComparer<T> Comparer { get; }

    public T this[int index]
    {
        get => GetAtIndex(index);
        set => SetAtIndex(index, value);
    }

    object? IList.this[int index]
    {
        get => this[index];
        set
        {
            if (value is null)
            {
                return;
            }

            try
            {
                SetAtIndex(index, (T)value);
            }
            catch (InvalidCastException)
            {
                // do nothing
            }
        }
    }

    public int Count => _count;

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

    public bool Contains(object? value)
    {
        if (value is T typedValue)
        {
            return Contains(typedValue);
        }

        return false;
    }

    public void CopyTo(T[] array, int arrayIndex)
        => CopyToCore(array, arrayIndex);

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

    public int IndexOf(object? value)
    {
        if (value is T typedValue)
        {
            return IndexOf(typedValue);
        }

        return -1;
    }

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
        Debug.Assert(item is not null);

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
        var chunkBucket = Count / ChunkSize;
        var bucketIndex = Count % ChunkSize;

        if (bucketIndex == 0)
        {
            _chunks.Add(new List<T>((chunkBucket == 0) ? InitialChunkSize : ChunkSize));
        }

        _chunks[chunkBucket].Add(item);
        Interlocked.Increment(ref _count);
        Interlocked.Increment(ref _version);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected virtual void ClearItemsCore()
    {
        _chunks.Clear();
        _count = 0;
        Interlocked.Increment(ref _version);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected virtual void CopyToCore(T[] array, int arrayIndex)
    {
        Debug.Assert(arrayIndex >= 0 && arrayIndex < _count);

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
    private protected virtual T GetAtIndex(int index)
    {
        Debug.Assert(index >= 0 && index < Count);

        var chunkBucket = index / ChunkSize;
        var bucketIndex = index % ChunkSize;

        return _chunks[chunkBucket][bucketIndex];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected virtual void InsertAtCore(int index, T item)
    {
        if (index < 0 || index > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var chunkBucket = Count / ChunkSize;
        var bucketIndex = Count % ChunkSize;

        if (bucketIndex == 0)
        {
            _chunks.Add(new List<T>((chunkBucket == 0) ? InitialChunkSize : ChunkSize));
        }

        // Move elements to make room
        int targetChunk = index / ChunkSize;
        int indexInTargetChunk = index % ChunkSize;
        int chunkContainingLastElement = (Count - 1) / ChunkSize;
        int chunkContainingFirstUnusedSlot = chunkBucket;
        for (int i = chunkContainingLastElement; i >= targetChunk; i--)
        {
            var chunk = _chunks[i];

            // Find chunk size
            int thisChunkSize = ChunkSize;
            if (i == chunkContainingFirstUnusedSlot)
            {
                // Chunk is not full.
                thisChunkSize = Count % ChunkSize;
            }
            else
            {
                // Full chunk. Move last element in chunk to next chunk
                _chunks[i + 1][0] = chunk[ChunkSize - 1];
            }

            // Move rest of the elements in chunk one position forward
            int srcPos = 0;
            if (i == targetChunk)
                srcPos = indexInTargetChunk;
            int length = thisChunkSize - srcPos - (i == chunkContainingFirstUnusedSlot ? 0 : 1);
            if (length > 0)
                ListCopy(chunk, srcPos, chunk, srcPos + 1, length);
        }

        // Finally, set the element and increment the list size
        _chunks[targetChunk][indexInTargetChunk] = item;

        Interlocked.Increment(ref _count);
        Interlocked.Increment(ref _version);
    }

    private static void ListCopy(List<T> srcList, int srcPosition, List<T> destList, int destPosition, int length)
    {
        for (var i = 0; i < length; i++)
        {
            var itemToCopy = srcList[srcPosition + i];
            var indexToPush = destPosition + i;

            if (indexToPush >= destList.Count)
            {
                destList.Add(itemToCopy);
            }
            else
            {
                destList[indexToPush] = itemToCopy;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected virtual void SetAtIndex(int index, T item)
    {
        Debug.Assert(index >= 0 && index < Count);

        var chunkBucket = index / ChunkSize;
        var bucketIndex = index % ChunkSize;
        _chunks[chunkBucket][bucketIndex] = item;
        Interlocked.Increment(ref _version);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected virtual void RemoveAtIndexCore(int index)
    {
        Debug.Assert(index >= 0 && index < Count);

        var chunkBucket = index / ChunkSize;
        var bucketIndex = index % ChunkSize;
        var bucketContainingLastElement = (Count - 1) / ChunkSize;
        var bucketContainingFirstUnusedSlot = Count / ChunkSize;

        for (var i = chunkBucket; i <= bucketContainingLastElement; i++)
        {
            var chunk = _chunks[i];
            var currentChunkSize = ChunkSize;
            if (i == bucketContainingFirstUnusedSlot)
            {
                // bucket is not full
                currentChunkSize = Count % ChunkSize;
            }

            var srcPosition = 1;
            if (i == chunkBucket)
                srcPosition = bucketIndex + 1;

            var length = currentChunkSize - srcPosition;
            if (length > 0)
            {
                ListCopy(chunk, srcPosition, chunk, srcPosition - 1, length);
            }

            if (i != bucketContainingLastElement)
            {
                chunk[ChunkSize - 1] = _chunks[i + 1][0];
            }
        }

        Interlocked.Decrement(ref _count);
        Interlocked.Increment(ref _version);
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

    private struct ChunkedListEnumerator : IEnumerator<T>
    {
        private readonly ChunkedList<T> _list;
        private int _index;
        private readonly int _version;

        internal ChunkedListEnumerator(ChunkedList<T> list)
        {
            _list = list;
            _index = 0;
            _version = list._version;
            Current = default!;
        }

        public T Current { get; private set; }

        readonly object IEnumerator.Current
        {
            get
            {
                if (_index == 0 || _index == _list.Count + 1)
                {
                    throw new IndexOutOfRangeException($"{nameof(_index)} was out of range");
                }

                return Current!;
            }
        }

        public readonly void Dispose() { }

        public bool MoveNext()
        {
            var localList = _list;
            if (_version == localList._version && ((uint)_index < (uint)localList.Count))
            {
                Current = localList[_index];
                Interlocked.Increment(ref _index);
                return true;
            }

            return FinalMoveNext();
        }

        public void Reset()
        {
            CheckVersion();

            _index = 0;
            Current = default!;
        }

        private bool FinalMoveNext()
        {
            CheckVersion();

            _index = _list.Count + 1;
            Current = default!;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly void CheckVersion()
        {
            if (_version != _list._version)
            {
                throw new InvalidOperationException("Collection was modified during enumeration");
            }
        }
    }
}
