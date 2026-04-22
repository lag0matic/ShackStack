#define SHACKSTACK_GGMORSE_BRIDGE_BUILD

#include "shackstack_ggmorse_bridge.h"

#include <ggmorse/ggmorse.h>

#include <algorithm>
#include <cstring>
#include <deque>
#include <memory>
#include <string>
#include <vector>

struct shackstack_ggmorse_handle {
    std::unique_ptr<GGMorse> decoder;
    std::deque<float> pendingSamples;
    std::string pendingText;
    GGMorse::Statistics stats{};
    int32_t lastDecodeResult = 0;
    int32_t samplesPerFrame = GGMorse::kDefaultSamplesPerFrame;
    int32_t inputSamplesPerDecodeBlock = GGMorse::kDefaultSamplesPerFrame;
    float inputSampleRateHz = 48000.0f;
};

static void append_rx_text(shackstack_ggmorse_handle & handle) {
    GGMorse::TxRx rx;
    const auto n = handle.decoder->takeRxData(rx);
    if (n <= 0 || rx.empty()) {
        return;
    }

    handle.pendingText.append(reinterpret_cast<const char *>(rx.data()), static_cast<size_t>(rx.size()));
}

bool shackstack_ggmorse_create(
    const shackstack_ggmorse_create_params * params,
    shackstack_ggmorse_handle_t * out_handle) {
    if (params == nullptr || out_handle == nullptr) {
        return false;
    }

    auto handle = std::make_unique<shackstack_ggmorse_handle>();

    auto nativeParams = GGMorse::getDefaultParameters();
    nativeParams.sampleRateInp = params->input_sample_rate_hz > 0.0f ? params->input_sample_rate_hz : 48000.0f;
    nativeParams.sampleRateOut = nativeParams.sampleRateInp;
    nativeParams.samplesPerFrame = params->samples_per_frame > 0 ? params->samples_per_frame : GGMorse::kDefaultSamplesPerFrame;
    nativeParams.sampleFormatInp = GGMORSE_SAMPLE_FORMAT_F32;
    nativeParams.sampleFormatOut = GGMORSE_SAMPLE_FORMAT_F32;

    handle->samplesPerFrame = nativeParams.samplesPerFrame;
    handle->inputSampleRateHz = nativeParams.sampleRateInp;
    handle->inputSamplesPerDecodeBlock = std::max(
        1,
        static_cast<int32_t>(std::lround((nativeParams.sampleRateInp / GGMorse::kBaseSampleRate) * nativeParams.samplesPerFrame)));
    handle->decoder = std::make_unique<GGMorse>(nativeParams);

    auto decodeParams = GGMorse::getDefaultParametersDecode();
    decodeParams.frequency_hz = 700.0f;
    decodeParams.speed_wpm = 20.0f;
    decodeParams.frequencyRangeMin_hz = 200.0f;
    decodeParams.frequencyRangeMax_hz = 1200.0f;
    decodeParams.applyFilterHighPass = true;
    decodeParams.applyFilterLowPass = true;

    if (!handle->decoder->setParametersDecode(decodeParams)) {
        return false;
    }

    *out_handle = handle.release();
    return true;
}

void shackstack_ggmorse_destroy(shackstack_ggmorse_handle_t handle) {
    delete handle;
}

bool shackstack_ggmorse_configure(
    shackstack_ggmorse_handle_t handle,
    const shackstack_ggmorse_decode_params * params) {
    if (handle == nullptr || params == nullptr) {
        return false;
    }

    auto decodeParams = GGMorse::getDefaultParametersDecode();
    decodeParams.frequency_hz = params->pitch_hz;
    decodeParams.speed_wpm = params->speed_wpm;
    decodeParams.frequencyRangeMin_hz = params->frequency_min_hz;
    decodeParams.frequencyRangeMax_hz = params->frequency_max_hz;
    decodeParams.applyFilterHighPass = params->apply_high_pass;
    decodeParams.applyFilterLowPass = params->apply_low_pass;

    if (params->auto_pitch) {
        decodeParams.frequency_hz = 0.0f;
    }

    if (params->auto_speed) {
        decodeParams.speed_wpm = 0.0f;
    }

    return handle->decoder->setParametersDecode(decodeParams);
}

bool shackstack_ggmorse_reset(shackstack_ggmorse_handle_t handle) {
    if (handle == nullptr) {
        return false;
    }

    handle->pendingSamples.clear();
    handle->pendingText.clear();
    handle->lastDecodeResult = 0;

    auto decodeParams = handle->decoder->getDefaultParametersDecode();
    return handle->decoder->setParametersDecode(decodeParams);
}

bool shackstack_ggmorse_push_audio_f32(
    shackstack_ggmorse_handle_t handle,
    const float * samples,
    int32_t sample_count) {
    if (handle == nullptr || samples == nullptr || sample_count <= 0) {
        return false;
    }

    handle->pendingSamples.insert(handle->pendingSamples.end(), samples, samples + sample_count);

    const auto inputBlockSamples = std::max(1, handle->inputSamplesPerDecodeBlock);

    while (static_cast<int32_t>(handle->pendingSamples.size()) >= inputBlockSamples) {
        const auto ok = handle->decoder->decode([&](void * data, uint32_t nMaxBytes) -> uint32_t {
            const auto requestedSamples = static_cast<int32_t>(nMaxBytes / sizeof(float));
            if (requestedSamples <= 0 || static_cast<int32_t>(handle->pendingSamples.size()) < requestedSamples) {
                return 0;
            }

            auto * dst = reinterpret_cast<float *>(data);
            for (int32_t i = 0; i < requestedSamples; ++i) {
                dst[i] = handle->pendingSamples.front();
                handle->pendingSamples.pop_front();
            }

            return static_cast<uint32_t>(requestedSamples * sizeof(float));
        });

        handle->lastDecodeResult = ok ? 1 : 0;
        handle->stats = handle->decoder->getStatistics();
        append_rx_text(*handle);
    }

    return true;
}

int32_t shackstack_ggmorse_take_text_utf8(
    shackstack_ggmorse_handle_t handle,
    char * buffer,
    int32_t buffer_size) {
    if (handle == nullptr || buffer == nullptr || buffer_size <= 1) {
        return 0;
    }

    const auto nCopy = static_cast<int32_t>(std::min<size_t>(handle->pendingText.size(), static_cast<size_t>(buffer_size - 1)));
    if (nCopy <= 0) {
        buffer[0] = '\0';
        return 0;
    }

    std::memcpy(buffer, handle->pendingText.data(), static_cast<size_t>(nCopy));
    buffer[nCopy] = '\0';
    handle->pendingText.erase(0, static_cast<size_t>(nCopy));
    return nCopy;
}

bool shackstack_ggmorse_get_stats(
    shackstack_ggmorse_handle_t handle,
    shackstack_ggmorse_stats * out_stats) {
    if (handle == nullptr || out_stats == nullptr) {
        return false;
    }

    out_stats->estimated_pitch_hz = handle->stats.estimatedPitch_Hz;
    out_stats->estimated_speed_wpm = handle->stats.estimatedSpeed_wpm;
    out_stats->signal_threshold = handle->stats.signalThreshold;
    out_stats->cost_function = handle->stats.costFunction;
    out_stats->last_decode_result = handle->lastDecodeResult;
    return true;
}
