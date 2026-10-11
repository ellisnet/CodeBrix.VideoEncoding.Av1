/*
 * layout-check.c - compile-time checks of the codebrix_av1.h ABI the managed binding assumes.
 *
 * Cross-compiled (never executed) for EVERY Android ABI by build.sh, so the checks run on the
 * target's data model, not the build host's. codebrix_av1.h deliberately has no struct, no
 * callback and no C `long`; what the managed LibraryImport declarations rely on is the width of
 * the scalar types, pointers, and the numeric values of the constants. Any drift fails the build
 * here instead of corrupting a call on a device.
 */
#include <stddef.h>
#include <stdint.h>
#include "codebrix_av1.h"

#define SIZE(type, size) _Static_assert(sizeof(type) == (size), #type " size")
#define VALUE(name, value) _Static_assert((name) == (value), #name " value")

SIZE(int32_t, 4);
SIZE(int64_t, 8);
SIZE(uint8_t, 1);
SIZE(void*, 8);                       /* both shipped Android ABIs are 64-bit */
SIZE(codebrix_av1_encoder*, 8);
SIZE(const char*, 8);

VALUE(CODEBRIX_AV1_API_VERSION, 1);
VALUE(CBA_OK, 0);
VALUE(CBA_E_INVALID_ARGUMENT, -1);
VALUE(CBA_E_OUT_OF_MEMORY, -2);
VALUE(CBA_E_INVALID_STATE, -3);
VALUE(CBA_E_UNSUPPORTED, -4);
VALUE(CBA_E_BUFFER_TOO_SMALL, -5);
VALUE(CBA_E_AOM_FAULT, -6);
VALUE(CBA_LAYOUT_I420, 0);
VALUE(CBA_LAYOUT_I444, 1);
VALUE(CBA_LAYOUT_MONOCHROME, 2);
VALUE(CBA_USAGE_GOOD_QUALITY, 0);
VALUE(CBA_USAGE_REALTIME, 1);
VALUE(CBA_USAGE_ALL_INTRA, 2);
VALUE(CBA_RATE_CONSTANT_QUALITY, 0);
VALUE(CBA_RATE_CONSTRAINED_QUALITY, 1);
VALUE(CBA_RATE_VARIABLE_BITRATE, 2);
VALUE(CBA_RATE_CONSTANT_BITRATE, 3);
VALUE(CBA_RANGE_LIMITED, 0);
VALUE(CBA_RANGE_FULL, 1);
VALUE(CBA_CHROMA_POSITION_UNKNOWN, 0);
VALUE(CBA_CHROMA_POSITION_VERTICAL, 1);
VALUE(CBA_CHROMA_POSITION_COLOCATED, 2);
VALUE(CBA_OPT_CPU_USED, 1);
VALUE(CBA_OPT_RATE_MODE, 2);
VALUE(CBA_OPT_QUALITY, 3);
VALUE(CBA_OPT_TARGET_BITRATE_KBPS, 4);
VALUE(CBA_OPT_LOSSLESS, 5);
VALUE(CBA_OPT_THREADS, 6);
VALUE(CBA_OPT_ROW_MULTITHREADING, 7);
VALUE(CBA_OPT_TILE_COLUMNS_LOG2, 8);
VALUE(CBA_OPT_TILE_ROWS_LOG2, 9);
VALUE(CBA_OPT_LAG_IN_FRAMES, 10);
VALUE(CBA_OPT_KEY_FRAME_MAX_DISTANCE, 11);
VALUE(CBA_OPT_COLOR_PRIMARIES, 12);
VALUE(CBA_OPT_TRANSFER_CHARACTERISTICS, 13);
VALUE(CBA_OPT_MATRIX_COEFFICIENTS, 14);
VALUE(CBA_OPT_COLOR_RANGE, 15);
VALUE(CBA_OPT_CHROMA_SAMPLE_POSITION, 16);
VALUE(CBA_OPT_AUTOMATIC_KEY_FRAMES, 17);

/* The function types, exactly as the managed side declares them. A changed signature in the
 * header makes one of these initialisations a constraint violation (an error with -Werror). */
typedef codebrix_av1_encoder enc;
static int32_t     (*const p_api)(void) = codebrix_av1_api_version;
static const char* (*const p_libver)(void) = codebrix_av1_library_version;
static int32_t     (*const p_create)(int32_t, int32_t, int32_t, int32_t, int32_t, int32_t, int32_t, enc**) = codebrix_av1_encoder_create;
static int32_t     (*const p_set_int32)(enc*, int32_t, int32_t) = codebrix_av1_encoder_set_int32;
static int32_t     (*const p_set_string)(enc*, const char*, const char*) = codebrix_av1_encoder_set_string_option;
static int32_t     (*const p_start)(enc*) = codebrix_av1_encoder_start;
static int32_t     (*const p_encode)(enc*, const uint8_t*, int32_t, const uint8_t*, int32_t, const uint8_t*, int32_t,
                                     int64_t, int32_t) = codebrix_av1_encoder_encode;
static int32_t     (*const p_flush)(enc*) = codebrix_av1_encoder_flush;
static int32_t     (*const p_pending)(enc*) = codebrix_av1_encoder_pending_packets;
static int32_t     (*const p_pkinfo)(enc*, int32_t*, int64_t*, int32_t*) = codebrix_av1_encoder_packet_info;
static int32_t     (*const p_packet)(enc*, uint8_t*, int32_t) = codebrix_av1_encoder_read_packet;
static int32_t     (*const p_seqlen)(enc*) = codebrix_av1_encoder_sequence_header_length;
static int32_t     (*const p_seq)(enc*, uint8_t*, int32_t) = codebrix_av1_encoder_read_sequence_header;
static int64_t     (*const p_frames)(enc*) = codebrix_av1_encoder_frames_submitted;
static int64_t     (*const p_packets)(enc*) = codebrix_av1_encoder_packets_produced;
static const char* (*const p_error)(enc*) = codebrix_av1_encoder_last_error;
static void        (*const p_destroy)(enc*) = codebrix_av1_encoder_destroy;

/* Referenced so -Wunused does not fire. */
const void* const codebrix_av1_layout_check_refs[] = {
    (const void*)&p_api, (const void*)&p_libver, (const void*)&p_create, (const void*)&p_set_int32,
    (const void*)&p_set_string, (const void*)&p_start, (const void*)&p_encode, (const void*)&p_flush,
    (const void*)&p_pending, (const void*)&p_pkinfo, (const void*)&p_packet, (const void*)&p_seqlen,
    (const void*)&p_seq, (const void*)&p_frames, (const void*)&p_packets, (const void*)&p_error,
    (const void*)&p_destroy,
};
