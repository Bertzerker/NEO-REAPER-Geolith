// SPDX-License-Identifier: MIT
// Libretro transport for the same machine that supplies the REA viewer.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <wchar.h>
#include "libretro.h"

#define SIGNATURE UINT32_C(0x3150524e)
static HANDLE connection = INVALID_HANDLE_VALUE;
static retro_environment_t environment;
static retro_video_refresh_t video_cb;
static retro_audio_sample_batch_t audio_cb;
static retro_input_poll_t poll_cb;
static retro_input_state_t input_cb;
static bool bitmasks;
static size_t state_size;
static double fps = 59.185606, sample_rate = 55555;
static uint32_t video[320 * 264];
static int16_t audio[8192];

static void disconnect(void) {
    if (connection != INVALID_HANDLE_VALUE) CloseHandle(connection);
    connection = INVALID_HANDLE_VALUE;
    state_size = 0;
}

static void message(const char *text) {
    struct retro_message notification = { text, 240 };
    if (environment) environment(RETRO_ENVIRONMENT_SET_MESSAGE, &notification);
}

static bool receive(void *data, size_t size) {
    unsigned char *position = data;
    while (size) {
        DWORD count;
        if (!ReadFile(connection, position, (DWORD)size, &count, NULL) || !count) return false;
        position += count;
        size -= count;
    }
    return true;
}

static bool send(const void *data, size_t size) {
    const unsigned char *position = data;
    while (size) {
        DWORD count;
        if (!WriteFile(connection, position, (DWORD)size, &count, NULL) || !count) return false;
        position += count;
        size -= count;
    }
    return true;
}

static bool cartridge_matches(const char *path) {
    wchar_t expected[32768], actual[32768], normalized[32768];
    DWORD length = GetEnvironmentVariableW(L"NR_RETRO_ROM", expected, 32768);
    if (!path || !length || length >= 32768 ||
        !MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path, -1, actual, 32768)) return false;
    length = GetFullPathNameW(actual, 32768, normalized, NULL);
    return length && length < 32768 && !_wcsicmp(expected, normalized);
}

static bool connect_host(void) {
    wchar_t name[128] = L"\\\\.\\pipe\\";
    DWORD length = GetEnvironmentVariableW(L"NR_RETRO_PIPE", name + 9, 119);
    if (!length || length >= 119) return false;
    for (unsigned attempt = 0; attempt < 100; ++attempt) {
        connection = CreateFileW(name, GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING,
            SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION, NULL);
        if (connection != INVALID_HANDLE_VALUE) break;
        Sleep(100);
    }
    if (connection == INVALID_HANDLE_VALUE) return false;
    const uint32_t hello = SIGNATURE;
    uint32_t reply[3];
    if (!send(&hello, sizeof(hello)) || !receive(reply, sizeof(reply)) ||
        reply[0] != SIGNATURE || reply[1] < 40 || reply[1] > 1024 * 1024 + 40 ||
        !receive(&fps, sizeof(fps))) {
        disconnect();
        return false;
    }
    state_size = reply[1];
    sample_rate = reply[2];
    return true;
}

static uint16_t pad(unsigned port) {
    static const unsigned buttons[] = {
        RETRO_DEVICE_ID_JOYPAD_UP, RETRO_DEVICE_ID_JOYPAD_DOWN,
        RETRO_DEVICE_ID_JOYPAD_LEFT, RETRO_DEVICE_ID_JOYPAD_RIGHT,
        RETRO_DEVICE_ID_JOYPAD_B, RETRO_DEVICE_ID_JOYPAD_A,
        RETRO_DEVICE_ID_JOYPAD_Y, RETRO_DEVICE_ID_JOYPAD_X,
        RETRO_DEVICE_ID_JOYPAD_START, RETRO_DEVICE_ID_JOYPAD_SELECT
    };
    uint16_t result = 0;
    const uint16_t pressed = bitmasks ? (uint16_t)input_cb(port, RETRO_DEVICE_JOYPAD, 0, RETRO_DEVICE_ID_JOYPAD_MASK) : 0;
    for (unsigned i = 0; i < sizeof(buttons) / sizeof(buttons[0]); ++i) {
        if (bitmasks ? (pressed & (1u << buttons[i])) != 0 : input_cb(port, RETRO_DEVICE_JOYPAD, 0, buttons[i]) != 0)
            result |= (uint16_t)(1u << i);
    }
    if ((result & 3) == 3) result &= ~3u;
    if ((result & 12) == 12) result &= ~12u;
    return result;
}

