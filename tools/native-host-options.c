/* Home command facade over RetroArch's existing core option manager.
 * Included only in the pinned reviewed frontend build. SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <stdarg.h>
static bool home_append(char *output,size_t size,size_t *at,const char *format,...)
{
   int count;va_list args;if(*at>=size)return false;va_start(args,format);
   count=vsnprintf(output+*at,size-*at,format,args);va_end(args);
   if(count<0||(size_t)count>=size-*at)return false;*at+=(size_t)count;return true;
}
static bool home_json_string(char *output, size_t size, size_t *at, const char *text)
{
   const unsigned char *ptr=(const unsigned char*)(text ? text : "");
   if (*at+3>=size) return false; output[(*at)++]='"';
   while(*ptr)
   {
      unsigned char c=*ptr++; if (*at+7>=size) return false;
      if(c=='"'||c=='\\'){output[(*at)++]='\\';output[(*at)++]=(char)c;}
      else if(c<32){*at+=snprintf(output+*at,size-*at,"\\u%04x",c);}
      else output[(*at)++]=(char)c;
   }
   output[(*at)++]='"';output[*at]=0;return true;
}
bool command_home_timing(command_t *cmd,const char *arg)
{
   char reply[128];double fps=video_state_get_ptr()->av_info.timing.fps;
   size_t length=snprintf(reply,sizeof(reply),"HOME_TIMING %.8f\n",fps);
   cmd->replier(cmd,reply,length);return true;
}
bool command_home_state_size(command_t *cmd,const char *arg)
{
   char reply[96];size_t length=snprintf(reply,sizeof(reply),"HOME_STATE_SIZE %llu\n",(unsigned long long)core_serialize_size());
   cmd->replier(cmd,reply,length);return true;
}
bool command_home_device_get(command_t *cmd,const char *arg)
{
   unsigned port,i;char extra;char reply[15000];size_t at=0;
   rarch_system_info_t *system=&runloop_state_get_ptr()->system;
   if(sscanf(arg,"%u %c",&port,&extra)!=1||port>=4)return false;
   if(!home_append(reply,sizeof(reply),&at,"{\"port\":%u,\"device\":%u,\"items\":[",port,input_config_get_device(port)))return false;
   if(port<system->ports.size&&system->ports.data[port].num_types)
   {for(i=0;i<system->ports.data[port].num_types&&i<64;i++){if(i&&!home_append(reply,sizeof(reply),&at,","))return false;if(!home_append(reply,sizeof(reply),&at,"{\"id\":%u,\"name\":",system->ports.data[port].types[i].id))return false;if(!home_json_string(reply,sizeof(reply),&at,system->ports.data[port].types[i].desc))return false;if(!home_append(reply,sizeof(reply),&at,"}"))return false;}}
   else if(!home_append(reply,sizeof(reply),&at,"{\"id\":1,\"name\":\"RetroPad\"}"))return false;
   if(!home_append(reply,sizeof(reply),&at,"]}\n"))return false;cmd->replier(cmd,reply,at);return true;
}
bool command_home_device_set(command_t *cmd,const char *arg)
{
   unsigned port,device,i;char extra;char reply[4];bool allowed=false;
   rarch_system_info_t *system=&runloop_state_get_ptr()->system;
   if(sscanf(arg,"%u %u %c",&port,&device,&extra)==2&&port<4)
   {
      if(device==0||device==1)allowed=true;
      if(port<system->ports.size)for(i=0;i<system->ports.data[port].num_types;i++)if(system->ports.data[port].types[i].id==device)allowed=true;
      if(allowed){retro_ctx_controller_info_t pad;pad.port=port;pad.device=device;input_config_set_device(port,device);core_set_controller_port_device(&pad);}
   }
   strlcpy(reply,allowed?"OK\n":"NO\n",sizeof(reply));cmd->replier(cmd,reply,3);return allowed;
}
bool command_home_option_get(command_t *cmd, const char *arg)
{
   unsigned index; char extra; char value[15000]; char reply[31000]; size_t at=0,i,length;
   core_option_manager_t *manager=runloop_state_get_ptr()->core_options;
   unsigned count=manager ? (unsigned)manager->size : 0;
   if(sscanf(arg,"%u %c",&index,&extra)!=1||index>=count)
   {length=snprintf(reply,sizeof(reply),"HOME_OPTION %u -\n",count);cmd->replier(cmd,reply,length);return true;}
   { struct core_option *option=&manager->opts[index];
     if(!home_append(value,sizeof(value),&at,"{\"index\":%u,\"key\":",index))return false;
     if(!home_json_string(value,sizeof(value),&at,option->key))return false;
     if(!home_append(value,sizeof(value),&at,",\"label\":")||!home_json_string(value,sizeof(value),&at,option->desc))return false;
     if(!home_append(value,sizeof(value),&at,",\"info\":")||!home_json_string(value,sizeof(value),&at,option->info))return false;
     if(!home_append(value,sizeof(value),&at,",\"value\":")||!home_json_string(value,sizeof(value),&at,core_option_manager_get_val(manager,index)))return false;
     if(!home_append(value,sizeof(value),&at,",\"values\":["))return false;
     for(i=0;option->vals&&i<option->vals->size&&i<128;i++)
     {if(i&&!home_append(value,sizeof(value),&at,","))return false;if(!home_json_string(value,sizeof(value),&at,option->vals->elems[i].data))return false;}
     if(at+3>=sizeof(value))return false;value[at++]=']';value[at++]='}';value[at]=0;
   }
   length=snprintf(reply,sizeof(reply),"HOME_OPTION %u ",count);
   for(i=0;i<at;i++)length+=snprintf(reply+length,sizeof(reply)-length,"%02x",(unsigned char)value[i]);
   reply[length++]='\n';cmd->replier(cmd,reply,length);return true;
}
bool command_home_option_set(command_t *cmd,const char *arg)
{
   unsigned index,value;char extra;char reply[4];bool success=false;
   core_option_manager_t *manager=runloop_state_get_ptr()->core_options;
   if(manager&&sscanf(arg,"%u %u %c",&index,&value,&extra)==2&&index<manager->size&&manager->opts[index].vals&&value<manager->opts[index].vals->size)
   {core_option_manager_set_val(manager,index,value,false);success=true;}
   strlcpy(reply,success?"OK\n":"NO\n",sizeof(reply));cmd->replier(cmd,reply,3);return success;
}
