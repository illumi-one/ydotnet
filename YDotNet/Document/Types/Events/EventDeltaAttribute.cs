using System.Runtime.InteropServices;
using YDotNet.Document.Cells;
using YDotNet.Infrastructure;

namespace YDotNet.Document.Types.Events;

/// <summary>
///     The formatting attribute that's part of an <see cref="EventDelta" /> instance.
/// </summary>
public sealed class EventDeltaAttribute
{
    private readonly Lazy<Output> value;

    internal EventDeltaAttribute(nint handle, Doc doc, IResourceOwner owner)
    {
        Handle = handle;

        Key = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(handle))
            ?? throw new InvalidOperationException("Failed to read key");

        value = new Lazy<Output>(() =>
        {
            return new Output(handle + MemoryConstants.PointerSize, doc, owner);
        });
    }

    /// <summary>
    ///     Gets the attribute name.
    /// </summary>
    public string Key { get; }

    /// <summary>
    ///     Gets the attribute value.
    /// </summary>
    public Output Value => value.Value;

    /// <summary>
    ///     Gets the handle to the native resource.
    /// </summary>
    internal nint Handle { get; }
}
