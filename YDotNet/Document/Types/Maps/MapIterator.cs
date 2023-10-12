using System.Collections;
using YDotNet.Infrastructure;
using YDotNet.Native.Types.Maps;

namespace YDotNet.Document.Types.Maps;

/// <summary>
///     Represents an enumerable to read <see cref="MapEntry" /> instances from a <see cref="Map" />.
/// </summary>
/// <remarks>
///     Two important details about <see cref="MapIterator" />.
///     <ul>
///         <li>The <see cref="MapEntry" /> instances are unordered when iterating;</li>
///         <li>
///             The iterator can't be reused. If needed, use <see cref="Enumerable.ToArray{TSource}" /> to accumulate
///             values.
///         </li>
///     </ul>
/// </remarks>
public class MapIterator : UnmanagedResource, IEnumerable<MapEntry>
{
    internal MapIterator(nint handle, Doc doc)
        : base(handle)
    {
        Doc = doc;
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="MapIterator"/> class.
    /// </summary>
    ~MapIterator()
    {
        Dispose(false);
    }

    /// <inheritdoc/>
    protected internal override void DisposeCore(bool disposing)
    {
        MapChannel.IteratorDestroy(Handle);
    }

    internal Doc Doc { get; }

    /// <inheritdoc />
    public IEnumerator<MapEntry> GetEnumerator()
    {
        ThrowIfDisposed();
        return new MapEnumerator(this);
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
