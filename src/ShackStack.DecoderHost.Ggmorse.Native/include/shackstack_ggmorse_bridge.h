#pragma once

#ifdef _WIN32
#  ifdef SHACKSTACK_GGMORSE_BRIDGE_BUILD
#    define SHACKSTACK_GGMORSE_API __declspec(dllexport)
#  else
#    define SHACKSTACK_GGMORSE_API __declspec(dllimport)
#  endif
#else
#  define SHACKSTACK_GGMORSE_API
#endif

#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct shackstack_ggmorse_handle * shackstack_ggmorse_handle_t;

typedef struct {
    float input_sample_rate_hz;
    int32_t samples_per_frame;
} shackstack_ggmorse_create_params;

typedef struct {
    float pitch_hz;
    float speed_wpm;
    float frequency_min_hz;
    float frequency_max_hz;
    bool auto_pitch;
    bool auto_speed;
    bool apply_high_pass;
    bool apply_low_pass;
} shackstack_ggmorse_decode_params;

typedef struct {
    float estimated_pitch_hz;
    float estimated_speed_wpm;
    float signal_threshold;
    float cost_function;
    int32_t last_decode_result;
} shackstack_ggmorse_stats;

SHACKSTACK_GGMORSE_API bool shackstack_ggmorse_create(
    const shackstack_ggmorse_create_params * params,
    shackstack_ggmorse_handle_t * out_handle);

SHACKSTACK_GGMORSE_API void shackstack_ggmorse_destroy(
    shackstack_ggmorse_handle_t handle);

SHACKSTACK_GGMORSE_API bool shackstack_ggmorse_configure(
    shackstack_ggmorse_handle_t handle,
    const shackstack_ggmorse_decode_params * params);

SHACKSTACK_GGMORSE_API bool shackstack_ggmorse_reset(
    shackstack_ggmorse_handle_t handle);

SHACKSTACK_GGMORSE_API bool shackstack_ggmorse_push_audio_f32(
    shackstack_ggmorse_handle_t handle,
    const float * samples,
    int32_t sample_count);

SHACKSTACK_GGMORSE_API int32_t shackstack_ggmorse_take_text_utf8(
    shackstack_ggmorse_handle_t handle,
    char * buffer,
    int32_t buffer_size);

SHACKSTACK_GGMORSE_API bool shackstack_ggmorse_get_stats(
    shackstack_ggmorse_handle_t handle,
    shackstack_ggmorse_stats * out_stats);

#ifdef __cplusplus
}
#endif
