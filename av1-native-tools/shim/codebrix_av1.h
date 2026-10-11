/*
 * codebrix_av1.h - the fixed-ABI C surface of the codebrix_av1 native library.
 *
 * This is the ONLY contract between the managed CodeBrix.VideoEncoding.Av1 assembly and the native
 * library. It deliberately exposes no libaom type, no C `long`, no struct and NO CALLBACK: every
 * parameter is an opaque handle, an int32_t, an int64_t, or a pointer to bytes, so one managed
 * DllImport set works on every RID (C `long` is 32-bit on Windows and 64-bit elsewhere).
 *
 * The library statically links libaom (Alliance for Open Media, BSD-2-Clause + the AOM Patent License
 * 1.0) built ENCODER-ONLY (no decoder, no tools, no tests) with high bit depth enabled, and exports
 * only the functions below. Frames go in as planar samples; what comes out is AV1 TEMPORAL UNITS - one
 * per input frame, in presentation order, each a self-contained byte run beginning with a temporal
 * delimiter OBU - exactly the bytes a container (CBVF, WebM, MP4) stores as one video packet. The
 * sequence header OBU is readable separately so the managed side can build the av1C configuration
 * record without parsing packets.
 *
 * Lifecycle: create -> set_* (any number, any order) -> start -> encode ... -> flush -> destroy.
 * After every encode and after flush, drain packets with pending_packets / packet_info / read_packet.
 * The encoder never reorders output: with the default lag the first packets may arrive a few encode
 * calls late, but every packet carries the pts the caller gave for that frame, and packets come out in
 * pts order (libaom's AV1 encoder emits show-existing-frame temporal units so that each temporal unit
 * corresponds to one displayed frame).
 *
 * Samples: 8-bit layouts use one byte per sample. 10- and 12-bit layouts use one little-endian
 * uint16_t per sample holding the value in its low bits (the libaom / dav1d convention); strides are
 * ALWAYS in bytes. Plane memory is only read during the encode call - the caller may reuse it as
 * soon as encode returns.
 *
 * Threading: a handle may be used from one thread at a time. Different handles are independent.
 * libaom's own worker threads (CBA_OPT_THREADS) are internal to a handle.
 */
#ifndef CODEBRIX_AV1_H
#define CODEBRIX_AV1_H

#include <stdint.h>

#if defined(_WIN32)
#  define CBA_EXPORT __declspec(dllexport)
#else
#  define CBA_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ---- Versions ---------------------------------------------------------------------------- */

/* The version of THIS contract. The managed side refuses a library whose value differs. Bump it
 * whenever a function or constant below changes shape or meaning. */
#define CODEBRIX_AV1_API_VERSION 1

/* Returns CODEBRIX_AV1_API_VERSION of the loaded library. */
CBA_EXPORT int32_t codebrix_av1_api_version(void);

/* Returns libaom's own version string (aom_codec_version_str(), e.g. "3.15.2"). Static; never free. */
CBA_EXPORT const char* codebrix_av1_library_version(void);

/* ---- Result codes ------------------------------------------------------------------------ */

/* Every int32_t-returning function returns 0 (CBA_OK), a non-negative count where documented, or one
 * of these negatives. Keep the numbers stable (bump the API version if one must change). */
#define CBA_OK                      0
#define CBA_E_INVALID_ARGUMENT     -1   /* null handle/pointer, or a value out of the documented range */
#define CBA_E_OUT_OF_MEMORY        -2
#define CBA_E_INVALID_STATE        -3   /* e.g. a set_* call after start, encode after flush, read with nothing pending */
#define CBA_E_UNSUPPORTED          -4   /* libaom refused the parameter combination at start (see last_error) */
#define CBA_E_BUFFER_TOO_SMALL     -5   /* the destination capacity is below the length reported by *_length / *_info */
#define CBA_E_AOM_FAULT            -6   /* any other libaom failure (the text is in last_error) */

/* ---- Constants ---------------------------------------------------------------------------- */

/* Pixel layouts (codebrix_av1_encoder_create). */
#define CBA_LAYOUT_I420             0   /* planar 4:2:0 - Y full size, U and V ((w+1)/2) x ((h+1)/2) */
#define CBA_LAYOUT_I444             1   /* planar 4:4:4 - Y, U and V all full size                     */
#define CBA_LAYOUT_MONOCHROME       2   /* Y only (AV1 "mono_chrome"); U and V are ignored (may be NULL) */

/* Encoder usage (codebrix_av1_encoder_create). Chooses libaom's usage profile; it also changes the
 * defaults of several options (cpu-used range, lag-in-frames) exactly as aomenc's --usage does. */
