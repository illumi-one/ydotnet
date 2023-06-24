using System.Runtime.InteropServices;
using YDotNet.Native.Cells.Inputs;

namespace YDotNet.Document.Cells;

/// <summary>
///     Represents a cell used to send information to be stored.
/// </summary>
public sealed class Input
{
    private Input(InputNative inputNative)
    {
        InputNative = inputNative;
    }

    /// <summary>
    ///     Gets the native input cell represented by this cell.
    /// </summary>
    internal InputNative InputNative { get; }

    /// <summary>
    ///     Creates a new instance of the <see cref="Input" /> class.
    /// </summary>
    /// <param name="value">The <see cref="Doc" /> instance to be stored in the cell.</param>
    /// <returns>The <see cref="Input" /> cell that represents the provided value.</returns>
    public static Input Doc(Doc value)
    {
        return new Input(InputChannel.Doc(value.Handle));
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="Input" /> class.
    /// </summary>
    /// <param name="value">The <see cref="string" /> value to be stored in the cell.</param>
    /// <returns>The <see cref="Input" /> cell that represents the provided value.</returns>
    public static Input String(string value)
    {
        // TODO [LSViana] Free the memory allocated here.
        return new Input(InputChannel.String(Marshal.StringToHGlobalAuto(value)));
    }
}
