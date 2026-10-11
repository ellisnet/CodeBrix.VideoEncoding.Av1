/*
 * codebrix_av1.c - implementation of codebrix_av1.h over libaom's AV1 encoder (aom_codec_av1_cx()).
 *
 * Part of CodeBrix.VideoEncoding.Av1's av1-native-tools. libaom (v3.15.2, BSD-2-Clause + the AOM
 * Patent License 1.0, unmodified) is built ENCODER-ONLY with high bit depth, linked in statically and
 * compiled with hidden visibility; only the functions of codebrix_av1.h are exported.
 *
 * How it works (the header's file comment, in implementation terms):
 *   - create only records the parameters; set_int32 / set_string_option only record options. libaom
 *     is untouched until start.
 *   - start: aom_codec_enc_config_default(usage) -> g_w/g_h, g_profile (derived: 0 Main for I420 and
 *     MONOCHROME at 8/10 bits, 1 High for I444 at 8/10 bits, 2 Professional at 12 bits), g_bit_depth
 *     + g_input_bit_depth (+ AOM_CODEC_USE_HIGHBITDEPTH for 10/12 bits), monochrome, g_timebase,
 *     rc_end_usage (default AOM_Q), rc_target_bitrate, kf_min_dist = 0 + kf_max_dist, g_threads,
 *     g_lag_in_frames -> aom_codec_enc_init -> the codec controls (CQ level, cpu-used, lossless,
 *     row-mt, tiles, chroma subsampling (needed for the 12-bit Professional profile), colour
 *     signalling, chroma sample position) -> the string options through
 *     aom_codec_set_option, in the order given. Integer options the caller never set are left at
 *     libaom's value for the usage, EXCEPT the four the header fixes: rate mode (AOM_Q), quality
 *     (cq-level 30 in the two quality modes), kf-min-dist (0) and the colour description (libaom's
 *     own defaults already equal the header's: unspecified / unspecified / unspecified / limited).
 *     Any failure there destroys the libaom context again and the encoder stays CREATED (start may
 *     be retried after changing the options).
 *   - The sequence header OBU is taken from the FIRST KEY-FRAME TEMPORAL UNIT libaom produces (the
 *     OBU_SEQUENCE_HEADER libaom puts in every key frame), copied byte for byte. Until that unit
 *     exists sequence_header_length / read_sequence_header answer CBA_E_INVALID_STATE.
 *     Why not aom_codec_get_global_headers() right after init: with libaom 3.15.2 it returns the
 *     sequence parameters as they stand BEFORE the first frame, and libaom finalises them only
 *     when it codes that frame (speed-feature-dependent coding tools: use_128x128_superblock,
 *     enable_interintra_compound, enable_dual_filter, enable_jnt_comp, enable_restoration; and for
 *     the 12-bit Professional profile the chroma subsampling + chroma sample position, which come
 *     from the first image). Measured on 2026-10-10: the global header differed from the in-stream
 *     one in EVERY configuration tried, so a container / av1C built from it would contradict the
 *     samples. The smoke test checks that every key frame carries exactly the header this shim
 *     reports.
 *   - encode wraps the caller's planes in an aom_image_t WITHOUT copying (aom_img_wrap, then the
 *     plane pointers and byte strides are overridden). Two cases need shim memory, both sized once
 *     per encoder: MONOCHROME (libaom's image must still carry chroma planes; they point at a
 *     mid-grey plane the shim owns - libaom ignores them for a monochrome sequence), and I420 / I444
 *     input whose U and V strides differ (libaom requires equal U/V strides; V is then copied into a
 *     shim buffer with U's stride).
 *   - Every frame is passed with duration = pts - previous pts (1 for the first frame), i.e. one
 *     timebase unit per frame for the documented frame-index pts. libaom's rate control derives the
 *     frame rate from it.
 *   - After each aom_codec_encode (and in flush: aom_codec_encode(NULL) until a call yields no
 *     packet) every AOM_CODEC_CX_FRAME_PKT is copied into the packet queue with its pts and
 *     (flags & AOM_FRAME_IS_KEY). libaom itself prefixes each temporal unit with a temporal
 *     delimiter and folds invisible (alt-ref) frames into the next shown frame's unit, so one packet
 *     == one input frame.
 *   - last_error is cleared at the start of every call on a handle (except last_error itself) and
 *     set on failure: aom_codec_error() + ": " + aom_codec_error_detail() for libaom failures, or the
 *     shim's own explanation for an argument / state error.
 *
 * Threading: one handle, one thread at a time (the header's rule). libaom's worker threads are
 * internal to the handle and joined by aom_codec_destroy.
 */

#include "codebrix_av1.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "aom/aom_codec.h"
#include "aom/aom_encoder.h"
#include "aom/aom_image.h"
#include "aom/aomcx.h"

/* ---- internal types ------------------------------------------------------------------------- */

enum cba_phase { CBA_PHASE_CREATED = 0, CBA_PHASE_STARTED = 1, CBA_PHASE_FLUSHED = 2 };

#define CBA_OPT_FIRST 1
#define CBA_OPT_LAST  17
#define CBA_ERROR_CAPACITY 1024

typedef struct cba_packet {
    uint8_t* data;
    int32_t  length;
    int64_t  pts;
    int32_t  is_key_frame;
} cba_packet;

typedef struct cba_string_option {
    char* name;
    char* value;
} cba_string_option;

struct codebrix_av1_encoder {
    /* create parameters */
    int32_t width, height, bit_depth, layout, timebase_num, timebase_den, usage;

    /* options recorded before start */
    int32_t option_set[CBA_OPT_LAST + 1];
    int32_t option_value[CBA_OPT_LAST + 1];
    cba_string_option* string_options;
    int32_t string_option_count, string_option_capacity;