#define CBA_USAGE_GOOD_QUALITY      0   /* AOM_USAGE_GOOD_QUALITY - the default for masters            */
#define CBA_USAGE_REALTIME          1   /* AOM_USAGE_REALTIME - low latency, lag-in-frames forced to 0 */
#define CBA_USAGE_ALL_INTRA         2   /* AOM_USAGE_ALL_INTRA - every frame a key frame               */

/* Rate-control modes (CBA_OPT_RATE_MODE). */
#define CBA_RATE_CONSTANT_QUALITY   0   /* AOM_Q:   CBA_OPT_QUALITY only (the default)                  */
#define CBA_RATE_CONSTRAINED_QUALITY 1  /* AOM_CQ:  CBA_OPT_QUALITY + CBA_OPT_TARGET_BITRATE_KBPS cap   */
#define CBA_RATE_VARIABLE_BITRATE   2   /* AOM_VBR: CBA_OPT_TARGET_BITRATE_KBPS                        */
#define CBA_RATE_CONSTANT_BITRATE   3   /* AOM_CBR: CBA_OPT_TARGET_BITRATE_KBPS                        */

/* Colour range (CBA_OPT_COLOR_RANGE). */
#define CBA_RANGE_LIMITED           0   /* studio / "TV" range - the default for colour pictures      */
#define CBA_RANGE_FULL              1   /* full / "PC" range - what an alpha-as-monochrome stream uses */

/* Chroma sample position (CBA_OPT_CHROMA_SAMPLE_POSITION; 4:2:0 only). */
#define CBA_CHROMA_POSITION_UNKNOWN   0
#define CBA_CHROMA_POSITION_VERTICAL  1  /* "left" (MPEG-2 style) - what dav1d/most sources mean       */
#define CBA_CHROMA_POSITION_COLOCATED 2  /* "top-left"                                                  */

/*
 * Integer options for codebrix_av1_encoder_set_int32. All are optional; every one defaults to the
 * value libaom chooses for the usage. Set before start only (CBA_E_INVALID_STATE afterwards). An out-
 * of-range value is CBA_E_INVALID_ARGUMENT at set time; a combination libaom rejects is
 * CBA_E_UNSUPPORTED at start time, with libaom's explanation in last_error.
 */
#define CBA_OPT_CPU_USED                 1   /* 0..9 (GOOD/REALTIME), 0..9 ALL_INTRA; libaom "cpu-used" - higher = faster, lower quality */
#define CBA_OPT_RATE_MODE                2   /* CBA_RATE_*; default CBA_RATE_CONSTANT_QUALITY                                       */
#define CBA_OPT_QUALITY                  3   /* 0..63 "cq-level" for the two quality modes (lower = better); default 30 when unset   */
#define CBA_OPT_TARGET_BITRATE_KBPS      4   /* 1..2000000 for CQ/VBR/CBR; required by those modes                                  */
#define CBA_OPT_LOSSLESS                 5   /* 0/1; 1 = mathematically lossless (AV1E_SET_LOSSLESS); quality/bitrate are then
                                                 ignored, AND lag-in-frames is forced to 0 whatever CBA_OPT_LAG_IN_FRAMES says:
                                                 libaom's lossless mode with look-ahead is NOT bit-exact at 10 bits (reproduced
                                                 with aomenc itself), without look-ahead it is.                                  */
#define CBA_OPT_THREADS                  6   /* 1..64 worker threads; default = 1 (libaom's)                                        */
#define CBA_OPT_ROW_MULTITHREADING       7   /* 0/1 row-based multithreading; default libaom's (on)                                 */
#define CBA_OPT_TILE_COLUMNS_LOG2        8   /* 0..6; default 0                                                                     */
#define CBA_OPT_TILE_ROWS_LOG2           9   /* 0..6; default 0                                                                     */
#define CBA_OPT_LAG_IN_FRAMES           10   /* 0..48 look-ahead frames; default usage's (GOOD 35, REALTIME 0). Output then lags.   */
#define CBA_OPT_KEY_FRAME_MAX_DISTANCE  11   /* 0..65535 "kf-max-dist": the largest gap between automatic key frames (0 = every
                                                 frame a key frame); default libaom's (9999). kf-min-dist is always 0 so that a
                                                 forced key frame (see encode) is honoured anywhere.                                */
#define CBA_OPT_COLOR_PRIMARIES         12   /* ISO/IEC 23091-4 code point as in the AV1 spec (1 = BT.709, 2 = unspecified ...);
                                                 default 2 (unspecified)                                                             */
