/*
 * smoke-test.c - the load-and-run verification program for the codebrix_av1 native library
 *
 * Part of CodeBrix.VideoEncoding.Av1's av1-native-tools. Every platform's gate builds it through
 * shim/CMakeLists.txt (-DCBA_BUILD_SMOKE_TEST=ON -> target codebrix_av1_smoke) and runs it against
 * the STAGED (stripped, about-to-be-shipped) library, so the bytes that are tested are the bytes
 * that ship:
 *
 *     ./codebrix_av1_smoke <path-to-library> [ivf-output-prefix]
 *
 * It loads the library the way .NET's DllImport does (dlopen / LoadLibrary, by path) and resolves
 * every entry point of codebrix_av1.h by name. NO decoder exists in this build (libaom is built
 * encoder-only and the smoke test links no libaom at all), so every check is at the bitstream
 * level: this file parses OBUs, the sequence header and the start of each frame header itself.
 *
 *   1. api_version == 1; library_version names libaom 3.15.2.
 *   2. 48 synthetic frames (a moving gradient + box) at 320x180, pts = frame index, a FORCED key
 *      frame at 24, in five configurations: I420 8-bit, I420 10-bit, I444 8-bit, MONOCHROME 8-bit
 *      full range and MONOCHROME 10-bit full range (plus I420 / I444 12-bit, Professional profile,
 *      and I444 10-bit). For each: the sequence header is NOT available between start and the
 *      first packet (CBA_E_INVALID_STATE - the shim takes it from the first key frame, see
 *      codebrix_av1.c), then parses as an
 *      OBU_SEQUENCE_HEADER with the profile / bit depth / mono_chrome / subsampling / colour
 *      description / colour range / chroma sample position asked for; packets == frames after
 *      flush; pts strictly increasing and == the frame index; every packet starts with a temporal
 *      delimiter OBU (0x12 0x00) and is a whole number of size-delimited OBUs; every packet holds
 *      exactly ONE shown frame (show_frame or show_existing_frame); packet 0 and packet 24 are key
 *      frames; is_key_frame matches "the shown frame is a KEY_FRAME"; every key frame carries a
 *      sequence header byte-identical to read_sequence_header (so the global-header route equals
 *      the in-stream header); frames_submitted == packets_produced == 48.
 *   3. Lossless (8-bit I420 and 10-bit MONOCHROME): every key frame has base_q_idx 0 and no DC/AC
 *      delta - the AV1 lossless condition - while the lossy default has base_q_idx > 0.
 *   4. kf-max-dist 0 and ALL_INTRA usage: every packet a key frame. REALTIME usage (lag 0): one
 *      packet out per frame in, immediately.
 *   5. U/V planes with DIFFERENT strides give byte-identical packets to equal strides (the shim's V
 *      copy path); MONOCHROME accepts NULL U/V.
 *   6. The contract's error paths: null handles, bad create arguments, unknown / out-of-range
 *      options, every set_* and start after start, encode before start / after flush, flush twice,
 *      read / info with nothing pending, short packet and sequence-header buffers (the packet stays
 *      queued), the sequence header before start, a non-increasing pts, a null luma plane, a rate
 *      mode without its bitrate and an option libaom rejects (CBA_E_UNSUPPORTED + last_error), an
 *      accepted string option, flush without frames.
 *   7. With a prefix argument, each configuration's packets are also written as <prefix>-<name>.ivf
 *      (for ffprobe / dav1d by hand).
 * Exit code 0 means every check passed ("SMOKE TEST PASSED"); anything else names what failed.
 *
 * Function types are declared by hand from codebrix_av1.h (int32_t / int64_t / pointers only,
 * default C calling convention on every platform).
 */

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#  define WIN32_LEAN_AND_MEAN   /* keeps rpcndr.h out: it #defines small, near, far, hyper ... */
#  include <windows.h>
typedef HMODULE lib_handle;
static lib_handle lib_open(const char* path) { return LoadLibraryA(path); }
static void* lib_sym(lib_handle h, const char* name) { return (void*)GetProcAddress(h, name); }
#else
#  include <dlfcn.h>
typedef void* lib_handle;
static lib_handle lib_open(const char* path) { return dlopen(path, RTLD_NOW | RTLD_LOCAL); }
static void* lib_sym(lib_handle h, const char* name) { return dlsym(h, name); }
#endif

/* ---- the contract (codebrix_av1.h, by value) ------------------------------------------------ */

#define CBA_OK                      0
#define CBA_E_INVALID_ARGUMENT     -1
#define CBA_E_INVALID_STATE        -3
#define CBA_E_UNSUPPORTED          -4
#define CBA_E_BUFFER_TOO_SMALL     -5

#define LAYOUT_I420 0
#define LAYOUT_I444 1
#define LAYOUT_MONO 2
#define USAGE_GOOD 0
#define USAGE_REALTIME 1
#define USAGE_ALL_INTRA 2

#define OPT_CPU_USED 1
#define OPT_RATE_MODE 2
#define OPT_QUALITY 3
#define OPT_TARGET_BITRATE 4
#define OPT_LOSSLESS 5
#define OPT_THREADS 6
#define OPT_LAG 10
#define OPT_KF_MAX 11
#define OPT_PRIMARIES 12
#define OPT_TRANSFER 13
#define OPT_MATRIX 14
#define OPT_RANGE 15
#define OPT_CHROMA_POS 16

typedef struct enc enc;
typedef int32_t     (*fn_api_version)(void);
typedef const char* (*fn_library_version)(void);
typedef int32_t     (*fn_create)(int32_t, int32_t, int32_t, int32_t, int32_t, int32_t, int32_t, enc**);
typedef int32_t     (*fn_set_int32)(enc*, int32_t, int32_t);
typedef int32_t     (*fn_set_string)(enc*, const char*, const char*);
typedef int32_t     (*fn_op)(enc*);
typedef int32_t     (*fn_encode)(enc*, const uint8_t*, int32_t, const uint8_t*, int32_t, const uint8_t*, int32_t, int64_t, int32_t);
typedef int32_t     (*fn_packet_info)(enc*, int32_t*, int64_t*, int32_t*);
typedef int32_t     (*fn_read)(enc*, uint8_t*, int32_t);
typedef int64_t     (*fn_i64)(enc*);
typedef const char* (*fn_last_error)(enc*);
typedef void        (*fn_destroy)(enc*);

static fn_api_version     p_api_version;
static fn_library_version p_library_version;
static fn_create          p_create;
static fn_set_int32       p_set_int32;
static fn_set_string      p_set_string_option;
static fn_op              p_start;
static fn_encode          p_encode;
static fn_op              p_flush;
static fn_op              p_pending_packets;
static fn_packet_info     p_packet_info;
static fn_read            p_read_packet;
static fn_op              p_sequence_header_length;
static fn_read            p_read_sequence_header;
static fn_i64             p_frames_submitted;
static fn_i64             p_packets_produced;
static fn_last_error      p_last_error;
static fn_destroy         p_destroy;

static int failures = 0;
static int checks = 0;

#define CHECK(cond, ...) do { checks++; if (!(cond)) { fprintf(stderr, "FAIL: "); fprintf(stderr, __VA_ARGS__); fprintf(stderr, "\n"); failures++; } \
                              else { printf("ok:   "); printf(__VA_ARGS__); printf("\n"); } } while (0)
/* A check that is counted but only printed when it fails (per-packet loops). */
#define QUIET_CHECK(cond, ...) do { checks++; if (!(cond)) { fprintf(stderr, "FAIL: "); fprintf(stderr, __VA_ARGS__); fprintf(stderr, "\n"); failures++; } } while (0)

/* ---- bit reader --------------------------------------------------------------------------- */

typedef struct bits { const uint8_t* p; size_t n; size_t pos; int overrun; } bits;

static uint32_t f(bits* b, int count)
{
    uint32_t v = 0;
    for (int i = 0; i < count; i++) {
        size_t byte = b->pos >> 3;
        int bit = 0;
        if (byte < b->n) bit = (b->p[byte] >> (7 - (b->pos & 7))) & 1;
        else b->overrun = 1;
        v = (v << 1) | (uint32_t)bit;
        b->pos++;
    }
    return v;
}

static uint32_t uvlc(bits* b)
{
    int leading = 0;
    while (!f(b, 1)) { if (++leading >= 32 || b->overrun) return 0; }
    return leading ? f(b, leading) + ((1u << leading) - 1) : 0;
}

static int floor_log2(uint32_t x) { int s = 0; while (x > 1) { x >>= 1; s++; } return s; }
static uint32_t ns(bits* b, uint32_t n)
{
    int w = floor_log2(n) + 1;
    uint32_t m = (1u << w) - n;
    uint32_t v = f(b, w - 1);
    if (v < m) return v;
    return (v << 1) - m + f(b, 1);
}
static int tile_log2(int blk, int target) { int k = 0; while ((blk << k) < target) k++; return k; }

/* ---- sequence header ---------------------------------------------------------------------- */