    /* libaom */
    int phase;
    int codec_initialised;
    aom_codec_ctx_t codec;

    /* shim-owned plane memory (MONOCHROME chroma, or V copy for unequal U/V strides) */
    uint8_t* grey_chroma;      /* one plane, mid-grey, stride grey_stride */
    int32_t  grey_stride;
    uint8_t* v_copy;           /* one plane, stride v_copy_stride */
    int32_t  v_copy_stride;
    size_t   v_copy_size;

    /* sequence header */
    uint8_t* sequence_header;
    int32_t  sequence_header_length;

    /* packet queue: packets[head .. count) are pending */
    cba_packet* packets;
    int32_t packet_head, packet_count, packet_capacity;

    /* progress */
    int64_t frames_submitted;
    int64_t packets_produced;
    int64_t last_pts;

    char last_error[CBA_ERROR_CAPACITY];
};

/* ---- small helpers -------------------------------------------------------------------------- */

static void clear_error(codebrix_av1_encoder* e) { e->last_error[0] = '\0'; }

static int32_t fail(codebrix_av1_encoder* e, int32_t code, const char* message)
{
    snprintf(e->last_error, sizeof e->last_error, "%s", message);
    return code;
}

static void set_aom_error(codebrix_av1_encoder* e, const char* context)
{
    const char* error = aom_codec_error(&e->codec);
    const char* detail = aom_codec_error_detail(&e->codec);
    if (error == NULL) error = "libaom error";
    if (detail != NULL && detail[0] != '\0') {
        if (context != NULL) snprintf(e->last_error, sizeof e->last_error, "%s: %s: %s", context, error, detail);
        else snprintf(e->last_error, sizeof e->last_error, "%s: %s", error, detail);
    } else {
        if (context != NULL) snprintf(e->last_error, sizeof e->last_error, "%s: %s", context, error);
        else snprintf(e->last_error, sizeof e->last_error, "%s", error);
    }
}

/* aom_codec_err_t of a failure at start -> CBA_E_*. */
static int32_t map_start_error(aom_codec_err_t err)
{
    switch (err) {
        case AOM_CODEC_MEM_ERROR: return CBA_E_OUT_OF_MEMORY;
        case AOM_CODEC_INVALID_PARAM:
        case AOM_CODEC_INCAPABLE:
        case AOM_CODEC_UNSUP_FEATURE:
        case AOM_CODEC_UNSUP_BITSTREAM:
        case AOM_CODEC_ERROR: return CBA_E_UNSUPPORTED;
        default: return CBA_E_AOM_FAULT;
    }
}

static int32_t map_run_error(aom_codec_err_t err)
{
    return err == AOM_CODEC_MEM_ERROR ? CBA_E_OUT_OF_MEMORY : CBA_E_AOM_FAULT;
}

static int32_t bytes_per_sample(const codebrix_av1_encoder* e) { return e->bit_depth > 8 ? 2 : 1; }
static int32_t chroma_width(const codebrix_av1_encoder* e)
{
    return e->layout == CBA_LAYOUT_I444 ? e->width : (int32_t)(((int64_t)e->width + 1) / 2);
}
static int32_t chroma_height(const codebrix_av1_encoder* e)
{
    return e->layout == CBA_LAYOUT_I444 ? e->height : (int32_t)(((int64_t)e->height + 1) / 2);
}

static int option_is_set(const codebrix_av1_encoder* e, int32_t option) { return e->option_set[option] != 0; }
static int32_t option_or(const codebrix_av1_encoder* e, int32_t option, int32_t fallback)
{
    return e->option_set[option] ? e->option_value[option] : fallback;
}

static int valid_ascii(const char* s, size_t max_length, int allow_empty)
{
    size_t n = 0;
    for (; s[n] != '\0'; n++) {
        unsigned char c = (unsigned char)s[n];
        if (c < 0x20 || c > 0x7E) return 0;
        if (n >= max_length) return 0;
    }
    return allow_empty || n > 0;
}

static char* duplicate(const char* s)
{
    size_t n = strlen(s) + 1;
    char* copy = (char*)malloc(n);
    if (copy != NULL) memcpy(copy, s, n);
    return copy;
}

/* ---- packet queue ------------------------------------------------------------------------- */

static int32_t queue_push(codebrix_av1_encoder* e, const uint8_t* data, size_t length, int64_t pts, int is_key)
{
    if (length > (size_t)INT32_MAX) return fail(e, CBA_E_AOM_FAULT, "a temporal unit larger than 2 GiB");
    if (e->packet_head > 0 && e->packet_head == e->packet_count) {
        e->packet_head = 0;
        e->packet_count = 0;
    }
    if (e->packet_count == e->packet_capacity) {
        if (e->packet_head > 0) {
            memmove(e->packets, e->packets + e->packet_head,
                    (size_t)(e->packet_count - e->packet_head) * sizeof(cba_packet));
            e->packet_count -= e->packet_head;
            e->packet_head = 0;
        } else {
            int32_t capacity = e->packet_capacity ? e->packet_capacity * 2 : 16;
            cba_packet* grown = (cba_packet*)realloc(e->packets, (size_t)capacity * sizeof(cba_packet));
            if (grown == NULL) return fail(e, CBA_E_OUT_OF_MEMORY, "out of memory (packet queue)");
            e->packets = grown;
            e->packet_capacity = capacity;
        }
    }
    cba_packet* p = &e->packets[e->packet_count];
    p->data = (uint8_t*)malloc(length > 0 ? length : 1);
    if (p->data == NULL) return fail(e, CBA_E_OUT_OF_MEMORY, "out of memory (packet)");
    if (length > 0) memcpy(p->data, data, length);
    p->length = (int32_t)length;
    p->pts = pts;
    p->is_key_frame = is_key ? 1 : 0;
    e->packet_count++;
    e->packets_produced++;
    return CBA_OK;
}

