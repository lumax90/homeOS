/* Original isolated GPU diagnostic backends; no emulator/game/content code. SPDX-License-Identifier: MIT */
#if defined(HOME_DIAG_D3D11)
#include <libretro_d3d11.h>
#define HOME_CONTEXT RETRO_HW_CONTEXT_D3D11
#define HOME_CONTEXT_MAJOR 11
#define HOME_CONTEXT_MINOR 0
static const struct retro_hw_render_interface_d3d11 *dx;
static ID3D11Texture2D *dx_image;static ID3D11RenderTargetView *dx_target;static ID3D11ShaderResourceView *dx_view;
static void reset_context(void)
{
   environment(RETRO_ENVIRONMENT_GET_HW_RENDER_INTERFACE,&dx);
   D3D11_TEXTURE2D_DESC desc={0};desc.Width=400;desc.Height=480;desc.MipLevels=1;desc.ArraySize=1;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_DEFAULT;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
   if(FAILED(dx->device->lpVtbl->CreateTexture2D(dx->device,&desc,0,&dx_image)))abort();
   if(FAILED(dx->device->lpVtbl->CreateRenderTargetView(dx->device,(ID3D11Resource*)dx_image,0,&dx_target)))abort();
   if(FAILED(dx->device->lpVtbl->CreateShaderResourceView(dx->device,(ID3D11Resource*)dx_image,0,&dx_view)))abort();
   fprintf(stderr,"HOME ORIGINAL GPU FIXTURE renderer=D3D11 frontend hardware device featurelevel=%x\n",dx->featureLevel);
}
static void paint(bool pressed){const float color[4]={pressed?1.0f:0.0f,pressed?0.0f:1.0f,0,1};dx->context->lpVtbl->ClearRenderTargetView(dx->context,dx_target,color);dx->context->lpVtbl->PSSetShaderResources(dx->context,0,1,&dx_view);}
static void destroy_context(void){if(dx_view)dx_view->lpVtbl->Release(dx_view);if(dx_target)dx_target->lpVtbl->Release(dx_target);if(dx_image)dx_image->lpVtbl->Release(dx_image);dx_view=0;dx_target=0;dx_image=0;}
#elif defined(HOME_DIAG_VULKAN)
#define VK_NO_PROTOTYPES
#include <libretro_vulkan.h>
#define HOME_CONTEXT RETRO_HW_CONTEXT_VULKAN
#define HOME_CONTEXT_MAJOR 1
#define HOME_CONTEXT_MINOR 0
static const struct retro_hw_render_interface_vulkan *vk;
static VkImage vk_image;static VkDeviceMemory vk_memory;static VkCommandPool vk_pool;static VkCommandBuffer vk_command;static struct retro_vulkan_image vk_output;static bool vk_first=true;
#define VK_FN(name) static PFN_##name name
VK_FN(vkCreateImage);VK_FN(vkGetImageMemoryRequirements);VK_FN(vkAllocateMemory);VK_FN(vkBindImageMemory);VK_FN(vkCreateImageView);VK_FN(vkCreateCommandPool);VK_FN(vkAllocateCommandBuffers);VK_FN(vkResetCommandPool);VK_FN(vkBeginCommandBuffer);VK_FN(vkEndCommandBuffer);VK_FN(vkCmdPipelineBarrier);VK_FN(vkCmdClearColorImage);VK_FN(vkQueueSubmit);VK_FN(vkQueueWaitIdle);VK_FN(vkDestroyImage);VK_FN(vkDestroyImageView);VK_FN(vkDestroyCommandPool);VK_FN(vkFreeMemory);
#define VK_LOAD(name) name=(PFN_##name)vk->get_device_proc_addr(vk->device,#name)
static void require_vk(VkResult result){if(result!=VK_SUCCESS){fprintf(stderr,"Original GPU fixture Vulkan result=%d\n",result);abort();}}
static void reset_context(void)
{
   environment(RETRO_ENVIRONMENT_GET_HW_RENDER_INTERFACE,&vk);if(!vk)abort();
   VK_LOAD(vkCreateImage);VK_LOAD(vkGetImageMemoryRequirements);VK_LOAD(vkAllocateMemory);VK_LOAD(vkBindImageMemory);VK_LOAD(vkCreateImageView);VK_LOAD(vkCreateCommandPool);VK_LOAD(vkAllocateCommandBuffers);VK_LOAD(vkResetCommandPool);VK_LOAD(vkBeginCommandBuffer);VK_LOAD(vkEndCommandBuffer);VK_LOAD(vkCmdPipelineBarrier);VK_LOAD(vkCmdClearColorImage);VK_LOAD(vkQueueSubmit);VK_LOAD(vkQueueWaitIdle);VK_LOAD(vkDestroyImage);VK_LOAD(vkDestroyImageView);VK_LOAD(vkDestroyCommandPool);VK_LOAD(vkFreeMemory);
   VkPhysicalDeviceProperties properties;((PFN_vkGetPhysicalDeviceProperties)vk->get_instance_proc_addr(vk->instance,"vkGetPhysicalDeviceProperties"))(vk->gpu,&properties);fprintf(stderr,"HOME ORIGINAL GPU FIXTURE renderer=Vulkan %s\n",properties.deviceName);
   VkImageCreateInfo info={VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};info.flags=VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT;info.imageType=VK_IMAGE_TYPE_2D;info.format=VK_FORMAT_R8G8B8A8_UNORM;info.extent=(VkExtent3D){400,480,1};info.mipLevels=1;info.arrayLayers=1;info.samples=VK_SAMPLE_COUNT_1_BIT;info.tiling=VK_IMAGE_TILING_OPTIMAL;info.usage=VK_IMAGE_USAGE_TRANSFER_DST_BIT|VK_IMAGE_USAGE_TRANSFER_SRC_BIT|VK_IMAGE_USAGE_SAMPLED_BIT;info.sharingMode=VK_SHARING_MODE_EXCLUSIVE;require_vk(vkCreateImage(vk->device,&info,0,&vk_image));
   VkMemoryRequirements requirement;vkGetImageMemoryRequirements(vk->device,vk_image,&requirement);VkPhysicalDeviceMemoryProperties memory;((PFN_vkGetPhysicalDeviceMemoryProperties)vk->get_instance_proc_addr(vk->instance,"vkGetPhysicalDeviceMemoryProperties"))(vk->gpu,&memory);
   VkMemoryAllocateInfo allocation={VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};allocation.allocationSize=requirement.size;unsigned index;for(index=0;index<memory.memoryTypeCount;index++)if((requirement.memoryTypeBits&(1u<<index))&&(memory.memoryTypes[index].propertyFlags&VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT))break;if(index==memory.memoryTypeCount)abort();allocation.memoryTypeIndex=index;require_vk(vkAllocateMemory(vk->device,&allocation,0,&vk_memory));require_vk(vkBindImageMemory(vk->device,vk_image,vk_memory,0));
   vk_output.create_info=(VkImageViewCreateInfo){VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO};vk_output.create_info.image=vk_image;vk_output.create_info.viewType=VK_IMAGE_VIEW_TYPE_2D;vk_output.create_info.format=info.format;vk_output.create_info.subresourceRange=(VkImageSubresourceRange){VK_IMAGE_ASPECT_COLOR_BIT,0,1,0,1};require_vk(vkCreateImageView(vk->device,&vk_output.create_info,0,&vk_output.image_view));vk_output.image_layout=VK_IMAGE_LAYOUT_GENERAL;
   VkCommandPoolCreateInfo pool={VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};pool.queueFamilyIndex=vk->queue_index;pool.flags=VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;require_vk(vkCreateCommandPool(vk->device,&pool,0,&vk_pool));VkCommandBufferAllocateInfo command={VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};command.commandPool=vk_pool;command.level=VK_COMMAND_BUFFER_LEVEL_PRIMARY;command.commandBufferCount=1;require_vk(vkAllocateCommandBuffers(vk->device,&command,&vk_command));vk_first=true;
}
static void paint(bool pressed)
{
   vk->lock_queue(vk->handle);require_vk(vkQueueWaitIdle(vk->queue));require_vk(vkResetCommandPool(vk->device,vk_pool,0));VkCommandBufferBeginInfo begin={VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};begin.flags=VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;require_vk(vkBeginCommandBuffer(vk_command,&begin));
   VkImageMemoryBarrier barrier={VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER};barrier.srcAccessMask=vk_first?0:VK_ACCESS_SHADER_READ_BIT;barrier.dstAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT;barrier.oldLayout=vk_first?VK_IMAGE_LAYOUT_UNDEFINED:VK_IMAGE_LAYOUT_GENERAL;barrier.newLayout=VK_IMAGE_LAYOUT_GENERAL;barrier.srcQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;barrier.dstQueueFamilyIndex=VK_QUEUE_FAMILY_IGNORED;barrier.image=vk_image;barrier.subresourceRange=vk_output.create_info.subresourceRange;
   vkCmdPipelineBarrier(vk_command,VK_PIPELINE_STAGE_ALL_COMMANDS_BIT,VK_PIPELINE_STAGE_TRANSFER_BIT,0,0,0,0,0,1,&barrier);VkClearColorValue color={{pressed?1.0f:0.0f,pressed?0.0f:1.0f,0,1}};vkCmdClearColorImage(vk_command,vk_image,VK_IMAGE_LAYOUT_GENERAL,&color,1,&barrier.subresourceRange);barrier.oldLayout=VK_IMAGE_LAYOUT_GENERAL;barrier.srcAccessMask=VK_ACCESS_TRANSFER_WRITE_BIT;barrier.dstAccessMask=VK_ACCESS_SHADER_READ_BIT;vkCmdPipelineBarrier(vk_command,VK_PIPELINE_STAGE_TRANSFER_BIT,VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,0,0,0,0,0,1,&barrier);require_vk(vkEndCommandBuffer(vk_command));VkSubmitInfo submit={VK_STRUCTURE_TYPE_SUBMIT_INFO};submit.commandBufferCount=1;submit.pCommandBuffers=&vk_command;require_vk(vkQueueSubmit(vk->queue,1,&submit,VK_NULL_HANDLE));require_vk(vkQueueWaitIdle(vk->queue));vk->unlock_queue(vk->handle);vk->set_image(vk->handle,&vk_output,0,0,VK_QUEUE_FAMILY_IGNORED);vk_first=false;
}
static void destroy_context(void){if(!vk)return;vk->lock_queue(vk->handle);vkQueueWaitIdle(vk->queue);vk->unlock_queue(vk->handle);vkDestroyCommandPool(vk->device,vk_pool,0);vkDestroyImageView(vk->device,vk_output.image_view,0);vkDestroyImage(vk->device,vk_image,0);vkFreeMemory(vk->device,vk_memory,0);vk=0;}
#else
#if defined(HOME_DIAG_GL)
#define HOME_CONTEXT RETRO_HW_CONTEXT_OPENGL
#define HOME_CONTEXT_MAJOR 2
#define HOME_CONTEXT_MINOR 1
#else
#define HOME_CONTEXT RETRO_HW_CONTEXT_OPENGL_CORE
#define HOME_CONTEXT_MAJOR 3
#define HOME_CONTEXT_MINOR 3
#endif
typedef void (*BindFramebuffer)(unsigned,unsigned);typedef void (*Viewport)(int,int,int,int);typedef void (*ClearColor)(float,float,float,float);typedef void (*Clear)(unsigned);typedef const unsigned char* (*GetString)(unsigned);
static BindFramebuffer bind_framebuffer;static Viewport viewport;static ClearColor clear_color;static Clear clear;
static void reset_context(void){bind_framebuffer=(BindFramebuffer)render.get_proc_address("glBindFramebuffer");viewport=(Viewport)render.get_proc_address("glViewport");clear_color=(ClearColor)render.get_proc_address("glClearColor");clear=(Clear)render.get_proc_address("glClear");GetString get_string=(GetString)render.get_proc_address("glGetString");fprintf(stderr,"HOME ORIGINAL GPU FIXTURE vendor=%s renderer=%s version=%s\n",get_string(0x1F00),get_string(0x1F01),get_string(0x1F02));}
static void paint(bool pressed){bind_framebuffer(0x8D40,(unsigned)render.get_current_framebuffer());viewport(0,0,400,480);clear_color(pressed?1.0f:0.0f,pressed?0.0f:1.0f,0.0f,1.0f);clear(0x00004000);}
static void destroy_context(void){}
#endif
