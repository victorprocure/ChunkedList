using System.Collections;

namespace ChunkedList;

internal sealed class EmptyChunkedList<T> : ChunkedList<T>
{
    private static readonly Exception _unableToExecute = new InvalidOperationException("This is the default empty list, nothing to do");
    internal EmptyChunkedList(IEqualityComparer<T>? equalityComparer = default) : base(4, 0, true, equalityComparer) { }

    private protected override void ClearItemsCore() => throw _unableToExecute;
    private protected override void CopyToCore(T[] array, int arrayIndex) => Array.Copy(Array.Empty<T>(), array, 0);
    private protected override int CountCore => 0;
    private protected override int FindItemIndex(T item) => -1;
    private protected override IEnumerator<T> GetEnumeratorCore() => EmptyEnumerator._default;
    private protected override void InsertAtCore(int index, T item) => throw _unableToExecute;
    private protected override void RemoveAtIndexCore(int index) => throw _unableToExecute;
    private protected override bool RemoveByValueCore(T value) => false;

    private readonly struct EmptyEnumerator : IEnumerator<T>
    {
        internal static readonly EmptyEnumerator _default = new();

        public T Current => default!;

        object IEnumerator.Current => default(T)!;

        public void Dispose()
        {
        }

        public bool MoveNext() => false;

        public void Reset()
        {
        }
    }
}