typedef struct seq_info {
    int ok;
    int profile, still_picture, reduced_still_picture_header;
    int timing_info_present, decoder_model_info_present, equal_picture_interval;
    int frame_presentation_time_length;
    int seq_level_idx0, seq_tier0;
    int frame_width_bits, frame_height_bits, max_width, max_height;
    int frame_id_numbers_present, id_len;
    int use_128x128;
    int enable_order_hint, order_hint_bits;
    int seq_force_screen_content_tools, seq_force_integer_mv;
    int enable_superres;
    int bit_depth, mono_chrome, color_description_present, cp, tc, mc, color_range;
    int subsampling_x, subsampling_y, chroma_sample_position, separate_uv_delta_q;
    int film_grain_params_present;
} seq_info;

static int read_leb128(const uint8_t* p, size_t avail, uint64_t* value, size_t* used)
{
    uint64_t v = 0;
    for (size_t i = 0; i < 8 && i < avail; i++) {
        v |= (uint64_t)(p[i] & 0x7F) << (7 * i);
        if ((p[i] & 0x80) == 0) { *value = v; *used = i + 1; return 1; }
    }
    return 0;
}

/* Parses a sequence_header_obu PAYLOAD (AV1 spec 5.5). */
static seq_info parse_sequence_header(const uint8_t* payload, size_t n)
{
    seq_info s;
    memset(&s, 0, sizeof s);
    bits b = { payload, n, 0, 0 };
    s.profile = (int)f(&b, 3);
    s.still_picture = (int)f(&b, 1);
    s.reduced_still_picture_header = (int)f(&b, 1);
    int buffer_delay_length = 0;
    if (s.reduced_still_picture_header) {
        s.seq_level_idx0 = (int)f(&b, 5);
    } else {
        s.timing_info_present = (int)f(&b, 1);
        if (s.timing_info_present) {
            f(&b, 32); f(&b, 32);
            s.equal_picture_interval = (int)f(&b, 1);
            if (s.equal_picture_interval) uvlc(&b);
            s.decoder_model_info_present = (int)f(&b, 1);
            if (s.decoder_model_info_present) {
                buffer_delay_length = (int)f(&b, 5) + 1;
                f(&b, 32);
                f(&b, 5);
                s.frame_presentation_time_length = (int)f(&b, 5) + 1;
            }
        }
        int initial_display_delay_present = (int)f(&b, 1);
        int count = (int)f(&b, 5) + 1;
        for (int i = 0; i < count; i++) {
            f(&b, 12);
            int level = (int)f(&b, 5);
            int tier = level > 7 ? (int)f(&b, 1) : 0;
            if (i == 0) { s.seq_level_idx0 = level; s.seq_tier0 = tier; }
            if (s.decoder_model_info_present) {
                if (f(&b, 1)) { f(&b, buffer_delay_length); f(&b, buffer_delay_length); f(&b, 1); }
            }
            if (initial_display_delay_present) { if (f(&b, 1)) f(&b, 4); }
        }
    }
    s.frame_width_bits = (int)f(&b, 4) + 1;
    s.frame_height_bits = (int)f(&b, 4) + 1;
    s.max_width = (int)f(&b, s.frame_width_bits) + 1;
    s.max_height = (int)f(&b, s.frame_height_bits) + 1;
    if (!s.reduced_still_picture_header) s.frame_id_numbers_present = (int)f(&b, 1);
    if (s.frame_id_numbers_present) {
        int delta = (int)f(&b, 4) + 2;
        int additional = (int)f(&b, 3) + 1;
        s.id_len = delta + additional;
    }
    s.use_128x128 = (int)f(&b, 1);
    f(&b, 1); f(&b, 1);   /* enable_filter_intra, enable_intra_edge_filter */
    s.seq_force_screen_content_tools = 2;
    s.seq_force_integer_mv = 2;
    if (!s.reduced_still_picture_header) {
        f(&b, 1); f(&b, 1); f(&b, 1); f(&b, 1);   /* interintra, masked, warped, dual filter */
        s.enable_order_hint = (int)f(&b, 1);
        if (s.enable_order_hint) { f(&b, 1); f(&b, 1); }   /* jnt_comp, ref_frame_mvs */
        if (f(&b, 1)) s.seq_force_screen_content_tools = 2;
        else s.seq_force_screen_content_tools = (int)f(&b, 1);
        if (s.seq_force_screen_content_tools > 0) {
            if (f(&b, 1)) s.seq_force_integer_mv = 2;
            else s.seq_force_integer_mv = (int)f(&b, 1);
        }
        if (s.enable_order_hint) s.order_hint_bits = (int)f(&b, 3) + 1;
    }
    s.enable_superres = (int)f(&b, 1);
    f(&b, 1); f(&b, 1);   /* enable_cdef, enable_restoration */
    /* color_config */
    int high_bitdepth = (int)f(&b, 1);
    s.bit_depth = 8;
    if (s.profile == 2 && high_bitdepth) s.bit_depth = f(&b, 1) ? 12 : 10;
    else if (high_bitdepth) s.bit_depth = 10;
    s.mono_chrome = s.profile == 1 ? 0 : (int)f(&b, 1);
    s.color_description_present = (int)f(&b, 1);
    s.cp = 2; s.tc = 2; s.mc = 2;
    if (s.color_description_present) { s.cp = (int)f(&b, 8); s.tc = (int)f(&b, 8); s.mc = (int)f(&b, 8); }
    if (s.mono_chrome) {
        s.color_range = (int)f(&b, 1);
        s.subsampling_x = s.subsampling_y = 1;
        s.chroma_sample_position = 0;
        s.separate_uv_delta_q = 0;
    } else {
        if (s.cp == 1 && s.tc == 13 && s.mc == 0) {
            s.color_range = 1;
            s.subsampling_x = s.subsampling_y = 0;
        } else {
            s.color_range = (int)f(&b, 1);
            if (s.profile == 0) { s.subsampling_x = s.subsampling_y = 1; }
            else if (s.profile == 1) { s.subsampling_x = s.subsampling_y = 0; }
            else if (s.bit_depth == 12) {
                s.subsampling_x = (int)f(&b, 1);
                s.subsampling_y = s.subsampling_x ? (int)f(&b, 1) : 0;
            } else { s.subsampling_x = 1; s.subsampling_y = 0; }
            if (s.subsampling_x && s.subsampling_y) s.chroma_sample_position = (int)f(&b, 2);
        }
        s.separate_uv_delta_q = (int)f(&b, 1);
    }
    s.film_grain_params_present = (int)f(&b, 1);
    /* trailing_bits: a 1 then zeros up to the end of the payload */
    int trailing_one = (int)f(&b, 1);
    int zeros_ok = 1;
    while (b.pos < n * 8) { if (f(&b, 1)) zeros_ok = 0; }
    s.ok = !b.overrun && trailing_one == 1 && zeros_ok;
    return s;
}

/* ---- frame header (the start of uncompressed_header, AV1 spec 5.9.2) ------------------------ */

typedef struct frame_info {
    int show_existing_frame, frame_type, show_frame;
    int q_parsed, base_q_idx, any_delta_q;   /* key frames only */
} frame_info;