static void queue_free(codebrix_av1_encoder* e)
{
    for (int32_t i = e->packet_head; i < e->packet_count; i++) free(e->packets[i].data);
    free(e->packets);
    e->packets = NULL;
    e->packet_head = e->packet_count = e->packet_capacity = 0;
}

/* ---- the sequence header: find OBU_SEQUENCE_HEADER inside a temporal unit ------------------- */

static int read_leb128(const uint8_t* p, size_t avail, uint64_t* value, size_t* used)
{
    uint64_t v = 0;
    for (size_t i = 0; i < 8 && i < avail; i++) {
        v |= (uint64_t)(p[i] & 0x7F) << (7 * i);
        if ((p[i] & 0x80) == 0) { *value = v; *used = i + 1; return 1; }
    }
    return 0;
}

static void capture_sequence_header_from_unit(codebrix_av1_encoder* e, const uint8_t* tu, size_t length)
{
    size_t pos = 0;
    while (pos < length) {
        const uint8_t header = tu[pos];
        const int type = (header >> 3) & 0x0F;
        const int has_extension = (header >> 2) & 1;
        const int has_size = (header >> 1) & 1;
        size_t header_bytes = 1 + (size_t)has_extension;
        uint64_t payload = 0;
        size_t used = 0;
        if (!has_size || pos + header_bytes > length) return;
        if (!read_leb128(tu + pos + header_bytes, length - pos - header_bytes, &payload, &used)) return;
        size_t total = header_bytes + used + (size_t)payload;
        if (payload > length || pos + total > length) return;
        if (type == 1 /* OBU_SEQUENCE_HEADER */) {
            uint8_t* copy = (uint8_t*)malloc(total);
            if (copy == NULL) return;
            memcpy(copy, tu + pos, total);
            e->sequence_header = copy;
            e->sequence_header_length = (int32_t)total;
            return;
        }
        pos += total;
    }
}

/* ---- draining libaom ---------------------------------------------------------------------- */

/* Moves every packet libaom has ready into the queue. *got (optional) = number of frame packets. */
static int32_t drain_codec(codebrix_av1_encoder* e, int* got)
{
    aom_codec_iter_t iter = NULL;
    const aom_codec_cx_pkt_t* pkt;
    int count = 0;
    while ((pkt = aom_codec_get_cx_data(&e->codec, &iter)) != NULL) {
        if (pkt->kind != AOM_CODEC_CX_FRAME_PKT) continue;
        const int is_key = (pkt->data.frame.flags & AOM_FRAME_IS_KEY) != 0;
        if (e->sequence_header == NULL && is_key)
            capture_sequence_header_from_unit(e, (const uint8_t*)pkt->data.frame.buf, pkt->data.frame.sz);
        int32_t rc = queue_push(e, (const uint8_t*)pkt->data.frame.buf, pkt->data.frame.sz,
                                (int64_t)pkt->data.frame.pts, is_key);
        if (rc != CBA_OK) return rc;
        count++;
    }
    if (got != NULL) *got = count;
    return CBA_OK;
}

/* ---- versions ------------------------------------------------------------------------------- */

CBA_EXPORT int32_t codebrix_av1_api_version(void) { return CODEBRIX_AV1_API_VERSION; }

CBA_EXPORT const char* codebrix_av1_library_version(void) { return aom_codec_version_str(); }

/* ---- lifecycle ------------------------------------------------------------------------------ */

CBA_EXPORT int32_t codebrix_av1_encoder_create(
    int32_t width, int32_t height, int32_t bit_depth, int32_t layout,
    int32_t timebase_num, int32_t timebase_den, int32_t usage,
    codebrix_av1_encoder** out_encoder)
{
    if (out_encoder == NULL) return CBA_E_INVALID_ARGUMENT;
    *out_encoder = NULL;
    if (width < 1 || width > 65536 || height < 1 || height > 65536) return CBA_E_INVALID_ARGUMENT;
    if (bit_depth != 8 && bit_depth != 10 && bit_depth != 12) return CBA_E_INVALID_ARGUMENT;
    if (layout != CBA_LAYOUT_I420 && layout != CBA_LAYOUT_I444 && layout != CBA_LAYOUT_MONOCHROME)
        return CBA_E_INVALID_ARGUMENT;
    if (timebase_num < 1 || timebase_den < 1) return CBA_E_INVALID_ARGUMENT;
    if (usage != CBA_USAGE_GOOD_QUALITY && usage != CBA_USAGE_REALTIME && usage != CBA_USAGE_ALL_INTRA)
        return CBA_E_INVALID_ARGUMENT;
    /* libaom 3.15.2 does not code the last chroma column (odd width) / row (odd height) of a 4:2:0
     * picture faithfully: measured 2026-10-10 with aomenc itself, lossless, every frame including
     * key frames, decoded identically by aomdec and dav1d. I444 and MONOCHROME odd sizes are exact. */
    if (layout == CBA_LAYOUT_I420 && ((width & 1) != 0 || (height & 1) != 0)) return CBA_E_INVALID_ARGUMENT;

    codebrix_av1_encoder* e = (codebrix_av1_encoder*)calloc(1, sizeof *e);
    if (e == NULL) return CBA_E_OUT_OF_MEMORY;
    e->width = width;
    e->height = height;
    e->bit_depth = bit_depth;
    e->layout = layout;
    e->timebase_num = timebase_num;
    e->timebase_den = timebase_den;
    e->usage = usage;
    e->phase = CBA_PHASE_CREATED;
    *out_encoder = e;
    return CBA_OK;
}

