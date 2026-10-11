using System.Collections.Generic;

namespace CodeBrix.VideoEncoding.Av1.Internal;

/// <summary>
/// Everything the native encoder is created and configured with, worked out from a request and options before
/// any native call: the create arguments, the integer options in the order they are set, and the libaom string
/// options in the order they are passed through.
/// </summary>
internal sealed class Av1NativeSettings
{
    /// <summary>Width in pixels.</summary>
    public int Width { get; set; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; set; }

    /// <summary>8, 10 or 12.</summary>
    public int BitDepth { get; set; }

    /// <summary>A <c>CBA_LAYOUT_*</c> value.</summary>
    public int Layout { get; set; }

    /// <summary>The pts unit's numerator in seconds (the frame rate's denominator).</summary>
    public int TimebaseNumerator { get; set; }

    /// <summary>The pts unit's denominator (the frame rate's numerator).</summary>
    public int TimebaseDenominator { get; set; }

    /// <summary>A <c>CBA_USAGE_*</c> value.</summary>
    public int Usage { get; set; }

    /// <summary>The <c>CBA_OPT_*</c> integer options, in the order they are set.</summary>
    public List<KeyValuePair<int, int>> IntOptions { get; } = new List<KeyValuePair<int, int>>();

    /// <summary>The libaom options by name, in the order they are passed through.</summary>
    public List<KeyValuePair<string, string>> StringOptions { get; } = new List<KeyValuePair<string, string>>();

    /// <summary>Looks an integer option up.</summary>
    /// <param name="option">The <c>CBA_OPT_*</c> id.</param>
    /// <param name="value">The value set, or 0.</param>
    /// <returns>True when the option is set.</returns>
    public bool TryGetInt(int option, out int value)
    {
        foreach (KeyValuePair<int, int> entry in IntOptions)
        {
            if (entry.Key != option) continue;
            value = entry.Value;
            return true;
        }

        value = 0;
        return false;
    }
}