static frame_info parse_frame_header(const seq_info* s, const uint8_t* p, size_t n)
{
    frame_info fi;
    memset(&fi, 0, sizeof fi);
    bits b = { p, n, 0, 0 };
    int error_resilient = 0;
    if (s->reduced_still_picture_header) {
        fi.frame_type = 0; fi.show_frame = 1;
    } else {
        fi.show_existing_frame = (int)f(&b, 1);
        if (fi.show_existing_frame) return fi;
        fi.frame_type = (int)f(&b, 2);
        fi.show_frame = (int)f(&b, 1);
        if (fi.show_frame && s->decoder_model_info_present && !s->equal_picture_interval)
            f(&b, s->frame_presentation_time_length);
        if (!fi.show_frame) f(&b, 1);   /* showable_frame */
        if (fi.frame_type == 3 || (fi.frame_type == 0 && fi.show_frame)) error_resilient = 1;
        else error_resilient = (int)f(&b, 1);
    }
    /* Only shown KEY frames are parsed further (to base_q_idx); that is all the checks need. */
    if (!(fi.frame_type == 0 && fi.show_frame) || s->decoder_model_info_present) return fi;
    (void)error_resilient;
    int disable_cdf_update = (int)f(&b, 1);
    int allow_screen_content_tools = s->seq_force_screen_content_tools == 2 ? (int)f(&b, 1) : s->seq_force_screen_content_tools;
    if (allow_screen_content_tools && s->seq_force_integer_mv == 2) f(&b, 1);
    if (s->frame_id_numbers_present) f(&b, s->id_len);
    int frame_size_override = s->reduced_still_picture_header ? 0 : (int)f(&b, 1);
    if (s->enable_order_hint) f(&b, s->order_hint_bits);
    /* primary_ref_frame = NONE for intra; refresh_frame_flags implied for a shown key frame */
    int frame_width = s->max_width, frame_height = s->max_height;
    if (frame_size_override) {
        frame_width = (int)f(&b, s->frame_width_bits) + 1;
        frame_height = (int)f(&b, s->frame_height_bits) + 1;
    }
    int upscaled_width = frame_width;
    if (s->enable_superres && f(&b, 1)) {
        int denom = (int)f(&b, 3) + 9;
        frame_width = (upscaled_width * 8 + denom / 2) / denom;
    }
    if (f(&b, 1)) { f(&b, 16); f(&b, 16); }   /* render_and_frame_size_different */
    if (allow_screen_content_tools && upscaled_width == frame_width) f(&b, 1);   /* allow_intrabc */
    if (!(s->reduced_still_picture_header || disable_cdf_update)) f(&b, 1);   /* disable_frame_end_update_cdf */
    /* tile_info */
    int mi_cols = 2 * ((frame_width + 7) >> 3), mi_rows = 2 * ((frame_height + 7) >> 3);
    int sb_cols = s->use_128x128 ? (mi_cols + 31) >> 5 : (mi_cols + 15) >> 4;
    int sb_rows = s->use_128x128 ? (mi_rows + 31) >> 5 : (mi_rows + 15) >> 4;
    int sb_shift = s->use_128x128 ? 5 : 4, sb_size = sb_shift + 2;
    int max_tile_width_sb = 4096 >> sb_size;
    int max_tile_area_sb = (4096 * 2304) >> (2 * sb_size);
    int min_log2_tile_cols = tile_log2(max_tile_width_sb, sb_cols);
    int max_log2_tile_cols = tile_log2(1, sb_cols < 64 ? sb_cols : 64);
    int max_log2_tile_rows = tile_log2(1, sb_rows < 64 ? sb_rows : 64);
    int min_log2_tiles = tile_log2(max_tile_area_sb, sb_rows * sb_cols);
    if (min_log2_tiles < min_log2_tile_cols) min_log2_tiles = min_log2_tile_cols;
    int tile_cols_log2, tile_rows_log2;
    if (f(&b, 1)) {   /* uniform_tile_spacing_flag */
        tile_cols_log2 = min_log2_tile_cols;
        while (tile_cols_log2 < max_log2_tile_cols && f(&b, 1)) tile_cols_log2++;
        int min_log2_tile_rows = min_log2_tiles - tile_cols_log2;
        if (min_log2_tile_rows < 0) min_log2_tile_rows = 0;
        tile_rows_log2 = min_log2_tile_rows;
        while (tile_rows_log2 < max_log2_tile_rows && f(&b, 1)) tile_rows_log2++;
    } else {
        int widest = 0, start = 0, cols = 0, rows = 0;
        while (start < sb_cols) {
            int max_width = sb_cols - start < max_tile_width_sb ? sb_cols - start : max_tile_width_sb;
            int size = (int)ns(&b, (uint32_t)max_width) + 1;
            if (size > widest) widest = size;
            start += size; cols++;
        }
        tile_cols_log2 = tile_log2(1, cols);
        int area = min_log2_tiles > 0 ? (sb_rows * sb_cols) >> (min_log2_tiles + 1) : sb_rows * sb_cols;
        int max_tile_height_sb = area / widest > 1 ? area / widest : 1;
        start = 0;
        while (start < sb_rows) {
            int max_height = sb_rows - start < max_tile_height_sb ? sb_rows - start : max_tile_height_sb;
            start += (int)ns(&b, (uint32_t)max_height) + 1; rows++;
        }
        tile_rows_log2 = tile_log2(1, rows);
    }
    if (tile_cols_log2 > 0 || tile_rows_log2 > 0) { f(&b, tile_rows_log2 + tile_cols_log2); f(&b, 2); }
    /* quantization_params */
    fi.base_q_idx = (int)f(&b, 8);
    int planes = s->mono_chrome ? 1 : 3;
    if (f(&b, 1)) { f(&b, 7); fi.any_delta_q = 1; }   /* DeltaQYDc */
    if (planes > 1) {
        int diff_uv = s->separate_uv_delta_q ? (int)f(&b, 1) : 0;
        if (f(&b, 1)) { f(&b, 7); fi.any_delta_q = 1; }   /* DeltaQUDc */
        if (f(&b, 1)) { f(&b, 7); fi.any_delta_q = 1; }   /* DeltaQUAc */
        if (diff_uv) {
            if (f(&b, 1)) { f(&b, 7); fi.any_delta_q = 1; }
            if (f(&b, 1)) { f(&b, 7); fi.any_delta_q = 1; }
        }
    }
    fi.q_parsed = !b.overrun;
    return fi;
}

/* ---- a temporal unit, OBU by OBU ------------------------------------------------------------ */

typedef struct tu_info {
    int well_formed;          /* every OBU size-delimited, sizes add up exactly */
    int starts_with_td;       /* first two bytes 0x12 0x00 */
    int shown_frames;         /* frames with show_frame == 1 or show_existing_frame == 1 */
    int shown_key;            /* the shown frame is a KEY_FRAME coded here */
    int seq_headers, seq_header_matches;
    int key_q_parsed, key_base_q_idx, key_any_delta_q;
    int obu_count;
} tu_info;

static tu_info inspect_tu(const seq_info* s, const uint8_t* tu, size_t n, const uint8_t* seq_obu, size_t seq_len)
{
    tu_info t;
    memset(&t, 0, sizeof t);
    t.starts_with_td = n >= 2 && tu[0] == 0x12 && tu[1] == 0x00;
    t.key_base_q_idx = -1;
    size_t pos = 0;
    t.well_formed = 1;
    while (pos < n) {
        uint8_t h = tu[pos];
        int forbidden = h >> 7, type = (h >> 3) & 15, ext = (h >> 2) & 1, has_size = (h >> 1) & 1;
        size_t hdr = 1 + (size_t)ext, used = 0;
        uint64_t payload = 0;
        if (forbidden || !has_size || pos + hdr > n || !read_leb128(tu + pos + hdr, n - pos - hdr, &payload, &used)
            || payload > n || pos + hdr + used + payload > n) { t.well_formed = 0; break; }
        const uint8_t* body = tu + pos + hdr + used;
        size_t total = hdr + used + (size_t)payload;
        t.obu_count++;
        if (type == 1) {
            t.seq_headers++;
            if (total == seq_len && memcmp(tu + pos, seq_obu, seq_len) == 0) t.seq_header_matches++;
        } else if (type == 3 || type == 6) {   /* OBU_FRAME_HEADER, OBU_FRAME */
            frame_info fi = parse_frame_header(s, body, (size_t)payload);
            if (fi.show_existing_frame || fi.show_frame) t.shown_frames++;
            if (!fi.show_existing_frame && fi.show_frame && fi.frame_type == 0) {
                t.shown_key = 1;
                t.key_q_parsed = fi.q_parsed;
                t.key_base_q_idx = fi.base_q_idx;
                t.key_any_delta_q = fi.any_delta_q;
            }
        }
        pos += total;
    }
    if (pos != n) t.well_formed = 0;
    return t;
}

/* ---- synthetic frames ----------------------------------------------------------------------- */

typedef struct frame {
    int w, h, cw, ch, bps;
    uint8_t *y, *u, *v;
    int32_t ys, us, vs;
} frame;

static void put_sample(uint8_t* row, int x, int bps, int value)
{
    if (bps == 1) row[x] = (uint8_t)value;
    else { uint16_t s = (uint16_t)value; memcpy(row + 2 * x, &s, 2); }
}

static void fill_frame(frame* fr, int index, int bit_depth, int full_range)
{
    const int max = (1 << bit_depth) - 1;
    const int lo = full_range ? 0 : 16 << (bit_depth - 8), hi = full_range ? max : 235 << (bit_depth - 8);
    const int bx = (index * 5) % (fr->w - 48), by = (index * 3) % (fr->h - 32);
    for (int r = 0; r < fr->h; r++) {
        uint8_t* row = fr->y + (size_t)r * (size_t)fr->ys;
        for (int x = 0; x < fr->w; x++) {
            int g = ((x + r + index * 4) * (hi - lo)) / (fr->w + fr->h);
            int value = lo + (g % (hi - lo + 1));
            if (x >= bx && x < bx + 48 && r >= by && r < by + 32) value = hi - (value - lo) / 3;
            put_sample(row, x, fr->bps, value);
        }
    }
    if (fr->u == NULL) return;
    const int mid = 1 << (bit_depth - 1);
    for (int r = 0; r < fr->ch; r++) {
        uint8_t* urow = fr->u + (size_t)r * (size_t)fr->us;
        uint8_t* vrow = fr->v + (size_t)r * (size_t)fr->vs;
        for (int x = 0; x < fr->cw; x++) {
            int du = ((x * 7 + index * 3) % 64) - 32, dv = ((r * 5 + index * 2) % 64) - 32;
            put_sample(urow, x, fr->bps, mid + (du << (bit_depth - 8)) / 2);
            put_sample(vrow, x, fr->bps, mid + (dv << (bit_depth - 8)) / 2);
        }
    }
}