#define CBA_OPT_TRANSFER_CHARACTERISTICS 13  /* same code points (1 = BT.709, 2 = unspecified, 13 = sRGB ...); default 2           */
#define CBA_OPT_MATRIX_COEFFICIENTS     14   /* same code points (1 = BT.709, 2 = unspecified, 0 = identity/RGB ...); default 2   */
#define CBA_OPT_COLOR_RANGE             15   /* CBA_RANGE_*; default CBA_RANGE_LIMITED                                             */
#define CBA_OPT_CHROMA_SAMPLE_POSITION  16   /* CBA_CHROMA_POSITION_*; default UNKNOWN; ignored unless layout is I420              */
#define CBA_OPT_AUTOMATIC_KEY_FRAMES    17   /* 0/1; default 1. 0 = AOM_KF_DISABLED: libaom places NO key frame of its own
                                                 (no scene-cut or interval key frames) - frame 0 and the frames encoded with
                                                 force_key_frame = 1 are the ONLY key frames. This is what a lock-step pair
                                                 of streams (colour + alpha) uses so that key-frame positions never depend on
                                                 picture content. CBA_OPT_KEY_FRAME_MAX_DISTANCE is ignored when this is 0.  */

/* ---- Encoder ----------------------------------------------------------------------------- */

typedef struct codebrix_av1_encoder codebrix_av1_encoder;

/*
 * Creates an encoder; libaom is not initialised until codebrix_av1_encoder_start.
 *   width, height      1..65536 each (the AV1 limit). CBA_LAYOUT_I420 requires BOTH to be EVEN
 *                      (CBA_E_INVALID_ARGUMENT otherwise): libaom codes the last chroma column/row of an
 *                      odd-sized 4:2:0 picture as skip, so it never round-trips (not even lossless).
 *                      I444 and MONOCHROME accept odd sizes.
 *   bit_depth          8, 10 or 12. The AV1 profile is chosen from layout + depth: Main for I420 and
 *                      MONOCHROME at 8/10 bits, High for I444 at 8/10 bits, Professional for 12 bits.
 *   layout             CBA_LAYOUT_*
 *   timebase_num,      the unit of every pts, in seconds, as a fraction: pts * num / den seconds. For a
 *   timebase_den       constant frame rate fps_num/fps_den pass num = fps_den, den = fps_num and use the
 *                      frame index as pts. 1..2147483647 each.
 *   usage              CBA_USAGE_*
 *   out_encoder        receives the handle on success, NULL on failure.
 * Returns CBA_OK or a CBA_E_* code.
 */
CBA_EXPORT int32_t codebrix_av1_encoder_create(
    int32_t width,
    int32_t height,
    int32_t bit_depth,
    int32_t layout,
    int32_t timebase_num,
    int32_t timebase_den,
    int32_t usage,
    codebrix_av1_encoder** out_encoder);

/* Sets one CBA_OPT_* integer option. Before start only. */
CBA_EXPORT int32_t codebrix_av1_encoder_set_int32(
    codebrix_av1_encoder* encoder,
    int32_t option,
    int32_t value);

/*
 * Records a libaom option by NAME and VALUE (NUL-terminated ASCII, the names aomenc accepts without
 * their leading dashes: "tune", "enable-tpl-model", "sharpness", "deltaq-mode", ...). Applied through
 * aom_codec_set_option in the order given, right after libaom is initialised at start; a name or
 * value libaom rejects makes start fail with CBA_E_UNSUPPORTED and the reason in last_error. This is
 * the pass-through for everything the CBA_OPT_* set does not name. Before start only.
 */
CBA_EXPORT int32_t codebrix_av1_encoder_set_string_option(
    codebrix_av1_encoder* encoder,
    const char* name_ascii,
    const char* value_ascii);

/*
 * Initialises libaom with everything set so far. Exactly once, before the first encode. Returns
 * CBA_E_UNSUPPORTED when libaom rejects the configuration (last_error says why). The sequence header
 * becomes readable once the FIRST PACKET has been produced (see sequence_header_length).
 */
CBA_EXPORT int32_t codebrix_av1_encoder_start(codebrix_av1_encoder* encoder);

/*
 * Encodes one frame.
 *   y, y_stride_bytes      the luma plane: width samples by height rows
 *   u, u_stride_bytes      the Cb plane (I420: (width+1)/2 by (height+1)/2; I444: full size); ignored
 *   v, v_stride_bytes      and may be NULL for MONOCHROME
 *   pts                    presentation time in timebase units; must be greater than the previous pts
 *   force_key_frame        1 = this frame MUST be coded as a key frame (AOM_EFLAG_FORCE_KF): the lock-step
 *                          alpha stream relies on this; 0 = libaom decides. KNOWN libaom BEHAVIOUR: in
 *                          GOOD_QUALITY usage a forced key frame at N makes frame N+1 a key frame as well
 *                          (its two-pass planner counts the forced frame as the next interval's start);
 *                          REALTIME and ALL_INTRA place key frames exactly. Two encoders fed identical
 *                          flags still produce IDENTICAL key-frame sets, which is what lock-step needs.
 * Completed temporal units join the packet queue (possibly none yet while look-ahead fills; possibly
 * more than one later). Returns CBA_OK or a CBA_E_* code. After flush: CBA_E_INVALID_STATE.
 */