CBA_EXPORT int32_t codebrix_av1_encoder_set_int32(codebrix_av1_encoder* e, int32_t option, int32_t value)
{
    if (e == NULL) return CBA_E_INVALID_ARGUMENT;
    clear_error(e);
    if (e->phase != CBA_PHASE_CREATED) return fail(e, CBA_E_INVALID_STATE, "options can only be set before start");

    int32_t lo, hi;
    switch (option) {
        case CBA_OPT_CPU_USED:                 lo = 0; hi = 9; break;
        case CBA_OPT_RATE_MODE:                lo = CBA_RATE_CONSTANT_QUALITY; hi = CBA_RATE_CONSTANT_BITRATE; break;
        case CBA_OPT_QUALITY:                  lo = 0; hi = 63; break;
        case CBA_OPT_TARGET_BITRATE_KBPS:      lo = 1; hi = 2000000; break;
        case CBA_OPT_LOSSLESS:                 lo = 0; hi = 1; break;
        case CBA_OPT_THREADS:                  lo = 1; hi = 64; break;
        case CBA_OPT_ROW_MULTITHREADING:       lo = 0; hi = 1; break;
        case CBA_OPT_TILE_COLUMNS_LOG2:        lo = 0; hi = 6; break;
        case CBA_OPT_TILE_ROWS_LOG2:           lo = 0; hi = 6; break;
        case CBA_OPT_LAG_IN_FRAMES:            lo = 0; hi = 48; break;
        case CBA_OPT_KEY_FRAME_MAX_DISTANCE:   lo = 0; hi = 65535; break;
        case CBA_OPT_COLOR_PRIMARIES:          lo = 0; hi = 255; break;
        case CBA_OPT_TRANSFER_CHARACTERISTICS: lo = 0; hi = 255; break;
        case CBA_OPT_MATRIX_COEFFICIENTS:      lo = 0; hi = 255; break;
        case CBA_OPT_COLOR_RANGE:              lo = CBA_RANGE_LIMITED; hi = CBA_RANGE_FULL; break;
        case CBA_OPT_CHROMA_SAMPLE_POSITION:   lo = CBA_CHROMA_POSITION_UNKNOWN; hi = CBA_CHROMA_POSITION_COLOCATED; break;
        case CBA_OPT_AUTOMATIC_KEY_FRAMES:     lo = 0; hi = 1; break;
        default: return fail(e, CBA_E_INVALID_ARGUMENT, "unknown option");
    }
    if (value < lo || value > hi) return fail(e, CBA_E_INVALID_ARGUMENT, "option value out of range");
    e->option_set[option] = 1;
    e->option_value[option] = value;
    return CBA_OK;
}

CBA_EXPORT int32_t codebrix_av1_encoder_set_string_option(
    codebrix_av1_encoder* e, const char* name_ascii, const char* value_ascii)
{
    if (e == NULL) return CBA_E_INVALID_ARGUMENT;
    clear_error(e);
    if (name_ascii == NULL || value_ascii == NULL) return fail(e, CBA_E_INVALID_ARGUMENT, "null option name or value");
    if (e->phase != CBA_PHASE_CREATED) return fail(e, CBA_E_INVALID_STATE, "options can only be set before start");
    if (!valid_ascii(name_ascii, 256, 0)) return fail(e, CBA_E_INVALID_ARGUMENT, "option name must be 1..256 printable ASCII characters");
    if (!valid_ascii(value_ascii, 4096, 1)) return fail(e, CBA_E_INVALID_ARGUMENT, "option value must be printable ASCII (at most 4096 characters)");

    if (e->string_option_count == e->string_option_capacity) {
        int32_t capacity = e->string_option_capacity ? e->string_option_capacity * 2 : 8;
        cba_string_option* grown = (cba_string_option*)realloc(e->string_options, (size_t)capacity * sizeof(cba_string_option));
        if (grown == NULL) return fail(e, CBA_E_OUT_OF_MEMORY, "out of memory");
        e->string_options = grown;
        e->string_option_capacity = capacity;
    }
    char* name = duplicate(name_ascii);
    char* value = duplicate(value_ascii);
    if (name == NULL || value == NULL) {
        free(name);
        free(value);
        return fail(e, CBA_E_OUT_OF_MEMORY, "out of memory");
    }
    e->string_options[e->string_option_count].name = name;
    e->string_options[e->string_option_count].value = value;
    e->string_option_count++;
    return CBA_OK;
}

/* Destroys a half-initialised libaom context after a failed start; the encoder stays CREATED. */
static int32_t abandon_start(codebrix_av1_encoder* e, int32_t code)
{
    if (e->codec_initialised) {
        aom_codec_destroy(&e->codec);
        e->codec_initialised = 0;
    }
    free(e->sequence_header);
    e->sequence_header = NULL;
    e->sequence_header_length = 0;
    free(e->grey_chroma);
    e->grey_chroma = NULL;
    return code;
}

#define CBA_CONTROL(ctrl, val, what)                                                            \
    do {                                                                                         \
        aom_codec_err_t err_ = aom_codec_control(&e->codec, ctrl, val);                          \
        if (err_ != AOM_CODEC_OK) { set_aom_error(e, what); return abandon_start(e, map_start_error(err_)); } \
    } while (0)