static frame frame_alloc(int w, int h, int bit_depth, int layout, int v_stride_extra)
{
    frame fr;
    memset(&fr, 0, sizeof fr);
    fr.w = w; fr.h = h; fr.bps = bit_depth > 8 ? 2 : 1;
    fr.cw = layout == LAYOUT_I444 ? w : (w + 1) / 2;
    fr.ch = layout == LAYOUT_I444 ? h : (h + 1) / 2;
    fr.ys = w * fr.bps + 16;   /* padded strides, as real frame buffers have */
    fr.y = (uint8_t*)calloc((size_t)fr.ys * (size_t)h, 1);
    if (layout != LAYOUT_MONO) {
        fr.us = fr.cw * fr.bps + 8;
        fr.vs = fr.us + v_stride_extra;
        fr.u = (uint8_t*)calloc((size_t)fr.us * (size_t)fr.ch, 1);
        fr.v = (uint8_t*)calloc((size_t)fr.vs * (size_t)fr.ch, 1);
    }
    if (!fr.y || (layout != LAYOUT_MONO && (!fr.u || !fr.v))) { fprintf(stderr, "out of memory\n"); exit(9); }
    return fr;
}

static void frame_free(frame* fr) { free(fr->y); free(fr->u); free(fr->v); }

/* ---- encoding runs -------------------------------------------------------------------------- */

typedef struct packet { uint8_t* data; int32_t length; int64_t pts; int32_t key; } packet;
typedef struct run {
    packet* packets; int count, cap;
    uint8_t seq[1024]; int32_t seq_len;
    int max_pending_seen;
    int64_t frames_submitted, packets_produced;
    int start_rc;
    int seq_rc_before_first_packet;   /* sequence_header_length between start and the first packet */
} run;

static void run_free(run* r) { for (int i = 0; i < r->count; i++) free(r->packets[i].data); free(r->packets); memset(r, 0, sizeof *r); }

static void drain(enc* e, run* r)
{
    int32_t pending = p_pending_packets(e);
    if (pending > r->max_pending_seen) r->max_pending_seen = pending;
    for (;;) {
        int32_t length = 0, key = 0;
        int64_t pts = 0;
        if (p_packet_info(e, &length, &pts, &key) != CBA_OK) break;
        uint8_t* data = (uint8_t*)malloc(length > 0 ? (size_t)length : 1);
        int32_t got = p_read_packet(e, data, length);
        QUIET_CHECK(got == length, "read_packet returned %d for a %d-byte packet", got, length);
        if (r->count == r->cap) {
            r->cap = r->cap ? r->cap * 2 : 64;
            r->packets = (packet*)realloc(r->packets, (size_t)r->cap * sizeof(packet));
            if (!r->packets) { fprintf(stderr, "out of memory\n"); exit(9); }
        }
        r->packets[r->count].data = data;
        r->packets[r->count].length = length;
        r->packets[r->count].pts = pts;
        r->packets[r->count].key = key;
        r->count++;
    }
}

typedef struct config {
    const char* name;
    int layout, bit_depth, usage, frames, force_kf_at, full_range;
    int lossless, kf_max_dist, cp, tc, mc, chroma_pos, v_stride_extra, null_mono_chroma, lag;
    int expect_immediate;   /* REALTIME: one packet per encode call, at once */
} config;

static int profile_for(const config* c) { return c->bit_depth == 12 ? 2 : (c->layout == LAYOUT_I444 ? 1 : 0); }

static run encode_run(const config* c, int w, int h)
{
    run r;
    memset(&r, 0, sizeof r);
    enc* e = NULL;
    int32_t rc = p_create(w, h, c->bit_depth, c->layout, 1, 30, c->usage, &e);
    QUIET_CHECK(rc == CBA_OK && e != NULL, "%s: create", c->name);
    if (e == NULL) return r;
    if (c->usage != USAGE_ALL_INTRA) p_set_int32(e, OPT_CPU_USED, c->usage == USAGE_REALTIME ? 9 : 6);
    else p_set_int32(e, OPT_CPU_USED, 6);
    p_set_int32(e, OPT_THREADS, 2);
    if (c->full_range) p_set_int32(e, OPT_RANGE, 1);
    if (c->lossless) p_set_int32(e, OPT_LOSSLESS, 1);
    if (c->kf_max_dist >= 0) p_set_int32(e, OPT_KF_MAX, c->kf_max_dist);
    if (c->lag >= 0) p_set_int32(e, OPT_LAG, c->lag);
    if (c->cp >= 0) { p_set_int32(e, OPT_PRIMARIES, c->cp); p_set_int32(e, OPT_TRANSFER, c->tc); p_set_int32(e, OPT_MATRIX, c->mc); }
    if (c->chroma_pos >= 0) p_set_int32(e, OPT_CHROMA_POS, c->chroma_pos);
    r.start_rc = p_start(e);
    QUIET_CHECK(r.start_rc == CBA_OK, "%s: start (%s)", c->name, p_last_error(e));
    r.seq_rc_before_first_packet = p_sequence_header_length(e);
    r.seq_len = -1;
    frame fr = frame_alloc(w, h, c->bit_depth, c->layout, c->v_stride_extra);
    for (int i = 0; i < c->frames; i++) {
        fill_frame(&fr, i, c->bit_depth, c->full_range);
        const uint8_t* u = fr.u;
        const uint8_t* v = fr.v;
        if (c->layout == LAYOUT_MONO && c->null_mono_chroma) { u = NULL; v = NULL; }
        int before = r.count;
        rc = p_encode(e, fr.y, fr.ys, u, fr.us, v, fr.vs, (int64_t)i, i == c->force_kf_at ? 1 : 0);
        QUIET_CHECK(rc == CBA_OK, "%s: encode frame %d -> %d (%s)", c->name, i, rc, p_last_error(e));
        drain(e, &r);
        if (r.count == 0) {
            int32_t sl = p_sequence_header_length(e);
            QUIET_CHECK(sl == CBA_E_INVALID_STATE, "%s: no sequence header while no packet exists (got %d)", c->name, sl);
        } else if (r.seq_len < 0) {
            r.seq_len = p_sequence_header_length(e);
            if (r.seq_len > 0 && r.seq_len <= (int32_t)sizeof r.seq) {
                int32_t got = p_read_sequence_header(e, r.seq, (int32_t)sizeof r.seq);
                QUIET_CHECK(got == r.seq_len, "%s: read_sequence_header", c->name);
            }
        }
        if (c->expect_immediate) QUIET_CHECK(r.count == before + 1, "%s: frame %d gave %d packets at once (expected 1)", c->name, i, r.count - before);
    }
    rc = p_flush(e);
    QUIET_CHECK(rc == CBA_OK, "%s: flush (%s)", c->name, p_last_error(e));
    drain(e, &r);
    if (r.seq_len < 0 && r.count > 0) {
        r.seq_len = p_sequence_header_length(e);
        if (r.seq_len > 0 && r.seq_len <= (int32_t)sizeof r.seq) p_read_sequence_header(e, r.seq, (int32_t)sizeof r.seq);
    }
    {
        uint8_t again[1024];
        int32_t n = p_read_sequence_header(e, again, (int32_t)sizeof again);
        QUIET_CHECK(n == r.seq_len && n > 0 && memcmp(again, r.seq, (size_t)n) == 0, "%s: sequence header unchanged at the end", c->name);
    }
    r.frames_submitted = p_frames_submitted(e);
    r.packets_produced = p_packets_produced(e);
    p_destroy(e);
    frame_free(&fr);
    return r;
}

static void write_ivf(const char* prefix, const char* name, const run* r, int w, int h)
{
    if (prefix == NULL) return;
    char path[1024];
    snprintf(path, sizeof path, "%s-%s.ivf", prefix, name);
    FILE* fp = fopen(path, "wb");
    if (!fp) { fprintf(stderr, "warning: cannot write %s\n", path); return; }
    uint8_t hdr[32] = { 'D', 'K', 'I', 'F', 0, 0, 32, 0, 'A', 'V', '0', '1' };
    hdr[12] = (uint8_t)w; hdr[13] = (uint8_t)(w >> 8); hdr[14] = (uint8_t)h; hdr[15] = (uint8_t)(h >> 8);
    hdr[16] = 30; hdr[20] = 1;   /* rate 30, scale 1 */
    hdr[24] = (uint8_t)r->count; hdr[25] = (uint8_t)(r->count >> 8);
    fwrite(hdr, 1, 32, fp);
    for (int i = 0; i < r->count; i++) {
        uint8_t fh[12];
        uint32_t n = (uint32_t)r->packets[i].length;
        uint64_t pts = (uint64_t)r->packets[i].pts;
        for (int k = 0; k < 4; k++) fh[k] = (uint8_t)(n >> (8 * k));
        for (int k = 0; k < 8; k++) fh[4 + k] = (uint8_t)(pts >> (8 * k));
        fwrite(fh, 1, 12, fp);
        fwrite(r->packets[i].data, 1, n, fp);
    }
    fclose(fp);
    printf("      wrote %s\n", path);
}