CBA_EXPORT int32_t codebrix_av1_encoder_encode(
    codebrix_av1_encoder* encoder,
    const uint8_t* y, int32_t y_stride_bytes,
    const uint8_t* u, int32_t u_stride_bytes,
    const uint8_t* v, int32_t v_stride_bytes,
    int64_t pts,
    int32_t force_key_frame);

/*
 * Ends the stream: drives libaom until every look-ahead frame has been coded, queueing the remaining
 * temporal units. After this, encode returns CBA_E_INVALID_STATE; the packet queue and the sequence
 * header still drain. Flushing a never-encoded encoder is valid and queues nothing. Calling flush a
 * second time is a no-op returning CBA_OK.
 */
CBA_EXPORT int32_t codebrix_av1_encoder_flush(codebrix_av1_encoder* encoder);

/* ---- Output: packets (temporal units) ---------------------------------------------------- */

/* Number of temporal units waiting in the packet queue. Negative only for a null handle. */
CBA_EXPORT int32_t codebrix_av1_encoder_pending_packets(codebrix_av1_encoder* encoder);

/* Describes the OLDEST pending packet without removing it: its length in bytes, its pts (the value
 * given to encode for that frame) and whether it is a key frame (AOM_FRAME_IS_KEY - a random-access
 * point a container marks as such). Returns CBA_OK, or CBA_E_INVALID_STATE when nothing is pending. */
CBA_EXPORT int32_t codebrix_av1_encoder_packet_info(
    codebrix_av1_encoder* encoder,
    int32_t* out_length,
    int64_t* out_pts,
    int32_t* out_is_key_frame);

/* Copies the oldest pending packet into `destination` (capacity must be >= its length) and removes it
 * from the queue. Returns the number of bytes copied, or a CBA_E_* code (CBA_E_INVALID_STATE when none,
 * CBA_E_BUFFER_TOO_SMALL when capacity is short - the packet then stays queued). */
CBA_EXPORT int32_t codebrix_av1_encoder_read_packet(
    codebrix_av1_encoder* encoder,
    uint8_t* destination,
    int32_t capacity);

/* ---- Output: the sequence header --------------------------------------------------------- */

/* Length in bytes of the sequence header OBU (obu_header + size field + payload, i.e. exactly the OBU
 * as it appears in the bitstream) for this stream, taken from the FIRST KEY-FRAME temporal unit the
 * encoder produced (libaom's aom_codec_get_global_headers() reports tool flags that differ from what
 * it writes into the stream, so it is NOT used). Valid once at least one packet has been produced
 * (pending_packets > 0 or packets_produced > 0); CBA_E_INVALID_STATE before that. The managed side
 * derives the av1C record (profile, level, tier, bit depth, monochrome, subsampling) from it before
 * handing out the first packet. It is also the OBU a container may store ahead of the first packet. */
CBA_EXPORT int32_t codebrix_av1_encoder_sequence_header_length(codebrix_av1_encoder* encoder);

/* Copies the sequence header OBU into `destination` (capacity must be >= its length). Returns bytes
 * copied or a CBA_E_* code. Not consumed; may be read again. */
CBA_EXPORT int32_t codebrix_av1_encoder_read_sequence_header(
    codebrix_av1_encoder* encoder,
    uint8_t* destination,
    int32_t capacity);

/* ---- Progress / diagnostics -------------------------------------------------------------- */

/* Frames accepted by encode so far (progress reporting). Negative only for a null handle. */
CBA_EXPORT int64_t codebrix_av1_encoder_frames_submitted(codebrix_av1_encoder* encoder);

/* Temporal units produced so far, read or not. Negative only for a null handle. */
CBA_EXPORT int64_t codebrix_av1_encoder_packets_produced(codebrix_av1_encoder* encoder);

/* libaom's explanation of the last failing call on this handle: aom_codec_error() and, when present,
 * aom_codec_error_detail() joined by ": " - or "" when the last call succeeded. Static per handle;
 * valid until the next call on the handle. For exception messages. */
CBA_EXPORT const char* codebrix_av1_encoder_last_error(codebrix_av1_encoder* encoder);

/* Releases everything, including libaom's threads. NULL is ignored. The handle is invalid afterwards. */
CBA_EXPORT void codebrix_av1_encoder_destroy(codebrix_av1_encoder* encoder);

#ifdef __cplusplus
}
#endif

#endif /* CODEBRIX_AV1_H */