CBA_EXPORT int32_t codebrix_av1_encoder_start(codebrix_av1_encoder* e)
{
    if (e == NULL) return CBA_E_INVALID_ARGUMENT;
    clear_error(e);
    if (e->phase != CBA_PHASE_CREATED) return fail(e, CBA_E_INVALID_STATE, "start was already called");

    const int32_t rate_mode = option_or(e, CBA_OPT_RATE_MODE, CBA_RATE_CONSTANT_QUALITY);
    if (rate_mode != CBA_RATE_CONSTANT_QUALITY && !option_is_set(e, CBA_OPT_TARGET_BITRATE_KBPS))
        return fail(e, CBA_E_UNSUPPORTED, "this rate-control mode needs CBA_OPT_TARGET_BITRATE_KBPS");

    /* libaom (validate_img) refuses matrix_coefficients 0 (identity / RGB) for any subsampled
     * input, and a MONOCHROME stream is fed 4:2:0 (see encode). Say so now, not at the first
     * encode. */
    if (e->layout != CBA_LAYOUT_I444 && option_or(e, CBA_OPT_MATRIX_COEFFICIENTS, 2) == 0)
        return fail(e, CBA_E_UNSUPPORTED, "matrix coefficients 0 (identity) need 4:4:4 (CBA_LAYOUT_I444): "
                                          "AV1 forbids them with subsampled chroma, and libaom refuses them for I420 and MONOCHROME input");

    aom_codec_iface_t* iface = aom_codec_av1_cx();
    unsigned int aom_usage = e->usage == CBA_USAGE_REALTIME ? AOM_USAGE_REALTIME
                           : e->usage == CBA_USAGE_ALL_INTRA ? AOM_USAGE_ALL_INTRA
                           : AOM_USAGE_GOOD_QUALITY;
    aom_codec_enc_cfg_t cfg;
    aom_codec_err_t err = aom_codec_enc_config_default(iface, &cfg, aom_usage);
    if (err != AOM_CODEC_OK) {
        snprintf(e->last_error, sizeof e->last_error, "aom_codec_enc_config_default: %s", aom_codec_err_to_string(err));
        return map_start_error(err);
    }

    cfg.g_w = (unsigned int)e->width;
    cfg.g_h = (unsigned int)e->height;
    cfg.g_profile = e->bit_depth == 12 ? 2u : (e->layout == CBA_LAYOUT_I444 ? 1u : 0u);
    cfg.g_bit_depth = e->bit_depth == 12 ? AOM_BITS_12 : e->bit_depth == 10 ? AOM_BITS_10 : AOM_BITS_8;
    cfg.g_input_bit_depth = (unsigned int)e->bit_depth;
    cfg.monochrome = e->layout == CBA_LAYOUT_MONOCHROME ? 1u : 0u;
    cfg.g_timebase.num = e->timebase_num;
    cfg.g_timebase.den = e->timebase_den;
    cfg.rc_end_usage = rate_mode == CBA_RATE_CONSTRAINED_QUALITY ? AOM_CQ
                     : rate_mode == CBA_RATE_VARIABLE_BITRATE ? AOM_VBR
                     : rate_mode == CBA_RATE_CONSTANT_BITRATE ? AOM_CBR
                     : AOM_Q;
    if (option_is_set(e, CBA_OPT_TARGET_BITRATE_KBPS))
        cfg.rc_target_bitrate = (unsigned int)e->option_value[CBA_OPT_TARGET_BITRATE_KBPS];
    cfg.kf_min_dist = 0;
    if (option_or(e, CBA_OPT_AUTOMATIC_KEY_FRAMES, 1) == 0) {
        /* No key frame of libaom's own: frame 0 and the forced ones only (kf-max-dist ignored). */
        cfg.kf_mode = AOM_KF_DISABLED;
    } else if (option_is_set(e, CBA_OPT_KEY_FRAME_MAX_DISTANCE)) {
        cfg.kf_max_dist = (unsigned int)e->option_value[CBA_OPT_KEY_FRAME_MAX_DISTANCE];
    }
    if (option_is_set(e, CBA_OPT_THREADS))
        cfg.g_threads = (unsigned int)e->option_value[CBA_OPT_THREADS];
    if (option_is_set(e, CBA_OPT_LAG_IN_FRAMES))
        cfg.g_lag_in_frames = (unsigned int)e->option_value[CBA_OPT_LAG_IN_FRAMES];
    /* Lossless ALWAYS runs with lag 0. libaom 3.15.2 with look-ahead is NOT bit-exact at 10 bits:
     * measured 2026-10-10 (shim AND aomenc --lossless=1, decoded by aomdec and dav1d alike): a few
     * wrong samples on some inter frames, content- and size-dependent (e.g. 162x92 / 164x92 / 136x92
     * I420 chroma at the right edge, 320x180 MONOCHROME luma), independent of cpu-used, threads,
     * arnr-*, overlay and keyframe filtering. lag 0 (and, alternatively, enable-order-hint=0) was
     * exact in every case tried; lag 0 is the smaller machine (no look-ahead, no TPL), so it wins. */
    if (option_or(e, CBA_OPT_LOSSLESS, 0) == 1)
        cfg.g_lag_in_frames = 0;

    const aom_codec_flags_t flags = e->bit_depth > 8 ? AOM_CODEC_USE_HIGHBITDEPTH : 0;
    memset(&e->codec, 0, sizeof e->codec);
    err = aom_codec_enc_init(&e->codec, iface, &cfg, flags);
    if (err != AOM_CODEC_OK) {
        /* On failure libaom has already torn its context down, but keeps the error and its
         * detail readable (aom_encoder.c: the detail is a string literal). */
        set_aom_error(e, "aom_codec_enc_init");
        memset(&e->codec, 0, sizeof e->codec);
        return map_start_error(err);
    }
    e->codec_initialised = 1;

    if (rate_mode == CBA_RATE_CONSTANT_QUALITY || rate_mode == CBA_RATE_CONSTRAINED_QUALITY)
        CBA_CONTROL(AOME_SET_CQ_LEVEL, (unsigned int)option_or(e, CBA_OPT_QUALITY, 30), "cq-level");
    if (option_is_set(e, CBA_OPT_CPU_USED))
        CBA_CONTROL(AOME_SET_CPUUSED, (int)e->option_value[CBA_OPT_CPU_USED], "cpu-used");
    if (option_is_set(e, CBA_OPT_LOSSLESS))
        CBA_CONTROL(AV1E_SET_LOSSLESS, (unsigned int)e->option_value[CBA_OPT_LOSSLESS], "lossless");
    if (option_is_set(e, CBA_OPT_ROW_MULTITHREADING))
        CBA_CONTROL(AV1E_SET_ROW_MT, (unsigned int)e->option_value[CBA_OPT_ROW_MULTITHREADING], "row-mt");
    if (option_is_set(e, CBA_OPT_TILE_COLUMNS_LOG2))
        CBA_CONTROL(AV1E_SET_TILE_COLUMNS, (unsigned int)e->option_value[CBA_OPT_TILE_COLUMNS_LOG2], "tile-columns");
    /* The chroma subsampling libaom writes for the Professional profile (12-bit) comes from these
     * two controls (their default is 0,0 = 4:4:4), not from the image; set them for every layout so
     * the sequence header known at start is the one the stream carries. (Profiles 0 and 1 imply
     * their subsampling and ignore the controls.) */
    CBA_CONTROL(AV1E_SET_CHROMA_SUBSAMPLING_X, e->layout == CBA_LAYOUT_I444 ? 0u : 1u, "chroma-subsampling-x");
    CBA_CONTROL(AV1E_SET_CHROMA_SUBSAMPLING_Y, e->layout == CBA_LAYOUT_I444 ? 0u : 1u, "chroma-subsampling-y");
    if (option_is_set(e, CBA_OPT_TILE_ROWS_LOG2))
        CBA_CONTROL(AV1E_SET_TILE_ROWS, (unsigned int)e->option_value[CBA_OPT_TILE_ROWS_LOG2], "tile-rows");
    CBA_CONTROL(AV1E_SET_COLOR_PRIMARIES, (int)option_or(e, CBA_OPT_COLOR_PRIMARIES, 2), "color-primaries");
    CBA_CONTROL(AV1E_SET_TRANSFER_CHARACTERISTICS, (int)option_or(e, CBA_OPT_TRANSFER_CHARACTERISTICS, 2), "transfer-characteristics");
    CBA_CONTROL(AV1E_SET_MATRIX_COEFFICIENTS, (int)option_or(e, CBA_OPT_MATRIX_COEFFICIENTS, 2), "matrix-coefficients");
    CBA_CONTROL(AV1E_SET_COLOR_RANGE, (int)option_or(e, CBA_OPT_COLOR_RANGE, CBA_RANGE_LIMITED), "color-range");
    if (e->layout == CBA_LAYOUT_I420 && option_is_set(e, CBA_OPT_CHROMA_SAMPLE_POSITION))
        CBA_CONTROL(AV1E_SET_CHROMA_SAMPLE_POSITION, (int)e->option_value[CBA_OPT_CHROMA_SAMPLE_POSITION], "chroma-sample-position");

    for (int32_t i = 0; i < e->string_option_count; i++) {
        err = aom_codec_set_option(&e->codec, e->string_options[i].name, e->string_options[i].value);
        if (err != AOM_CODEC_OK) {
            char context[300];
            snprintf(context, sizeof context, "option \"%s\"", e->string_options[i].name);
            set_aom_error(e, context);
            return abandon_start(e, map_start_error(err));
        }
    }

    /* Neutral chroma for MONOCHROME: one mid-grey plane, used for both U and V of every frame. */
    if (e->layout == CBA_LAYOUT_MONOCHROME) {
        const int32_t cw = chroma_width(e), ch = chroma_height(e), bps = bytes_per_sample(e);
        e->grey_stride = cw * bps;
        e->grey_chroma = (uint8_t*)malloc((size_t)e->grey_stride * (size_t)ch);
        if (e->grey_chroma == NULL) { snprintf(e->last_error, sizeof e->last_error, "out of memory"); return abandon_start(e, CBA_E_OUT_OF_MEMORY); }
        if (bps == 1) {
            memset(e->grey_chroma, 128, (size_t)e->grey_stride * (size_t)ch);
        } else {
            const uint16_t mid = (uint16_t)(1u << (e->bit_depth - 1));
            const size_t samples = (size_t)cw * (size_t)ch;
            for (size_t i = 0; i < samples; i++) memcpy(e->grey_chroma + i * 2, &mid, 2);
        }
    }

    e->phase = CBA_PHASE_STARTED;
    e->last_pts = INT64_MIN;
    return CBA_OK;
}