/* The full set of stream checks for one configuration. */
static void check_stream(const config* c, const run* r, int w, int h, int* key_count_out, int* lossless_ok_out, int* lossy_q_out)
{
    const char* nm = c->name;
    CHECK(r->start_rc == CBA_OK, "%s: started", nm);
    CHECK(r->seq_rc_before_first_packet == CBA_E_INVALID_STATE, "%s: sequence header CBA_E_INVALID_STATE between start and the first packet", nm);
    CHECK(r->seq_len > 3 && r->seq_len <= 1024, "%s: sequence header readable once the first packet exists (%d bytes)", nm, r->seq_len);
    seq_info s;
    memset(&s, 0, sizeof s);
    if (r->seq_len > 3) {
        uint64_t payload = 0;
        size_t used = 0;
        int type = (r->seq[0] >> 3) & 15, has_size = (r->seq[0] >> 1) & 1, ext = (r->seq[0] >> 2) & 1;
        int lenok = has_size && read_leb128(r->seq + 1 + ext, (size_t)r->seq_len - 1 - (size_t)ext, &payload, &used)
                    && 1 + (size_t)ext + used + payload == (size_t)r->seq_len;
        CHECK(type == 1 && lenok, "%s: sequence header is one OBU_SEQUENCE_HEADER (type %d, size field %s)", nm, type, lenok ? "exact" : "WRONG");
        if (lenok) s = parse_sequence_header(r->seq + 1 + ext + used, (size_t)payload);
    }
    CHECK(s.ok, "%s: sequence header parses to its trailing bits", nm);
    CHECK(s.profile == profile_for(c) && s.still_picture == 0 && s.reduced_still_picture_header == 0,
          "%s: seq_profile %d (expected %d), not a still picture", nm, s.profile, profile_for(c));
    CHECK(s.bit_depth == c->bit_depth, "%s: bit depth %d", nm, s.bit_depth);
    CHECK(s.mono_chrome == (c->layout == LAYOUT_MONO), "%s: mono_chrome %d", nm, s.mono_chrome);
    {
        int want_ss = c->layout == LAYOUT_I444 ? 0 : 1;
        CHECK(s.subsampling_x == want_ss && s.subsampling_y == want_ss, "%s: subsampling %d,%d", nm, s.subsampling_x, s.subsampling_y);
    }
    CHECK(s.color_range == (c->full_range ? 1 : 0), "%s: color_range bit %d (asked %s)", nm, s.color_range, c->full_range ? "full" : "limited");
    if (c->cp >= 0)
        CHECK(s.color_description_present && s.cp == c->cp && s.tc == c->tc && s.mc == c->mc,
              "%s: colour description %d/%d/%d", nm, s.cp, s.tc, s.mc);
    else
        CHECK(!s.color_description_present || (s.cp == 2 && s.tc == 2 && s.mc == 2),
              "%s: colour description unspecified (present=%d %d/%d/%d)", nm, s.color_description_present, s.cp, s.tc, s.mc);
    if (c->chroma_pos >= 0) CHECK(s.chroma_sample_position == c->chroma_pos, "%s: chroma_sample_position %d", nm, s.chroma_sample_position);
    CHECK(s.max_width == w && s.max_height == h, "%s: max frame size %dx%d", nm, s.max_width, s.max_height);
    CHECK(s.film_grain_params_present == 0 && s.timing_info_present == 0, "%s: no film grain, no timing info", nm);

    CHECK(r->count == c->frames, "%s: %d packets for %d frames after flush", nm, r->count, c->frames);
    CHECK(r->frames_submitted == c->frames && r->packets_produced == c->frames,
          "%s: frames_submitted %lld, packets_produced %lld", nm, (long long)r->frames_submitted, (long long)r->packets_produced);

    int pts_ok = 1, td_ok = 1, shape_ok = 1, one_shown = 1, key_flag_ok = 1, seq_in_key_ok = 1, keys = 0;
    int lossless_ok = 1, lossy_q = -1;
    for (int i = 0; i < r->count; i++) {
        const packet* p = &r->packets[i];
        if (p->pts != i) pts_ok = 0;
        tu_info t = inspect_tu(&s, p->data, (size_t)p->length, r->seq, (size_t)r->seq_len);
        if (!t.starts_with_td) td_ok = 0;
        if (!t.well_formed) shape_ok = 0;
        if (t.shown_frames != 1) one_shown = 0;
        if (p->key != t.shown_key) {
            key_flag_ok = 0;
            fprintf(stderr, "      %s: packet %d is_key_frame=%d but its shown frame is %sa key frame\n", nm, i, p->key, t.shown_key ? "" : "NOT ");
        }
        if (p->key) {
            keys++;
            if (t.seq_headers < 1 || t.seq_header_matches != t.seq_headers) seq_in_key_ok = 0;
            if (c->lossless && !(t.key_q_parsed && t.key_base_q_idx == 0 && !t.key_any_delta_q)) lossless_ok = 0;
            if (!c->lossless && t.key_q_parsed && lossy_q < 0) lossy_q = t.key_base_q_idx;
        }
    }
    CHECK(pts_ok, "%s: pts strictly increasing and == frame index on every packet", nm);
    CHECK(td_ok, "%s: every packet begins with a temporal delimiter OBU (12 00)", nm);
    CHECK(shape_ok, "%s: every packet is a whole number of size-delimited OBUs", nm);
    CHECK(one_shown, "%s: every packet holds exactly one shown frame", nm);
    CHECK(key_flag_ok, "%s: is_key_frame == (shown frame is a KEY_FRAME) on every packet", nm);
    CHECK(r->count > 0 && r->packets[0].key, "%s: packet 0 is a key frame", nm);
    if (c->force_kf_at >= 0 && c->force_kf_at < r->count)
        CHECK(r->packets[c->force_kf_at].key, "%s: forced key frame honoured at %d", nm, c->force_kf_at);
    CHECK(seq_in_key_ok, "%s: every key frame carries the sequence header, byte-identical to read_sequence_header", nm);
    printf("      %s: key frames at", nm);
    for (int i = 0; i < r->count; i++) if (r->packets[i].key) printf(" %d", i);
    printf("\n");
    long long total = 0;
    for (int i = 0; i < r->count; i++) total += r->packets[i].length;
    printf("      %s: %d key frames, %lld bytes in %d packets, largest pending queue %d\n", nm, keys, total, r->count, r->max_pending_seen);
    if (key_count_out) *key_count_out = keys;
    if (lossless_ok_out) *lossless_ok_out = lossless_ok;
    if (lossy_q_out) *lossy_q_out = lossy_q;
}

/* ---- lock-step: colour + alpha with automatic key frames OFF ---------------------------------- */

#define OPT_AUTOMATIC_KEY_FRAMES 17

/* Encodes `frames` frames; from frame `cut` on the picture changes completely (a hard scene cut,
 * which libaom would key on by itself); with `force`, forced key frames at 12 and 30. Returns the key positions
 * as a bit mask. */
static uint64_t lockstep_keys(int layout, int bit_depth, int automatic, int cut, int frames, int force, int* packets_out)
{
    enc* e = NULL;
    run r;
    memset(&r, 0, sizeof r);
    if (p_create(192, 108, bit_depth, layout, 1, 30, USAGE_GOOD, &e) != CBA_OK) return 0;
    p_set_int32(e, OPT_CPU_USED, 6);
    p_set_int32(e, OPT_AUTOMATIC_KEY_FRAMES, automatic);
    if (layout == LAYOUT_MONO) p_set_int32(e, OPT_RANGE, 1);
    if (p_start(e) != CBA_OK) { p_destroy(e); return 0; }
    frame fr = frame_alloc(192, 108, bit_depth, layout, 0);
    for (int i = 0; i < frames; i++) {
        fill_frame(&fr, i, bit_depth, layout == LAYOUT_MONO);
        /* luma: a flat dark picture until the cut, then a hard checkerboard */
        for (int y = 0; y < fr.h; y++)
            for (int x = 0; x < fr.w; x++)
                put_sample(fr.y + (size_t)y * (size_t)fr.ys, x, fr.bps,
                           i < cut ? (16 << (bit_depth - 8)) : ((((x >> 3) + (y >> 3)) & 1) ? (200 << (bit_depth - 8)) : (40 << (bit_depth - 8))));
        p_encode(e, fr.y, fr.ys, fr.u, fr.us, fr.v, fr.vs, i, (force && (i == 12 || i == 30)) ? 1 : 0);
        drain(e, &r);
    }
    p_flush(e);
    drain(e, &r);
    uint64_t mask = 0;
    for (int i = 0; i < r.count && i < 64; i++) if (r.packets[i].key) mask |= (uint64_t)1 << i;
    if (packets_out) *packets_out = r.count;
    run_free(&r);
    frame_free(&fr);
    p_destroy(e);
    return mask;
}

