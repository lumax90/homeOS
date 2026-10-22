/* Original isolated libretro GPU/input/state fixture. Not an emulator or playable Home game.
 * No ROM, BIOS, external artwork or model data. SPDX-License-Identifier: MIT
 */
#include <libretro.h>
#include <stdint.h>
#include <string.h>
#include <stdio.h>
#include <stdlib.h>

static retro_environment_t environment;
static retro_video_refresh_t video;
static retro_audio_sample_batch_t audio;
static retro_input_poll_t poll;
static retro_input_state_t input;
static struct retro_hw_render_callback render;
static uint32_t frame;
static int16_t held;
static bool disk_ejected;static unsigned disk_index;
static bool set_ejected(bool value){disk_ejected=value;return true;}
static bool get_ejected(void){return disk_ejected;}
static unsigned get_index(void){return disk_index;}
static bool set_index(unsigned value){if(!disk_ejected||value>=2)return false;disk_index=value;return true;}
static unsigned disk_count(void){return 2;}
static bool replace_disk(unsigned index,const struct retro_game_info* game){(void)index;(void)game;return false;}
static bool add_disk(void){return false;}
static struct {uint32_t frame;int16_t button;int16_t axes[4];int16_t pointer[3];int16_t key;int16_t pressure;int16_t second;int16_t third;int16_t fourth;uint16_t key_events;uint16_t key_code;uint16_t character;int16_t key_down;} observation;
static void keyboard_event(bool down,unsigned code,uint32_t character,uint16_t modifiers){(void)modifiers;observation.key_events++;observation.key_code=(uint16_t)code;observation.character=(uint16_t)character;observation.key_down=down;}
#include "SyntheticGpuBackends.h"
void retro_set_environment(retro_environment_t callback){environment=callback;bool no_game=true;environment(RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME,&no_game);static const struct retro_variable variables[]={{"home_fixture_mode","Original diagnostic option; default|alternate"},{0,0}};environment(RETRO_ENVIRONMENT_SET_VARIABLES,(void*)variables);static const struct retro_disk_control_callback disks={set_ejected,get_ejected,get_index,set_index,disk_count,replace_disk,add_disk};environment(RETRO_ENVIRONMENT_SET_DISK_CONTROL_INTERFACE,(void*)&disks);static const struct retro_keyboard_callback keyboard={keyboard_event};environment(RETRO_ENVIRONMENT_SET_KEYBOARD_CALLBACK,(void*)&keyboard);}
void retro_set_video_refresh(retro_video_refresh_t callback){video=callback;}
void retro_set_audio_sample(retro_audio_sample_t callback){(void)callback;}
void retro_set_audio_sample_batch(retro_audio_sample_batch_t callback){audio=callback;}
void retro_set_input_poll(retro_input_poll_t callback){poll=callback;}
void retro_set_input_state(retro_input_state_t callback){input=callback;}
unsigned retro_api_version(void){return RETRO_API_VERSION;}
void retro_init(void){}
void retro_deinit(void){}
void retro_get_system_info(struct retro_system_info* info){memset(info,0,sizeof(*info));info->library_name="Home ORIGINAL isolated GPU fixture";info->library_version="1";info->valid_extensions="fixture";}
void retro_get_system_av_info(struct retro_system_av_info* info){memset(info,0,sizeof(*info));info->timing.fps=60.0;info->timing.sample_rate=48000;info->geometry.base_width=400;info->geometry.base_height=480;info->geometry.max_width=400;info->geometry.max_height=480;info->geometry.aspect_ratio=400.0/480.0;}
bool retro_load_game(const struct retro_game_info* game){(void)game;enum retro_pixel_format format=RETRO_PIXEL_FORMAT_XRGB8888;environment(RETRO_ENVIRONMENT_SET_PIXEL_FORMAT,&format);struct retro_memory_descriptor memory={0};memory.ptr=&observation;memory.start=0x1000;memory.len=sizeof(observation);struct retro_memory_map map={&memory,1};environment(RETRO_ENVIRONMENT_SET_MEMORY_MAPS,&map);memset(&render,0,sizeof(render));render.context_type=HOME_CONTEXT;render.version_major=HOME_CONTEXT_MAJOR;render.version_minor=HOME_CONTEXT_MINOR;render.context_reset=reset_context;render.context_destroy=destroy_context;render.bottom_left_origin=true;return environment(RETRO_ENVIRONMENT_SET_HW_RENDER,&render);}
bool retro_load_game_special(unsigned type,const struct retro_game_info* game,size_t count){(void)type;(void)game;(void)count;return false;}
void retro_unload_game(void){}
void retro_reset(void){frame=0;held=0;}
void retro_run(void){int16_t samples[1600];unsigned i;poll();held=input(0,RETRO_DEVICE_JOYPAD,0,RETRO_DEVICE_ID_JOYPAD_A);observation.frame=frame;observation.button=held;for(i=0;i<4;i++)observation.axes[i]=input(0,RETRO_DEVICE_ANALOG,i/2,i%2);for(i=0;i<3;i++)observation.pointer[i]=input(0,RETRO_DEVICE_POINTER,0,i);observation.key=input(0,RETRO_DEVICE_KEYBOARD,0,RETROK_a);observation.pressure=input(0,RETRO_DEVICE_ANALOG,RETRO_DEVICE_INDEX_ANALOG_BUTTON,RETRO_DEVICE_ID_JOYPAD_L2);observation.second=input(1,RETRO_DEVICE_JOYPAD,0,RETRO_DEVICE_ID_JOYPAD_A);observation.third=input(2,RETRO_DEVICE_JOYPAD,0,RETRO_DEVICE_ID_JOYPAD_A);observation.fourth=input(3,RETRO_DEVICE_JOYPAD,0,RETRO_DEVICE_ID_JOYPAD_A);paint(held);video(RETRO_HW_FRAME_BUFFER_VALID,400,480,0);for(i=0;i<1600;i++)samples[i]=(int16_t)(((frame*800+i/2)%120)<60?1500:-1500);audio(samples,800);frame++;}
size_t retro_serialize_size(void){return sizeof(observation)+8;}
bool retro_serialize(void* data,size_t bytes){if(bytes<retro_serialize_size())return false;memcpy(data,&frame,4);memcpy((uint8_t*)data+4,&held,2);memset((uint8_t*)data+6,0,2);memcpy((uint8_t*)data+8,&observation,sizeof(observation));return true;}
bool retro_unserialize(const void* data,size_t bytes){if(bytes<retro_serialize_size())return false;memcpy(&frame,data,4);memcpy(&held,(const uint8_t*)data+4,2);memcpy(&observation,(const uint8_t*)data+8,sizeof(observation));return true;}
void* retro_get_memory_data(unsigned type){return type==RETRO_MEMORY_SYSTEM_RAM?&frame:0;}
size_t retro_get_memory_size(unsigned type){return type==RETRO_MEMORY_SYSTEM_RAM?4:0;}
unsigned retro_get_region(void){return RETRO_REGION_NTSC;}
void retro_set_controller_port_device(unsigned port,unsigned device){(void)port;(void)device;}
void retro_cheat_reset(void){}
void retro_cheat_set(unsigned index,bool enabled,const char* code){(void)index;(void)enabled;(void)code;}