CBA_EXPORT int32_t codebrix_av1_encoder_encode(
    codebrix_av1_encoder* e,
    const uint8_t* y, int32_t y_stride_bytes,
    const uint8_t* u, int32_t u_stride_bytes,
    const uint8_t* v, int32_t v_stride_bytes,
    int64_t pts,
    int32_t force_key_frame)
{
    if (e == NULL) return CBA_E_INVALID_ARGUMENT;
    clear_error(e);
    if (e->phase == CBA_PHASE_CREATED) return fail(e, CBA_E_INVALID_STATE, "encode before start");
    if (e->phase == CBA_PHASE_FLUSHED) return fail(e, CBA_E_INVALID_STATE, "encode after flush");

    const int32_t bps = bytes_per_sample(e);
    const int mono = e->layout == CBA_LAYOUT_MONOCHROME;
    if (y == NULL) return fail(e, CBA_E_INVALID_ARGUMENT, "null luma plane");
    if ((int64_t)y_stride_bytes < (int64_t)e->width * bps) return fail(e, CBA_E_INVALID_ARGUMENT, "luma stride smaller than a row");
    if (!mono) {
        const int64_t row = (int64_t)chroma_width(e) * bps;
        if (u == NULL || v == NULL) return fail(e, CBA_E_INVALID_ARGUMENT, "null chroma plane");
        if ((int64_t)u_stride_bytes < row || (int64_t)v_stride_bytes < row)
            return fail(e, CBA_E_INVALID_ARGUMENT, "chroma stride smaller than a row");
    }
    if (force_key_frame != 0 && force_key_frame != 1) return fail(e, CBA_E_INVALID_ARGUMENT, "force_key_frame must be 0 or 1");
    if (e->frames_submitted > 0 && pts <= e->last_pts)
        return fail(e, CBA_E_INVALID_ARGUMENT, "pts must be greater than the previous frame's pts");

    aom_img_fmt_t fmt = e->layout == CBA_LAYOUT_I444 ? AOM_IMG_FMT_I444 : AOM_IMG_FMT_I420;
    if (e->bit_depth > 8) fmt = (aom_img_fmt_t)(fmt | AOM_IMG_FMT_HIGHBITDEPTH);

    aom_image_t img;
    if (aom_img_wrap(&img, fmt, (unsigned int)e->width, (unsigned int)e->height, 1, (unsigned char*)y) == NULL)
        return fail(e, CBA_E_AOM_FAULT, "aom_img_wrap failed");
    img.bit_depth = (unsigned int)e->bit_depth;
    img.cp = (aom_color_primaries_t)option_or(e, CBA_OPT_COLOR_PRIMARIES, 2);
    img.tc = (aom_transfer_characteristics_t)option_or(e, CBA_OPT_TRANSFER_CHARACTERISTICS, 2);
    img.mc = (aom_matrix_coefficients_t)option_or(e, CBA_OPT_MATRIX_COEFFICIENTS, 2);
    img.range = option_or(e, CBA_OPT_COLOR_RANGE, CBA_RANGE_LIMITED) == CBA_RANGE_FULL ? AOM_CR_FULL_RANGE : AOM_CR_STUDIO_RANGE;
    img.csp = e->layout == CBA_LAYOUT_I420 ? (aom_chroma_sample_position_t)option_or(e, CBA_OPT_CHROMA_SAMPLE_POSITION, CBA_CHROMA_POSITION_UNKNOWN)
                                           : AOM_CSP_UNKNOWN;
    img.monochrome = mono ? 1 : 0;

    img.planes[AOM_PLANE_Y] = (unsigned char*)y;
    img.stride[AOM_PLANE_Y] = y_stride_bytes;
    if (mono) {
        img.planes[AOM_PLANE_U] = e->grey_chroma;
        img.planes[AOM_PLANE_V] = e->grey_chroma;
        img.stride[AOM_PLANE_U] = e->grey_stride;
        img.stride[AOM_PLANE_V] = e->grey_stride;
    } else {
        img.planes[AOM_PLANE_U] = (unsigned char*)u;
        img.stride[AOM_PLANE_U] = u_stride_bytes;
        if (u_stride_bytes == v_stride_bytes) {
            img.planes[AOM_PLANE_V] = (unsigned char*)v;
            img.stride[AOM_PLANE_V] = v_stride_bytes;
        } else {
            /* libaom requires equal U/V strides: copy V into shim memory with U's stride. */
            const int32_t ch = chroma_height(e);
            const size_t row = (size_t)chroma_width(e) * (size_t)bps;
            const size_t needed = (size_t)u_stride_bytes * (size_t)ch;
            if (e->v_copy == NULL || e->v_copy_size < needed) {
                uint8_t* grown = (uint8_t*)realloc(e->v_copy, needed);
                if (grown == NULL) return fail(e, CBA_E_OUT_OF_MEMORY, "out of memory (V plane copy)");
                e->v_copy = grown;
                e->v_copy_size = needed;
            }
            for (int32_t r = 0; r < ch; r++)
                memcpy(e->v_copy + (size_t)r * (size_t)u_stride_bytes, v + (size_t)r * (size_t)v_stride_bytes, row);
            img.planes[AOM_PLANE_V] = e->v_copy;
            img.stride[AOM_PLANE_V] = u_stride_bytes;
        }
    }

    const int64_t delta = e->frames_submitted > 0 ? pts - e->last_pts : 1;
    const unsigned long duration = delta > 0 && delta <= 0x7FFFFFFF ? (unsigned long)delta : 1UL;
    const aom_enc_frame_flags_t frame_flags = force_key_frame ? AOM_EFLAG_FORCE_KF : 0;

    aom_codec_err_t err = aom_codec_encode(&e->codec, &img, (aom_codec_pts_t)pts, duration, frame_flags);
    if (err != AOM_CODEC_OK) {
        set_aom_error(e, NULL);
        return map_run_error(err);
    }
    e->frames_submitted++;
    e->last_pts = pts;
    return drain_codec(e, NULL);
}

