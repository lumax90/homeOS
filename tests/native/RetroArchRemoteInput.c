/* Isolated source-level regression harness. The inserted functions are unchanged
 * GPL-3.0 RetroArch source (COPYING in the pinned source snapshot).
 * Only loopback UDP; no core, ROM, emulator process, driver or product catalog. */
#include <winsock2.h>
#include <windows.h>
#include <stdint.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <errno.h>
#include <net/net_compat.h>
#include <retro_miscellaneous.h>
#include <libretro.h>
#define MAX_USERS 1
#define RARCH_BIND_LIST_END 64
#define HAVE_NETWORKGAMEPAD
#define HAVE_NETWORKING
/* HOME_UPSTREAM_TYPES */
typedef struct { input_remote_t *remote; input_remote_state_t remote_st_ptr; } input_driver_state_t;
typedef struct { struct { bool network_remote_enable_user[MAX_USERS]; } bools; } settings_t;
/* HOME_UPSTREAM_PARSE */
/* HOME_UPSTREAM_RECEIVE */
/* HOME_UPSTREAM_FIXED_POLL */
static void released_poll(input_driver_state_t *input_st, settings_t *settings, unsigned max_users)
{
/* HOME_UPSTREAM_RELEASED_POLL */
}
static void require(bool valid, const char *message)
{
   if (!valid) { fprintf(stderr, "%s (Winsock=%d)\n", message, WSAGetLastError()); exit(1); }
}
static void send_button(SOCKET sender, int id, bool pressed)
{
   struct remote_message message = {0}; message.device = RETRO_DEVICE_JOYPAD; message.id = id; message.state = pressed;
   require(send(sender, (char*)&message, sizeof(message), 0) == sizeof(message), "Button send failed");
}
int main(void)
{
   WSADATA wsa; require(WSAStartup(MAKEWORD(2,2), &wsa) == 0, "Winsock init failed");
   SOCKET receiver = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP), sender = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
   require(receiver != INVALID_SOCKET && sender != INVALID_SOCKET, "Socket creation failed");
   struct sockaddr_in address = {0}; address.sin_family = AF_INET; address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
   require(bind(receiver, (struct sockaddr*)&address, sizeof(address)) == 0, "Loopback bind failed");
   int length = sizeof(address); require(getsockname(receiver, (struct sockaddr*)&address, &length) == 0, "Address query failed");
   require(connect(sender, (struct sockaddr*)&address, sizeof(address)) == 0, "Sender connect failed");
   u_long nonblocking = 1; require(ioctlsocket(receiver, FIONBIO, &nonblocking) == 0, "Nonblocking setup failed");
   input_remote_t remote = {0}; remote.net_fd[0] = receiver;
   input_driver_state_t state = {0}; state.remote = &remote;
   settings_t settings = {0}; settings.bools.network_remote_enable_user[0] = true;
   require(sizeof(struct remote_message) == 20, "Windows ABI changed");
   send_button(sender, 8, true); Sleep(10); released_poll(&state, &settings, 1);
   require(state.remote_st_ptr.buttons[0] == (1u<<8), "Released source did not receive initial A");
   errno = 0; released_poll(&state, &settings, 1);
   bool released_held = state.remote_st_ptr.buttons[0] == (1u<<8);
   require(!released_held, "Expected released-source Windows regression not reproduced");
   send_button(sender, 8, true); Sleep(10); input_remote_poll(&state, &settings, 1);
   require(state.remote_st_ptr.buttons[0] == (1u<<8), "Fixed source did not receive A");
   for (unsigned frame = 0; frame < 32; frame++) { errno = 0; input_remote_poll(&state, &settings, 1); Sleep(10); require(state.remote_st_ptr.buttons[0] == (1u<<8), "A was released without a release packet"); }
   send_button(sender, 0, true); send_button(sender, 7, true); Sleep(10); input_remote_poll(&state, &settings, 1);
   uint64_t combined = (1u<<8) | (1u<<0) | (1u<<7);
   require(state.remote_st_ptr.buttons[0] == combined, "Queued simultaneous keys were not applied together");
   for (unsigned frame = 0; frame < 32; frame++) { input_remote_poll(&state, &settings, 1); Sleep(10); require(state.remote_st_ptr.buttons[0] == combined, "Combined input did not remain held"); }
   send_button(sender, 0, false); Sleep(10); input_remote_poll(&state, &settings, 1);
   require(state.remote_st_ptr.buttons[0] == (combined & ~1u), "One release cleared other held controls");
   char malformed = 0; require(send(sender, &malformed, 1, 0) == 1, "Short packet send failed"); Sleep(10); input_remote_poll(&state, &settings, 1);
   require(state.remote_st_ptr.buttons[0] == (combined & ~1u), "Fixed receiver unexpectedly used malformed neutral");
   for (unsigned button = 0; button < 16; button++) send_button(sender, button, false);
   Sleep(10); input_remote_poll(&state, &settings, 1); require(state.remote_st_ptr.buttons[0] == 0, "Valid release-all did not clear held input");
   closesocket(sender); closesocket(receiver); WSACleanup();
   printf("{\"sourceHarnessOnly\":true,\"loopbackOnly\":true,\"windowsAbiBytes\":%u,\"releasedHeld\":%s,\"fixedHeld\":true,\"combinedHeld\":true,\"validReleaseAll\":true,\"fullCoreVerified\":false}\n", (unsigned)sizeof(struct remote_message), released_held ? "true" : "false");
   return 0;
}
