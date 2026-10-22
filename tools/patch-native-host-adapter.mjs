import {readFile,writeFile} from 'node:fs/promises';
import {join,resolve,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
// Narrow host adapter additions on pinned released RetroArch, not an emulator/codec/streaming engine.
if(process.argv.length!==3)throw new Error('Use <isolated build directory>');
const root=resolve(process.argv[2]);const changed=[];
async function edit(name,transform){const path=join(root,name),before=await readFile(path,'utf8'),after=transform(before);if(before===after)throw new Error('Expected pinned source anchor missing: '+name);await writeFile(path,after);changed.push({path:name,beforeSha256:createHash('sha256').update(before).digest('hex'),afterSha256:createHash('sha256').update(after).digest('hex')});}
function replaceOnce(text,from,to){if(!text.includes(from)||text.indexOf(from)!==text.lastIndexOf(from))throw new Error('Pinned unique anchor changed: '+from.slice(0,60));return text.replace(from,to);}
await edit('input/input_driver.h',t=>replaceOnce(t,'   int16_t analog[4][MAX_USERS];','   int16_t analog[4][MAX_USERS];\n   /* Home authenticated loopback pointer/keyboard adapter. */\n   int16_t analog_buttons[16][MAX_USERS];\n   int16_t pointer[4][MAX_USERS];\n   int16_t mouse[11][MAX_USERS];\n   int16_t lightgun[17][MAX_USERS];\n   uint8_t keyboard[RETROK_LAST][MAX_USERS];'));
await edit('input/input_driver.c',t=>{
  t=replaceOnce(t,'socket_init((void**)&res, port, NULL, SOCKET_TYPE_DATAGRAM, AF_INET)','socket_init((void**)&res, port, "127.0.0.1", SOCKET_TYPE_DATAGRAM, AF_INET)');
  t=replaceOnce(t,'      case RETRO_DEVICE_ANALOG:\n         if (msg->id<2 && msg->index<2)','      case RETRO_DEVICE_POINTER:\n         if (msg->index == 0 && msg->id < 4) input_state->pointer[msg->id][user] = msg->state;\n         break;\n      case RETRO_DEVICE_MOUSE:\n         if (msg->id < 11) input_state->mouse[msg->id][user] = msg->state;\n         break;\n      case RETRO_DEVICE_LIGHTGUN:\n         if (msg->id < 17) input_state->lightgun[msg->id][user] = msg->state;\n         break;\n      case RETRO_DEVICE_KEYBOARD:\n         if (msg->id < RETROK_LAST)\n         {\n            bool down = msg->state != 0;\n            if (user == 0 && input_state->keyboard[msg->id][user] != down)\n               input_keyboard_event(down, msg->id, msg->id < 127 ? msg->id : 0, 0, RETRO_DEVICE_KEYBOARD);\n            input_state->keyboard[msg->id][user] = down;\n         }\n         break;\n      case RETRO_DEVICE_ANALOG:\n         if (msg->id<2 && msg->index<2)');
  t=replaceOnce(t,'      fd = (int)input_st->remote->net_fd[user];','      fd = (int)input_st->remote->net_fd[user];\n      input_st->remote_st_ptr.mouse[0][user] = 0;\n      input_st->remote_st_ptr.mouse[1][user] = 0;');
  t=replaceOnce(t,'      input_st->remote_st_ptr.mouse[1][user] = 0;','      input_st->remote_st_ptr.mouse[1][user] = 0;\n      input_st->remote_st_ptr.mouse[4][user] = 0;\n      input_st->remote_st_ptr.mouse[5][user] = 0;');
  t=replaceOnce(t,'         if (msg->id < 11) input_state->mouse[msg->id][user] = msg->state;','         if (msg->id < 11)\n         {\n            int value = msg->state;\n            if (msg->id < 2) value += input_state->mouse[msg->id][user];\n            input_state->mouse[msg->id][user] = value < -32768 ? -32768 : value > 32767 ? 32767 : value;\n         }');
  t=replaceOnce(t,'               input_keyboard_event(down, msg->id, msg->id < 127 ? msg->id : 0, 0, RETRO_DEVICE_KEYBOARD);','            {\n               uint16_t modifiers = 0; unsigned character = msg->id < 127 ? msg->id : 0;\n               if (input_state->keyboard[RETROK_LSHIFT][user] || input_state->keyboard[RETROK_RSHIFT][user]) modifiers |= RETROKMOD_SHIFT;\n               if (input_state->keyboard[RETROK_LCTRL][user] || input_state->keyboard[RETROK_RCTRL][user]) modifiers |= RETROKMOD_CTRL;\n               if (input_state->keyboard[RETROK_LALT][user] || input_state->keyboard[RETROK_RALT][user]) modifiers |= RETROKMOD_ALT;\n               if ((modifiers & RETROKMOD_SHIFT) && character >= 97 && character <= 122) character -= 32;\n               input_keyboard_event(down, msg->id, character, modifiers, RETRO_DEVICE_KEYBOARD);\n            }');
  t=replaceOnce(t,'            if (user == 0 && input_state->keyboard[msg->id][user] != down)','            bool changed = input_state->keyboard[msg->id][user] != down;\n            input_state->keyboard[msg->id][user] = down;\n            if (user == 0 && changed)');
  t=replaceOnce(t,'               input_keyboard_event(down, msg->id, character, modifiers, RETRO_DEVICE_KEYBOARD);','               if ((modifiers & RETROKMOD_SHIFT) && character < 127)\n               {\n                  const char *plain = "1234567890-=[];\'`,./\\\\";\n                  const char *shifted = "!@#$%^&*()_+{}:\\\"~<>?|";\n                  const char *match = strchr(plain, (int)character);\n                  if (character && match) character = shifted[match - plain];\n               }\n               /* The registered libretro callback receives authenticated game keys, independently of frontend hotkey bindings. */\n               if (runloop_state_get_ptr()->frontend_key_event)\n                  runloop_state_get_ptr()->frontend_key_event(down, msg->id, character, modifiers);');
  t=replaceOnce(t,'            input_state->analog[msg->index * 2 + msg->id][user] = msg->state;','            input_state->analog[msg->index * 2 + msg->id][user] = msg->state;\n         else if (msg->index == 2 && msg->id < 16)\n            input_state->analog_buttons[msg->id][user] = msg->state;');
  const anchor='   int16_t res  = 0;\n\n   switch (device)';
  t=replaceOnce(t,anchor,'   int16_t res  = 0;\n\n#if defined(HAVE_NETWORKING) && defined(HAVE_NETWORKGAMEPAD)\n   /* Already authenticated by Home before its loopback relay. No OS input injection. */\n   if (input_st->remote && port < MAX_USERS)\n   {\n      input_remote_state_t *remote = &input_st->remote_st_ptr;\n      if (device == RETRO_DEVICE_ANALOG && idx == 2 && id < 16) return remote->analog_buttons[id][port];\n      if (device == RETRO_DEVICE_POINTER && idx == 0 && id < 4) return remote->pointer[id][port];\n      if (device == RETRO_DEVICE_MOUSE && id < 11) return remote->mouse[id][port];\n      if (device == RETRO_DEVICE_LIGHTGUN && id < 17) return remote->lightgun[id][port];\n      if (device == RETRO_DEVICE_KEYBOARD && id < RETROK_LAST) return remote->keyboard[id][port];\n   }\n#endif\n\n   switch (device)');
  t=replaceOnce(t,'return remote->analog_buttons[id][port];','return remote->analog_buttons[id][port] ? remote->analog_buttons[id][port] : (remote->buttons[port] & (UINT64_C(1) << id)) ? 32767 : 0;');
  return t;
});
await edit('command.c',t=>{
  t=replaceOnce(t,'socket_init((void**)&res, port, NULL,\n         SOCKET_TYPE_DATAGRAM, AF_INET)','socket_init((void**)&res, port, "127.0.0.1",\n         SOCKET_TYPE_DATAGRAM, AF_INET)');
  return replaceOnce(t,'bool command_get_status(command_t *cmd, const char* arg)',`/* Home disk facade over upstream's libretro disk-control interface. */
bool command_home_disk_status(command_t *cmd, const char *arg)
{
   char reply[128]; size_t len;
   disk_control_interface_t *disk = &runloop_state_get_ptr()->system.disk_control;
   unsigned count = disk_control_enabled(disk) ? disk_control_get_num_images(disk) : 0;
   unsigned index = count ? disk_control_get_image_index(disk) : 0;
   len = snprintf(reply, sizeof(reply), "HOME_DISK_STATUS %u %u %u\\n", count, index,
         count && disk_control_get_eject_state(disk) ? 1 : 0);
   cmd->replier(cmd, reply, len); return true;
}
bool command_home_disk_set(command_t *cmd, const char *arg)
{
   unsigned index; char extra; bool success = false; char reply[4];
   disk_control_interface_t *disk = &runloop_state_get_ptr()->system.disk_control;
   if (sscanf(arg, "%u %c", &index, &extra) == 1 && index < disk_control_get_num_images(disk))
      success = disk_control_set_eject_state(disk, true, false)
            && disk_control_set_index(disk, index, false)
            && disk_control_set_eject_state(disk, false, false);
   strlcpy(reply, success ? "OK\\n" : "NO\\n", sizeof(reply));
   cmd->replier(cmd, reply, 3); return success;
}

bool command_get_status(command_t *cmd, const char* arg)`);
});
await edit('command.h',t=>{
  t=replaceOnce(t,'bool command_get_status(command_t *cmd, const char* arg);','bool command_get_status(command_t *cmd, const char* arg);\nbool command_home_disk_status(command_t *cmd, const char* arg);\nbool command_home_disk_set(command_t *cmd, const char* arg);');
  return replaceOnce(t,'   { "GET_STATUS",       command_get_status,       "No argument" },','   { "HOME_DISK_STATUS", command_home_disk_status, "No argument" },\n   { "HOME_DISK_SET", command_home_disk_set, "<index>" },\n   { "GET_STATUS",       command_get_status,       "No argument" },');
});
const optionsSource=await readFile(join(dirname(fileURLToPath(import.meta.url)),'native-host-options.c'),'utf8');
await writeFile(join(root,'home-core-options.c'),optionsSource);
changed.push({path:'home-core-options.c',beforeSha256:null,afterSha256:createHash('sha256').update(optionsSource).digest('hex')});
await edit('command.c',t=>replaceOnce(t,'/* Home disk facade over upstream', '#include "core_option_manager.h"\n#include "home-core-options.c"\n\n/* Home disk facade over upstream'));
await edit('command.h',t=>{
  t=replaceOnce(t,'bool command_home_disk_set(command_t *cmd, const char* arg);','bool command_home_disk_set(command_t *cmd, const char* arg);\nbool command_home_option_get(command_t *cmd, const char* arg);\nbool command_home_option_set(command_t *cmd, const char* arg);\nbool command_home_timing(command_t *cmd, const char* arg);\nbool command_home_device_get(command_t *cmd, const char* arg);\nbool command_home_device_set(command_t *cmd, const char* arg);');
  t=replaceOnce(t,'bool command_home_timing(command_t *cmd, const char* arg);','bool command_home_timing(command_t *cmd, const char* arg);\nbool command_home_state_size(command_t *cmd, const char* arg);');
  return replaceOnce(t,'   { "HOME_DISK_SET", command_home_disk_set, "<index>" },','   { "HOME_DISK_SET", command_home_disk_set, "<index>" },\n   { "HOME_OPTION_GET", command_home_option_get, "<index>" },\n   { "HOME_OPTION_SET", command_home_option_set, "<index> <value-index>" },\n   { "HOME_TIMING", command_home_timing, "No argument" },\n   { "HOME_STATE_SIZE", command_home_state_size, "No argument" },\n   { "HOME_DEVICE_GET", command_home_device_get, "<port>" },\n   { "HOME_DEVICE_SET", command_home_device_set, "<port> <device>" },');
});
await edit('record/drivers/record_ffmpeg.c',t=>{
  t=replaceOnce(t,'   AVDictionary *audio_opts;','   AVDictionary *audio_opts;\n   AVDictionary *format_opts;');
  t=replaceOnce(t,'   bool audio_enable;','   bool audio_enable;\n   bool home_low_delay;');
  t=replaceOnce(t,'      else if (strstr(entry.key, "audio_") == entry.key)','      else if (strstr(entry.key, "format_") == entry.key)\n      {\n         const char *key = entry.key + STRLEN_CONST("format_");\n         av_dict_set(&params->format_opts, key, entry.value, 0);\n      }\n      else if (strstr(entry.key, "audio_") == entry.key)');
  t=replaceOnce(t,'   return avformat_write_header(handle->muxer.ctx, NULL) >= 0;','   return avformat_write_header(handle->muxer.ctx, &handle->config.format_opts) >= 0;');
  t=replaceOnce(t,'   config_get_uint(params->conf, "sample_rate", &params->sample_rate);','   config_get_bool(params->conf, "home_low_delay", &params->home_low_delay);\n   config_get_uint(params->conf, "sample_rate", &params->sample_rate);');
  t=replaceOnce(t,'   handle->muxer.ctx      = ctx;','   handle->muxer.ctx      = ctx;\n   if (handle->config.home_low_delay)\n   {\n      ctx->flags |= AVFMT_FLAG_FLUSH_PACKETS;\n      ctx->max_delay = 0;\n      ctx->max_interleave_delta = 1;\n   }');
  t=t.replaceAll('      av_packet_unref(pkt);','      if (handle->config.home_low_delay) avio_flush(handle->muxer.ctx->pb);\n      av_packet_unref(pkt);');
  t=replaceOnce(t,'   if (handle->config.audio_opts)\n      av_dict_free(&handle->config.audio_opts);','   if (handle->config.audio_opts)\n      av_dict_free(&handle->config.audio_opts);\n   if (handle->config.format_opts)\n      av_dict_free(&handle->config.format_opts);');
  return t;
});
const headers=['gfx/common/gl3_defines.h'];let patched=false;
for(const name of headers){let text;try{text=await readFile(join(root,name),'utf8')}catch{continue}if(/#define\s+GL_CORE_NUM_PBOS\s+4/.test(text)){await edit(name,t=>t.replace(/(#define\s+GL_CORE_NUM_PBOS\s+)4/,(_,prefix)=>prefix+'1'));patched=true;break;}}
if(!patched)throw new Error('Pinned GPU PBO count definition missing.');
await edit('gfx/drivers/gl3.c',t=>{
  t=replaceOnce(t,'glDeleteBuffers(4, gl->pbo_readback);','glDeleteBuffers(GL_CORE_NUM_PBOS, gl->pbo_readback);');
  t=replaceOnce(t,'   glBindBuffer(GL_PIXEL_PACK_BUFFER,\n         gl->pbo_readback[gl->pbo_readback_index++]);','   gl->pbo_readback_valid[gl->pbo_readback_index] = true;\n   glBindBuffer(GL_PIXEL_PACK_BUFFER,\n         gl->pbo_readback[gl->pbo_readback_index++]);');
  t=replaceOnce(t,'   gl->pbo_readback_valid[gl->pbo_readback_index] = true;\n\n   glReadPixels','\n   glReadPixels');
  t=replaceOnce(t,'      type = DEFAULT_SHADER_TYPE;','      /* Keep the lightweight stock GLSL path for unfiltered game streaming. */\n#ifdef HAVE_GLSL\n      type = config_get_ptr()->bools.video_gpu_record ? RARCH_SHADER_GLSL : DEFAULT_SHADER_TYPE;\n#else\n      type = DEFAULT_SHADER_TYPE;\n#endif');
  // Recording is initialized after the GL driver. Arm the existing PBO path from the requested config.
  return replaceOnce(t,'   if (video_gpu_record\n      && recording_state_get_ptr()->enable)','   if (video_gpu_record)');
});
await edit('gfx/video_driver.c',t=>{
  t=replaceOnce(t,'         ) && recording_st->data\n           && recording_st->driver','         ) && recording_st->data && !video_st->record_gpu_buffer\n           && recording_st->driver');
  return replaceOnce(t,'   video_st->frame_count++;\n\n   /* Display the status text','   /* GPU recording consumes this rendered frame, rather than the previous framebuffer. */\n   if (render_frame && video_st->record_gpu_buffer && recording_st->data\n         && recording_st->driver && recording_st->driver->push_video)\n      recording_dump_frame(data, width, height, pitch, runloop_idle);\n\n   video_st->frame_count++;\n\n   /* Display the status text');
});
await edit('gfx/drivers/d3d11.c',t=>replaceOnce(t,'   *vp = d3d11->vp;','   /* GPU recording asks for the viewport before the first presented frame. */\n   if (!d3d11->vp.width || !d3d11->vp.height) d3d11_update_viewport(d3d11, false);\n   *vp = d3d11->vp;'));
await edit('gfx/drivers/vulkan.c',t=>{
  t=replaceOnce(t,'   if (!(video_gpu_record && rec_st->enable))','   if (!video_gpu_record)');
  t=replaceOnce(t,'      struct vk_texture staging[VULKAN_MAX_SWAPCHAIN_IMAGES];','      struct vk_texture staging[VULKAN_MAX_SWAPCHAIN_IMAGES];\n      unsigned home_last_index;');
  t=replaceOnce(t,'   staging  = &vk->readback.staging[vk->context->current_frame_index];','   vk->readback.home_last_index = vk->context->current_frame_index;\n   staging  = &vk->readback.staging[vk->context->current_frame_index];');
  t=replaceOnce(t,'   staging = &vk->readback.staging[vk->context->current_frame_index];','   staging = &vk->readback.staging[(vk->flags & VK_FLAG_READBACK_STREAMED) ? vk->readback.home_last_index : vk->context->current_frame_index];');
  return replaceOnce(t,'         buffer += 3 * (vk->vp.height - 1) * vk->vp.width;','         /* Current-frame recording waits for this submitted copy, not a future swapchain turn. */\n#ifdef HAVE_THREADS\n         slock_lock(vk->context->queue_lock);\n#endif\n         vkQueueWaitIdle(vk->context->queue);\n#ifdef HAVE_THREADS\n         slock_unlock(vk->context->queue_lock);\n#endif\n         buffer += 3 * (vk->vp.height - 1) * vk->vp.width;');
});
await edit('gfx/drivers/gl2.c',t=>{
  t=replaceOnce(t,'   if (  video_gpu_record\n      && recording_st->enable)','   if (video_gpu_record)');
  return replaceOnce(t,'   gl2_renderchain_bind_pbo(\n         gl->pbo_readback[gl->pbo_readback_index++]);\n   gl->pbo_readback_index &= 3;\n\n   /* 4 frames back, we can readback. */\n   gl->pbo_readback_valid[gl->pbo_readback_index] = true;','   /* Consume the current frame in the dedicated recording host. */\n   gl->pbo_readback_index = 0;\n   gl2_renderchain_bind_pbo(gl->pbo_readback[0]);\n   gl->pbo_readback_valid[0] = true;');
});
await writeFile(join(root,'home-host-adapter.json'),JSON.stringify({format:1,version:'home-host-1',inputSource:'released+cfb12043',loopbackOnly:true,changes:changed,lowDelay:'upstream FFmpeg immediate mux/IO flush + current-frame single GPU PBO',peripherals:'bounded libretro pointer/mouse/keyboard/lightgun packets; no virtual HID engine'},null,2));
console.log(JSON.stringify({hostAdapter:'home-host-1',changed:changed.map(x=>x.path)}));