CBA_EXPORT int32_t codebrix_av1_encoder_flush(codebrix_av1_encoder* e)
{
    if (e == NULL) return CBA_E_INVALID_ARGUMENT;
    clear_error(e);
    if (e->phase == CBA_PHASE_CREATED) return fail(e, CBA_E_INVALID_STATE, "flush before start");
    if (e->phase == CBA_PHASE_FLUSHED) return CBA_OK;
    e->phase = CBA_PHASE_FLUSHED;
    if (e->frames_submitted == 0) return CBA_OK;
    for (;;) {
        aom_codec_err_t err = aom_codec_encode(&e->codec, NULL, 0, 0, 0);
        if (err != AOM_CODEC_OK) {
            set_aom_error(e, "flush");
            return map_run_error(err);
        }
        int got = 0;
        int32_t rc = drain_codec(e, &got);
        if (rc != CBA_OK) return rc;
        if (got == 0) break;
    }
    return CBA_OK;
}

/* ---- packets -------------------------------------------------------------------------------- */

CBA_EXPORT int32_t codebrix_av1_encoder_pending_packets(codebrix_av1_encoder* e)
{
    if (e == NULL) return CBA_E_INVALID_ARGUMENT;
    clear_error(e);
    return e->packet_count - e->packet_head;
}