static void print_mask(const char* what, uint64_t m)
{
    printf("      %s: key frames at", what);
    for (int i = 0; i < 64; i++) if (m & ((uint64_t)1 << i)) printf(" %d", i);
    printf("\n");
}

static void lockstep(void)
{
    int n1 = 0, n2 = 0, n3 = 0;
    uint64_t colour = lockstep_keys(LAYOUT_I420, 10, 0, 20, 40, 1, &n1);   /* colour stream: has a scene cut at 20 */
    uint64_t alpha = lockstep_keys(LAYOUT_MONO, 10, 0, 1000, 40, 1, &n2);   /* alpha stream: no cut */
    uint64_t automatic = lockstep_keys(LAYOUT_I420, 10, 1, 20, 40, 0, &n3);   /* control: nothing forced */
    int n4 = 0;
    uint64_t off_unforced = lockstep_keys(LAYOUT_I420, 10, 0, 20, 40, 0, &n4);
    print_mask("colour, automatic key frames off", colour);
    print_mask("alpha,  automatic key frames off", alpha);
    print_mask("control, nothing forced, automatic on ", automatic);
    print_mask("control, nothing forced, automatic off", off_unforced);
    CHECK(n1 == 40 && n2 == 40 && n3 == 40, "lock-step: 40 packets from each encoder");
    CHECK(colour == alpha, "lock-step: colour (with a scene cut) and alpha (without) have IDENTICAL key-frame positions with automatic key frames off");
    CHECK((colour & 1) && (colour & ((uint64_t)1 << 12)) && (colour & ((uint64_t)1 << 30)), "lock-step: frame 0 and both forced frames (12, 30) are key frames");
    CHECK(!(colour & ((uint64_t)1 << 20)), "lock-step: no key frame at the scene cut (20) with automatic key frames off");
    CHECK(automatic & ((uint64_t)1 << 20), "control: with automatic key frames ON (nothing forced) libaom keys the scene cut at 20");
    CHECK(n4 == 40 && off_unforced == 1, "control: automatic key frames OFF, nothing forced: frame 0 is the only key frame");
}

/* ---- error paths ---------------------------------------------------------------------------- */

