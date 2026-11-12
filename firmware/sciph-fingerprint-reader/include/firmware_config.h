#pragma once

// Compile-time configuration. Every value can be overridden from platformio.ini (build_flags = -D NAME=value).
// Nothing secret lives here: the Wi-Fi password, the device key and the CA are provisioned over Serial into NVS.

#ifndef SCIPH_FIRMWARE_VERSION
#define SCIPH_FIRMWARE_VERSION "0.1.0"
#endif

// R307 on HardwareSerial(2), 8N1.
#ifndef SCIPH_R307_UART
#define SCIPH_R307_UART 2
#endif
#ifndef SCIPH_R307_RX_PIN
#define SCIPH_R307_RX_PIN 16
#endif
#ifndef SCIPH_R307_TX_PIN
#define SCIPH_R307_TX_PIN 17
#endif
#ifndef SCIPH_R307_BAUD
#define SCIPH_R307_BAUD 57600
#endif
// Sensor handshake password (factory default 0). Not a credential of SCI-PH.
#ifndef SCIPH_R307_PASSWORD
#define SCIPH_R307_PASSWORD 0x00000000
#endif

// Optional indicators; -1 = not connected.
#ifndef SCIPH_LED_PIN
#define SCIPH_LED_PIN -1
#endif
#ifndef SCIPH_LED_ACTIVE_HIGH
#define SCIPH_LED_ACTIVE_HIGH 1
#endif
#ifndef SCIPH_BUZZER_PIN
#define SCIPH_BUZZER_PIN -1
#endif

#ifndef SCIPH_CONSOLE_BAUD
#define SCIPH_CONSOLE_BAUD 115200
#endif

#ifndef SCIPH_NTP_SERVER_1
#define SCIPH_NTP_SERVER_1 "pool.ntp.org"
#endif
#ifndef SCIPH_NTP_SERVER_2
#define SCIPH_NTP_SERVER_2 "time.google.com"
#endif

// Plain HTTP to a Device API running with RequireHttps=false on an isolated development network. Only the
// esp32dev-devhttp environment sets it; the production environment cannot (see the #error below).
#ifndef SCIPH_ALLOW_DEV_HTTP
#define SCIPH_ALLOW_DEV_HTTP 0
#endif

// Slot numbers in serial logs: explicit diagnostic builds only. Normal logs never print slots.
#ifndef SCIPH_DEBUG_SLOTS
#define SCIPH_DEBUG_SLOTS 0
#endif

#if defined(SCIPH_PRODUCTION) && SCIPH_ALLOW_DEV_HTTP
#error "SCIPH_ALLOW_DEV_HTTP cannot be enabled in a production build"
#endif

#if defined(SCIPH_PRODUCTION) && SCIPH_DEBUG_SLOTS
#error "SCIPH_DEBUG_SLOTS cannot be enabled in a production build"
#endif
