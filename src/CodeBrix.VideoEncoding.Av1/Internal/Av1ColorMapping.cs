using CodeBrix.VideoProcessing.Core.Media;

namespace CodeBrix.VideoEncoding.Av1.Internal;

/// <summary>
/// The one place the CodeBrix.VideoProcessing.Core colour tags turn into the code points an AV1 sequence
/// header carries (ISO/IEC 23091-4 / ITU-T H.273, as the AV1 specification lists them).
/// </summary>
/// <remarks>
/// A tag Core calls "Other" has no code point of its own here and is written as "unspecified" (2); a request
/// that needs a particular code point Core does not name can still set it through the libaom codec options.
/// </remarks>
internal static class Av1ColorMapping
{
    /// <summary>AV1 "unspecified" for primaries, transfer and matrix.</summary>
    public const int Unspecified = 2;

    /// <summary>AV1 <c>MC_IDENTITY</c>: the samples are RGB (GBR).</summary>
    public const int MatrixIdentity = 0;

    /// <summary>The <c>color_primaries</c> code point for a Core primaries tag.</summary>
    /// <param name="primaries">The tag.</param>
    /// <returns>The AV1 code point.</returns>
    public static int ToAv1(VideoColorPrimaries primaries) => primaries switch
    {
        VideoColorPrimaries.Bt709 => 1,       // CP_BT_709
        VideoColorPrimaries.Bt601Pal => 5,    // CP_BT_470_B_G
        VideoColorPrimaries.Bt601Ntsc => 6,   // CP_BT_601
        VideoColorPrimaries.Bt2020 => 9,      // CP_BT_2020
        VideoColorPrimaries.DciP3 => 11,      // CP_SMPTE_431
        VideoColorPrimaries.DisplayP3 => 12,  // CP_SMPTE_432
        _ => Unspecified,                     // Unspecified, Other
    };

    /// <summary>The <c>transfer_characteristics</c> code point for a Core transfer tag.</summary>
    /// <param name="transfer">The tag.</param>
    /// <returns>The AV1 code point.</returns>
    public static int ToAv1(VideoColorTransfer transfer) => transfer switch
    {
        VideoColorTransfer.Bt709 => 1,        // TC_BT_709
        VideoColorTransfer.Linear => 8,       // TC_LINEAR
        VideoColorTransfer.Srgb => 13,        // TC_SRGB
        VideoColorTransfer.Pq => 16,          // TC_SMPTE_2084
        VideoColorTransfer.Hlg => 18,         // TC_HLG
        _ => Unspecified,                     // Unspecified, Other
    };

    /// <summary>The <c>matrix_coefficients</c> code point for a Core matrix tag.</summary>
    /// <param name="matrix">The tag.</param>
    /// <returns>The AV1 code point.</returns>
    public static int ToAv1(VideoColorMatrix matrix) => matrix switch
    {
        VideoColorMatrix.Rgb => MatrixIdentity,          // MC_IDENTITY
        VideoColorMatrix.Bt709 => 1,                     // MC_BT_709
        VideoColorMatrix.Bt601 => 6,                     // MC_BT_601
        VideoColorMatrix.Bt2020NonConstant => 9,         // MC_BT_2020_NCL
        VideoColorMatrix.Bt2020Constant => 10,           // MC_BT_2020_CL
        _ => Unspecified,                                // Unspecified, Other
    };

    /// <summary>The <c>CBA_RANGE_*</c> value (the AV1 <c>color_range</c> bit) for a Core range tag.</summary>
    /// <param name="range">The tag.</param>
    /// <returns>1 for full range, 0 for limited (and for unspecified, which AV1 cannot express).</returns>
    public static int ToAv1(VideoColorRange range) => range == VideoColorRange.Full ? 1 : 0;
}