static void error_paths(void)
{
    enc* e = (enc*)1;
    CHECK(p_create(0, 180, 8, LAYOUT_I420, 1, 30, USAGE_GOOD, &e) == CBA_E_INVALID_ARGUMENT && e == NULL, "create: width 0 refused, *out = NULL");
    CHECK(p_create(65537, 180, 8, LAYOUT_I420, 1, 30, USAGE_GOOD, &e) == CBA_E_INVALID_ARGUMENT, "create: width 65537 refused");
    CHECK(p_create(320, 0, 8, LAYOUT_I420, 1, 30, USAGE_GOOD, &e) == CBA_E_INVALID_ARGUMENT, "create: height 0 refused");
    CHECK(p_create(320, 180, 9, LAYOUT_I420, 1, 30, USAGE_GOOD, &e) == CBA_E_INVALID_ARGUMENT, "create: bit depth 9 refused");
    CHECK(p_create(320, 180, 8, 3, 1, 30, USAGE_GOOD, &e) == CBA_E_INVALID_ARGUMENT, "create: layout 3 refused");
    CHECK(p_create(320, 180, 8, LAYOUT_I420, 0, 30, USAGE_GOOD, &e) == CBA_E_INVALID_ARGUMENT, "create: timebase_num 0 refused");
    CHECK(p_create(320, 180, 8, LAYOUT_I420, 1, -30, USAGE_GOOD, &e) == CBA_E_INVALID_ARGUMENT, "create: negative timebase_den refused");
    CHECK(p_create(320, 180, 8, LAYOUT_I420, 1, 30, 3, &e) == CBA_E_INVALID_ARGUMENT, "create: usage 3 refused");
    CHECK(p_create(320, 180, 8, LAYOUT_I420, 1, 30, USAGE_GOOD, NULL) == CBA_E_INVALID_ARGUMENT, "create: null out_encoder refused");
    CHECK(p_create(161, 90, 8, LAYOUT_I420, 1, 30, USAGE_GOOD, &e) == CBA_E_INVALID_ARGUMENT
          && p_create(160, 91, 10, LAYOUT_I420, 1, 30, USAGE_GOOD, &e) == CBA_E_INVALID_ARGUMENT, "create: odd width / height refused for I420");
    CHECK(p_create(161, 91, 8, LAYOUT_I444, 1, 30, USAGE_GOOD, &e) == CBA_OK && e != NULL, "create: odd size accepted for I444");
    p_destroy(e); e = NULL;
    CHECK(p_create(161, 91, 10, LAYOUT_MONO, 1, 30, USAGE_GOOD, &e) == CBA_OK && e != NULL, "create: odd size accepted for MONOCHROME");
    p_destroy(e); e = NULL;

    CHECK(p_set_int32(NULL, OPT_CPU_USED, 5) == CBA_E_INVALID_ARGUMENT, "set_int32: null handle");
    CHECK(p_set_string_option(NULL, "tune", "psnr") == CBA_E_INVALID_ARGUMENT, "set_string_option: null handle");
    CHECK(p_start(NULL) == CBA_E_INVALID_ARGUMENT, "start: null handle");
    CHECK(p_encode(NULL, (const uint8_t*)"", 1, NULL, 0, NULL, 0, 0, 0) == CBA_E_INVALID_ARGUMENT, "encode: null handle");
    CHECK(p_flush(NULL) == CBA_E_INVALID_ARGUMENT, "flush: null handle");
    CHECK(p_pending_packets(NULL) < 0, "pending_packets: null handle negative");
    CHECK(p_frames_submitted(NULL) < 0 && p_packets_produced(NULL) < 0, "frames_submitted / packets_produced: null handle negative");
    CHECK(p_sequence_header_length(NULL) == CBA_E_INVALID_ARGUMENT, "sequence_header_length: null handle");
    CHECK(p_last_error(NULL) != NULL, "last_error: null handle gives a string");
    p_destroy(NULL);
    CHECK(1, "destroy(NULL) ignored");

    /* before start */
    e = NULL;
    CHECK(p_create(64, 48, 8, LAYOUT_I420, 1, 30, USAGE_GOOD, &e) == CBA_OK && e != NULL, "create 64x48 I420 8-bit");
    CHECK(p_set_int32(e, 0, 1) == CBA_E_INVALID_ARGUMENT && p_set_int32(e, 18, 1) == CBA_E_INVALID_ARGUMENT, "set_int32: unknown options 0 and 18 refused");
    CHECK(p_last_error(e)[0] != '\0', "last_error explains a refused option (\"%s\")", p_last_error(e));
    CHECK(p_set_int32(e, OPT_CPU_USED, 10) == CBA_E_INVALID_ARGUMENT && p_set_int32(e, OPT_QUALITY, 64) == CBA_E_INVALID_ARGUMENT
          && p_set_int32(e, OPT_THREADS, 0) == CBA_E_INVALID_ARGUMENT && p_set_int32(e, OPT_LAG, 49) == CBA_E_INVALID_ARGUMENT
          && p_set_int32(e, OPT_KF_MAX, 65536) == CBA_E_INVALID_ARGUMENT && p_set_int32(e, OPT_RANGE, 2) == CBA_E_INVALID_ARGUMENT
          && p_set_int32(e, OPT_TARGET_BITRATE, 0) == CBA_E_INVALID_ARGUMENT && p_set_int32(e, OPT_CHROMA_POS, 3) == CBA_E_INVALID_ARGUMENT,
          "set_int32: out-of-range values refused");
    CHECK(p_set_int32(e, OPT_CPU_USED, 9) == CBA_OK && p_last_error(e)[0] == '\0', "set_int32 ok clears last_error");
    CHECK(p_set_string_option(e, NULL, "x") == CBA_E_INVALID_ARGUMENT && p_set_string_option(e, "", "x") == CBA_E_INVALID_ARGUMENT
          && p_set_string_option(e, "tune", "\x01") == CBA_E_INVALID_ARGUMENT, "set_string_option: null / empty name, non-printable value refused");
    CHECK(p_sequence_header_length(e) == CBA_E_INVALID_STATE, "sequence header before start: CBA_E_INVALID_STATE");
    {
        uint8_t tmp[64];
        CHECK(p_read_sequence_header(e, tmp, 64) == CBA_E_INVALID_STATE, "read_sequence_header before start: CBA_E_INVALID_STATE");
    }
    uint8_t y[64 * 48], u[32 * 24], v[32 * 24];
    memset(y, 100, sizeof y); memset(u, 128, sizeof u); memset(v, 128, sizeof v);
    CHECK(p_encode(e, y, 64, u, 32, v, 32, 0, 0) == CBA_E_INVALID_STATE, "encode before start: CBA_E_INVALID_STATE");
    CHECK(p_flush(e) == CBA_E_INVALID_STATE, "flush before start: CBA_E_INVALID_STATE");
    CHECK(p_set_string_option(e, "tune", "psnr") == CBA_OK, "set_string_option tune=psnr recorded");
    CHECK(p_start(e) == CBA_OK, "start with tune=psnr accepted by libaom (%s)", p_last_error(e));

    /* after start */
    CHECK(p_set_int32(e, OPT_CPU_USED, 5) == CBA_E_INVALID_STATE, "set_int32 after start: CBA_E_INVALID_STATE");
    CHECK(p_set_string_option(e, "sharpness", "1") == CBA_E_INVALID_STATE, "set_string_option after start: CBA_E_INVALID_STATE");
    CHECK(p_start(e) == CBA_E_INVALID_STATE, "start twice: CBA_E_INVALID_STATE");
    {
        int32_t len = 0, key = 0; int64_t pts = 0; uint8_t tmp[16];
        CHECK(p_packet_info(e, &len, &pts, &key) == CBA_E_INVALID_STATE, "packet_info with nothing pending: CBA_E_INVALID_STATE");
        CHECK(p_read_packet(e, tmp, 16) == CBA_E_INVALID_STATE, "read_packet with nothing pending: CBA_E_INVALID_STATE");
        CHECK(p_sequence_header_length(e) == CBA_E_INVALID_STATE && p_read_sequence_header(e, tmp, 16) == CBA_E_INVALID_STATE,
              "sequence header after start but before any packet: CBA_E_INVALID_STATE");
    }
    CHECK(p_encode(e, NULL, 64, u, 32, v, 32, 0, 0) == CBA_E_INVALID_ARGUMENT, "encode null luma: CBA_E_INVALID_ARGUMENT");
    CHECK(p_encode(e, y, 63, u, 32, v, 32, 0, 0) == CBA_E_INVALID_ARGUMENT, "encode luma stride < width: CBA_E_INVALID_ARGUMENT");
    CHECK(p_encode(e, y, 64, NULL, 32, v, 32, 0, 0) == CBA_E_INVALID_ARGUMENT, "encode null chroma (I420): CBA_E_INVALID_ARGUMENT");
    CHECK(p_encode(e, y, 64, u, 32, v, 32, 0, 2) == CBA_E_INVALID_ARGUMENT, "encode force_key_frame 2: CBA_E_INVALID_ARGUMENT");
    CHECK(p_encode(e, y, 64, u, 32, v, 32, 10, 0) == CBA_OK, "encode pts 10");
    CHECK(p_encode(e, y, 64, u, 32, v, 32, 10, 0) == CBA_E_INVALID_ARGUMENT, "encode pts 10 again: CBA_E_INVALID_ARGUMENT");
    CHECK(p_encode(e, y, 64, u, 32, v, 32, 9, 0) == CBA_E_INVALID_ARGUMENT, "encode pts 9 after 10: CBA_E_INVALID_ARGUMENT");
    CHECK(p_last_error(e)[0] != '\0', "last_error explains the pts refusal (\"%s\")", p_last_error(e));
    CHECK(p_encode(e, y, 64, u, 32, v, 32, 11, 0) == CBA_OK, "encode pts 11");
    CHECK(p_flush(e) == CBA_OK, "flush");
    CHECK(p_flush(e) == CBA_OK, "flush twice: CBA_OK (no-op)");
    CHECK(p_encode(e, y, 64, u, 32, v, 32, 12, 0) == CBA_E_INVALID_STATE, "encode after flush: CBA_E_INVALID_STATE");
    CHECK(p_pending_packets(e) == 2, "2 packets pending after flush");
    {
        uint8_t tmp[1024];
        int32_t sl = p_sequence_header_length(e);
        CHECK(sl > 0 && p_read_sequence_header(e, tmp, sl - 1) == CBA_E_BUFFER_TOO_SMALL, "read_sequence_header short buffer: CBA_E_BUFFER_TOO_SMALL");
        CHECK(p_sequence_header_length(e) == sl, "sequence header not consumed (still %d bytes)", sl);
        CHECK(p_read_sequence_header(e, NULL, 64) == CBA_E_INVALID_ARGUMENT, "read_sequence_header null destination: CBA_E_INVALID_ARGUMENT");
    }
    {
        int32_t len = 0, key = 0; int64_t pts = -1;
        CHECK(p_packet_info(e, &len, &pts, &key) == CBA_OK && pts == 10 && key == 1 && len > 2, "packet_info: pts 10, key frame, %d bytes", len);
        uint8_t* buf = (uint8_t*)malloc((size_t)len);
        CHECK(p_read_packet(e, buf, len - 1) == CBA_E_BUFFER_TOO_SMALL, "read_packet short buffer: CBA_E_BUFFER_TOO_SMALL");
        CHECK(p_pending_packets(e) == 2, "the packet stays queued after a short read");
        CHECK(p_read_packet(e, buf, len) == len, "read_packet with the exact capacity");
        CHECK(p_packet_info(e, &len, &pts, &key) == CBA_OK && pts == 11, "next packet pts 11");
        free(buf);
    }
    CHECK(p_packet_info(e, NULL, NULL, NULL) == CBA_E_INVALID_ARGUMENT, "packet_info null outputs: CBA_E_INVALID_ARGUMENT");
    p_destroy(e);
    CHECK(1, "destroy with a packet still queued");

    /* start failures */
    e = NULL;
    p_create(64, 48, 8, LAYOUT_I420, 1, 30, USAGE_GOOD, &e);
    p_set_int32(e, OPT_RATE_MODE, 2);
    CHECK(p_start(e) == CBA_E_UNSUPPORTED && p_last_error(e)[0] != '\0', "VBR without a bitrate: CBA_E_UNSUPPORTED (\"%s\")", p_last_error(e));
    p_set_int32(e, OPT_TARGET_BITRATE, 300);
    CHECK(p_start(e) == CBA_OK, "VBR 300 kbps then starts (the failed start left the encoder usable)");
    p_destroy(e);

    e = NULL;
    p_create(64, 48, 8, LAYOUT_I420, 1, 30, USAGE_GOOD, &e);
    p_set_string_option(e, "no-such-libaom-option", "1");
    CHECK(p_start(e) == CBA_E_UNSUPPORTED, "unknown libaom option: start CBA_E_UNSUPPORTED");
    CHECK(strstr(p_last_error(e), "no-such-libaom-option") != NULL, "last_error names it (\"%s\")", p_last_error(e));
    CHECK(p_sequence_header_length(e) == CBA_E_INVALID_STATE, "no sequence header after a failed start");
    p_destroy(e);

    e = NULL;
    p_create(64, 48, 8, LAYOUT_I420, 1, 30, USAGE_GOOD, &e);
    p_set_string_option(e, "tune", "not-a-tuning");
    CHECK(p_start(e) == CBA_E_UNSUPPORTED && p_last_error(e)[0] != '\0', "bad value for a known option: CBA_E_UNSUPPORTED (\"%s\")", p_last_error(e));
    p_destroy(e);

    e = NULL;
    p_create(64, 48, 10, LAYOUT_MONO, 1, 30, USAGE_GOOD, &e);
    p_set_int32(e, OPT_MATRIX, 0);
    CHECK(p_start(e) == CBA_E_UNSUPPORTED && p_last_error(e)[0] != '\0', "MONOCHROME + identity matrix: CBA_E_UNSUPPORTED at start (\"%s\")", p_last_error(e));
    p_destroy(e);

    /* flush without frames */
    e = NULL;
    p_create(64, 48, 10, LAYOUT_MONO, 1, 30, USAGE_GOOD, &e);
    CHECK(p_start(e) == CBA_OK && p_flush(e) == CBA_OK && p_pending_packets(e) == 0 && p_packets_produced(e) == 0,
          "start + flush with no frame: valid, nothing queued");
    CHECK(p_sequence_header_length(e) == CBA_E_INVALID_STATE, "no frame ever coded: no sequence header (CBA_E_INVALID_STATE)");
    p_destroy(e);
}

/* ---- main ----------------------------------------------------------------------------------- */

#define RESOLVE(var, type, name) do { var = (type)lib_sym(lib, name); if (!var) { fprintf(stderr, "FAIL: missing export %s\n", name); return 3; } } while (0)