void retro_set_environment(retro_environment_t cb) { environment = cb; }
void retro_set_video_refresh(retro_video_refresh_t cb) { video_cb = cb; }
void retro_set_audio_sample(retro_audio_sample_t cb) { (void)cb; }
void retro_set_audio_sample_batch(retro_audio_sample_batch_t cb) { audio_cb = cb; }
void retro_set_input_poll(retro_input_poll_t cb) { poll_cb = cb; }
void retro_set_input_state(retro_input_state_t cb) { input_cb = cb; }
void retro_init(void) { bitmasks = environment(RETRO_ENVIRONMENT_GET_INPUT_BITMASKS, NULL); }
void retro_deinit(void) { disconnect(); }
unsigned retro_api_version(void) { return RETRO_API_VERSION; }
void retro_get_system_info(struct retro_system_info *info) {
    *info = (struct retro_system_info){ "NEO REAPER Geolith", "1", "neo", true, false };
}
void retro_get_system_av_info(struct retro_system_av_info *info) {
    *info = (struct retro_system_av_info){ {304, 224, 304, 224, 304.0f / 224.0f}, {fps, sample_rate} };
}
bool retro_load_game(const struct retro_game_info *game) {
    enum retro_pixel_format format = RETRO_PIXEL_FORMAT_XRGB8888;
    if (!game || !cartridge_matches(game->path)) {
        message("Launch this cartridge with Start-REA.ps1 to connect its live viewer.");
        return false;
    }
    if (!environment(RETRO_ENVIRONMENT_SET_PIXEL_FORMAT, &format) || !connect_host()) {
        message("Could not connect to the NEO REAPER host.");
        return false;
    }
    return true;
}
void retro_unload_game(void) { disconnect(); }
void retro_run(void) {
    if (connection == INVALID_HANDLE_VALUE) { video_cb(NULL, 304, 224, 320 * 4); return; }
    poll_cb();
    const unsigned char command = 'F';
    const uint16_t pads[] = {pad(0), pad(1)};
    uint32_t audio_bytes;
    if (!send(&command, 1) || !send(pads, sizeof(pads)) || !receive(&audio_bytes, sizeof(audio_bytes)) ||
        audio_bytes > sizeof(audio) || (audio_bytes % 4) || !receive(video, sizeof(video)) ||
        !receive(audio, audio_bytes)) {
        disconnect();
        message("NEO REAPER disconnected. Reopen the desktop session.");
        return;
    }
    video_cb(video + 24 * 320 + 8, 304, 224, 320 * 4);
    if (audio_bytes) audio_cb(audio, audio_bytes / 4);
}
size_t retro_serialize_size(void) { return state_size; }
bool retro_serialize(void *data, size_t size) {
    const unsigned char command = 'S';
    return state_size && size == state_size && send(&command, 1) && receive(data, size);
}
bool retro_unserialize(const void *data, size_t size) {
    const unsigned char command = 'L';
    unsigned char accepted;
    return state_size && size == state_size && send(&command, 1) && send(data, size) &&
        receive(&accepted, 1) && accepted == 1;
}
void retro_reset(void) {
    const unsigned char command = 'R';
    unsigned char reply;
    if (connection != INVALID_HANDLE_VALUE && (!send(&command, 1) || !receive(&reply, 1))) disconnect();
}
unsigned retro_get_region(void) { return RETRO_REGION_NTSC; }
void retro_set_controller_port_device(unsigned port, unsigned device) { (void)port; (void)device; }
void *retro_get_memory_data(unsigned id) { (void)id; return NULL; }
size_t retro_get_memory_size(unsigned id) { (void)id; return 0; }
void retro_cheat_reset(void) {}
void retro_cheat_set(unsigned index, bool enabled, const char *code) { (void)index; (void)enabled; (void)code; }
bool retro_load_game_special(unsigned type, const struct retro_game_info *game, size_t count) {
    (void)type; (void)game; (void)count;
    return false;
}