CBA_EXPORT int32_t codebrix_av1_encoder_packet_info(
    codebrix_av1_encoder* e, int32_t* out_length, int64_t* out_pts, int32_t* out_is_key_frame)
{
    if (e == NULL) return CBA_E_INVALID_ARGUMENT;
    clear_error(e);
    if (out_length == NULL || out_pts == NULL || out_is_key_frame == NULL)
        return fail(e, CBA_E_INVALID_ARGUMENT, "null output pointer");
    if (e->packet_head >= e->packet_count) return fail(e, CBA_E_INVALID_STATE, "no packet pending");
    const cba_packet* p = &e->packets[e->packet_head];
    *out_length = p->length;
    *out_pts = p->pts;
    *out_is_key_frame = p->is_key_frame;
    return CBA_OK;
}

CBA_EXPORT int32_t codebrix_av1_encoder_read_packet(codebrix_av1_encoder* e, uint8_t* destination, int32_t capacity)
{
    if (e == NULL) return CBA_E_INVALID_ARGUMENT;
    clear_error(e);
    if (e->packet_head >= e->packet_count) return fail(e, CBA_E_INVALID_STATE, "no packet pending");
    cba_packet* p = &e->packets[e->packet_head];
    if (destination == NULL || capacity < 0) return fail(e, CBA_E_INVALID_ARGUMENT, "null destination or negative capacity");
    if (capacity < p->length) return fail(e, CBA_E_BUFFER_TOO_SMALL, "destination smaller than the packet");
    const int32_t length = p->length;
    if (length > 0) memcpy(destination, p->data, (size_t)length);
    free(p->data);
    p->data = NULL;
    e->packet_head++;
    if (e->packet_head == e->packet_count) e->packet_head = e->packet_count = 0;
    return length;
}

/* ---- sequence header ------------------------------------------------------------------------ */

CBA_EXPORT int32_t codebrix_av1_encoder_sequence_header_length(codebrix_av1_encoder* e)
{
    if (e == NULL) return CBA_E_INVALID_ARGUMENT;
    clear_error(e);
    if (e->phase == CBA_PHASE_CREATED) return fail(e, CBA_E_INVALID_STATE, "the sequence header exists after start");
    if (e->sequence_header == NULL)
        return fail(e, CBA_E_INVALID_STATE, "the sequence header is known once the first packet (a key frame) has been produced");
    return e->sequence_header_length;
}

CBA_EXPORT int32_t codebrix_av1_encoder_read_sequence_header(codebrix_av1_encoder* e, uint8_t* destination, int32_t capacity)
{
    if (e == NULL) return CBA_E_INVALID_ARGUMENT;
    clear_error(e);
    if (e->phase == CBA_PHASE_CREATED) return fail(e, CBA_E_INVALID_STATE, "the sequence header exists after start");
    if (e->sequence_header == NULL)
        return fail(e, CBA_E_INVALID_STATE, "the sequence header is known once the first packet (a key frame) has been produced");
    if (destination == NULL || capacity < 0) return fail(e, CBA_E_INVALID_ARGUMENT, "null destination or negative capacity");
    if (capacity < e->sequence_header_length) return fail(e, CBA_E_BUFFER_TOO_SMALL, "destination smaller than the sequence header");
    memcpy(destination, e->sequence_header, (size_t)e->sequence_header_length);
    return e->sequence_header_length;
}

/* ---- progress / diagnostics ----------------------------------------------------------------- */

CBA_EXPORT int64_t codebrix_av1_encoder_frames_submitted(codebrix_av1_encoder* e)
{
    if (e == NULL) return CBA_E_INVALID_ARGUMENT;
    clear_error(e);
    return e->frames_submitted;
}

CBA_EXPORT int64_t codebrix_av1_encoder_packets_produced(codebrix_av1_encoder* e)
{
    if (e == NULL) return CBA_E_INVALID_ARGUMENT;
    clear_error(e);
    return e->packets_produced;
}

CBA_EXPORT const char* codebrix_av1_encoder_last_error(codebrix_av1_encoder* e)
{
    if (e == NULL) return "";
    return e->last_error;
}

CBA_EXPORT void codebrix_av1_encoder_destroy(codebrix_av1_encoder* e)
{
    if (e == NULL) return;
    if (e->codec_initialised) aom_codec_destroy(&e->codec);
    queue_free(e);
    for (int32_t i = 0; i < e->string_option_count; i++) {
        free(e->string_options[i].name);
        free(e->string_options[i].value);
    }
    free(e->string_options);
    free(e->sequence_header);
    free(e->grey_chroma);
    free(e->v_copy);
    free(e);
}