int main(int argc, char** argv)
{
    if (argc < 2) { fprintf(stderr, "usage: %s <path-to-codebrix_av1-library> [ivf-output-prefix]\n", argv[0]); return 2; }
    const char* prefix = argc > 2 ? argv[2] : NULL;
    lib_handle lib = lib_open(argv[1]);
    if (!lib) {
#if defined(_WIN32)
        fprintf(stderr, "FAIL: cannot load %s (error %lu)\n", argv[1], (unsigned long)GetLastError());
#else
        fprintf(stderr, "FAIL: cannot load %s: %s\n", argv[1], dlerror());
#endif
        return 3;
    }
    RESOLVE(p_api_version, fn_api_version, "codebrix_av1_api_version");
    RESOLVE(p_library_version, fn_library_version, "codebrix_av1_library_version");
    RESOLVE(p_create, fn_create, "codebrix_av1_encoder_create");
    RESOLVE(p_set_int32, fn_set_int32, "codebrix_av1_encoder_set_int32");
    RESOLVE(p_set_string_option, fn_set_string, "codebrix_av1_encoder_set_string_option");
    RESOLVE(p_start, fn_op, "codebrix_av1_encoder_start");
    RESOLVE(p_encode, fn_encode, "codebrix_av1_encoder_encode");
    RESOLVE(p_flush, fn_op, "codebrix_av1_encoder_flush");
    RESOLVE(p_pending_packets, fn_op, "codebrix_av1_encoder_pending_packets");
    RESOLVE(p_packet_info, fn_packet_info, "codebrix_av1_encoder_packet_info");
    RESOLVE(p_read_packet, fn_read, "codebrix_av1_encoder_read_packet");
    RESOLVE(p_sequence_header_length, fn_op, "codebrix_av1_encoder_sequence_header_length");
    RESOLVE(p_read_sequence_header, fn_read, "codebrix_av1_encoder_read_sequence_header");
    RESOLVE(p_frames_submitted, fn_i64, "codebrix_av1_encoder_frames_submitted");
    RESOLVE(p_packets_produced, fn_i64, "codebrix_av1_encoder_packets_produced");
    RESOLVE(p_last_error, fn_last_error, "codebrix_av1_encoder_last_error");
    RESOLVE(p_destroy, fn_destroy, "codebrix_av1_encoder_destroy");
    printf("ok:   all 17 entry points resolved\n");

    CHECK(p_api_version() == 1, "api_version == 1");
    const char* version = p_library_version();
    CHECK(version != NULL && strstr(version, "3.15.2") != NULL, "library_version \"%s\" (libaom 3.15.2)", version ? version : "(null)");

    const int W = 320, H = 180;
    /*             name               layout       depth usage            frames kf  full lossless kfmax cp tc mc  csp vstr nullmono lag immediate */
    config cfgs[] = {
        { "i420-8bit",          LAYOUT_I420, 8,  USAGE_GOOD,      48, 24, 0, 0, -1, -1, -1, -1,  1, 0, 0, -1, 0 },
        { "i420-10bit",         LAYOUT_I420, 10, USAGE_GOOD,      48, 24, 0, 0, -1,  1,  1,  1, -1, 0, 0, -1, 0 },
        { "i444-8bit",          LAYOUT_I444, 8,  USAGE_GOOD,      48, 24, 0, 0, -1, -1, -1, -1, -1, 0, 0, -1, 0 },
        { "mono-8bit-full",     LAYOUT_MONO, 8,  USAGE_GOOD,      48, 24, 1, 0, -1, -1, -1, -1, -1, 0, 1, -1, 0 },
        { "mono-10bit-full",    LAYOUT_MONO, 10, USAGE_GOOD,      48, 24, 1, 0, -1,  1,  1,  1, -1, 0, 0, -1, 0 },
        { "i420-12bit",         LAYOUT_I420, 12, USAGE_GOOD,      24, 12, 0, 0, -1, -1, -1, -1,  2, 0, 0, -1, 0 },
        { "i444-12bit",         LAYOUT_I444, 12, USAGE_GOOD,      24, 12, 1, 0, -1,  1, 13,  0, -1, 0, 0, -1, 0 },
        { "i444-10bit-full",    LAYOUT_I444, 10, USAGE_GOOD,      24, 12, 1, 0, -1, -1, -1, -1, -1, 0, 0, -1, 0 },
    };
    int lossy_q_i420 = -1;
    for (size_t i = 0; i < sizeof cfgs / sizeof cfgs[0]; i++) {
        printf("--- %s: %d frames %dx%d, forced key frame at %d ---\n", cfgs[i].name, cfgs[i].frames, W, H, cfgs[i].force_kf_at);
        run r = encode_run(&cfgs[i], W, H);
        int lq = -1;
        check_stream(&cfgs[i], &r, W, H, NULL, NULL, &lq);
        if (i == 0) lossy_q_i420 = lq;
        write_ivf(prefix, cfgs[i].name, &r, W, H);
        run_free(&r);
    }
    CHECK(lossy_q_i420 > 0, "lossy default (cq-level 30): key frame base_q_idx %d > 0", lossy_q_i420);

    printf("--- lossless ---\n");
    {
        config c8 = { "i420-8bit-lossless", LAYOUT_I420, 8, USAGE_GOOD, 12, 6, 0, 1, -1, -1, -1, -1, -1, 0, 0, -1, 0 };
        run r = encode_run(&c8, W, H);
        int ok = 0;
        check_stream(&c8, &r, W, H, NULL, &ok, NULL);
        CHECK(ok, "%s: every key frame has base_q_idx 0 and no delta q (AV1 lossless)", c8.name);
        write_ivf(prefix, c8.name, &r, W, H);
        run_free(&r);
        config m10 = { "mono-10bit-lossless", LAYOUT_MONO, 10, USAGE_GOOD, 12, 6, 1, 1, -1, -1, -1, -1, -1, 0, 1, -1, 0 };
        r = encode_run(&m10, W, H);
        check_stream(&m10, &r, W, H, NULL, &ok, NULL);
        CHECK(ok, "%s: every key frame has base_q_idx 0 and no delta q (AV1 lossless)", m10.name);
        write_ivf(prefix, m10.name, &r, W, H);
        run_free(&r);
    }

    printf("--- key-frame control and usages ---\n");
    {
        int keys = 0;
        config kf0 = { "kf-max-dist-0", LAYOUT_I420, 8, USAGE_GOOD, 10, -1, 0, 0, 0, -1, -1, -1, -1, 0, 0, -1, 0 };
        run r = encode_run(&kf0, W, H);
        check_stream(&kf0, &r, W, H, &keys, NULL, NULL);
        CHECK(keys == 10, "kf-max-dist 0: every packet a key frame (%d of 10)", keys);
        run_free(&r);
        config ai = { "all-intra", LAYOUT_I420, 8, USAGE_ALL_INTRA, 10, -1, 0, 0, -1, -1, -1, -1, -1, 0, 0, -1, 0 };
        r = encode_run(&ai, W, H);
        check_stream(&ai, &r, W, H, &keys, NULL, NULL);
        CHECK(keys == 10, "ALL_INTRA: every packet a key frame (%d of 10)", keys);
        run_free(&r);
        config rt = { "realtime", LAYOUT_I420, 8, USAGE_REALTIME, 30, 15, 0, 0, -1, -1, -1, -1, -1, 0, 0, -1, 1 };
        r = encode_run(&rt, W, H);
        check_stream(&rt, &r, W, H, NULL, NULL, NULL);
        CHECK(r.max_pending_seen <= 1, "REALTIME: never more than one packet waiting (%d)", r.max_pending_seen);
        run_free(&r);
        config lag0 = { "good-lag-0", LAYOUT_MONO, 8, USAGE_GOOD, 12, 6, 1, 0, -1, -1, -1, -1, -1, 0, 0, 0, 1 };
        r = encode_run(&lag0, W, H);
        check_stream(&lag0, &r, W, H, NULL, NULL, NULL);
        run_free(&r);
    }

    printf("--- plane-stride handling ---\n");
    {
        config same = { "uv-equal-strides", LAYOUT_I420, 8, USAGE_GOOD, 8, 4, 0, 0, -1, -1, -1, -1, -1, 0, 0, -1, 0 };
        config diff = same; diff.name = "uv-unequal-strides"; diff.v_stride_extra = 40;
        run a = encode_run(&same, W, H), b = encode_run(&diff, W, H);
        int identical = a.count == b.count && a.count == 8;
        for (int i = 0; identical && i < a.count; i++)
            identical = a.packets[i].length == b.packets[i].length && memcmp(a.packets[i].data, b.packets[i].data, (size_t)a.packets[i].length) == 0;
        CHECK(identical, "unequal U/V strides (V copied by the shim) give byte-identical packets");
        run_free(&a); run_free(&b);
        config odd = { "i444-odd-size", LAYOUT_I444, 8, USAGE_GOOD, 6, 3, 0, 0, -1, -1, -1, -1, -1, 0, 0, -1, 0 };
        run o = encode_run(&odd, 99, 61);
        check_stream(&odd, &o, 99, 61, NULL, NULL, NULL);
        run_free(&o);
        config oddm = { "mono-odd-size", LAYOUT_MONO, 10, USAGE_GOOD, 6, 3, 1, 0, -1, -1, -1, -1, -1, 0, 1, -1, 0 };
        o = encode_run(&oddm, 99, 61);
        check_stream(&oddm, &o, 99, 61, NULL, NULL, NULL);
        run_free(&o);
    }

    printf("--- lock-step key frames ---\n");
    lockstep();

    printf("--- error paths ---\n");
    error_paths();

    printf("\n%d checks, %d failed\n", checks, failures);
    if (failures) { printf("SMOKE TEST FAILED\n"); return 1; }
    printf("SMOKE TEST PASSED\n");
    return 0;
}
