import { useEffect,useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation,useQuery,useQueryClient } from '@tanstack/react-query';
import { Cast,Link2,Radio } from 'lucide-react';
import { mutate,request,randomId,type MediaItem } from '../api';
import { useSession } from './SessionContext';
import { ProductJobs } from './ProductJobs';
import { hasAirplay,loadCast,useReceiverPlayback,type Receiver } from './ReceiverPlayback';

interface Renderer {id:string;name:string;address:string;protocols:string}
export function ReceiverPanel({item,position=0,subtitleId,onPauseLocal}:{item:MediaItem;position?:number;subtitleId?:string;onPauseLocal?:()=>void}){
 const session=useSession();const client=useQueryClient();const runtime=useReceiverPlayback();const [open,setOpen]=useState(false);const [target,setTarget]=useState('');const [base,setBase]=useState('');const [castReady,setCastReady]=useState(false);
 const devices=useQuery({queryKey:['render-devices'],enabled:open,queryFn:()=>request<Renderer[]>('/receivers/devices')});const settings=useQuery({queryKey:['receiver-settings'],enabled:open,queryFn:()=>request<{publicBaseUrl:string|null}>('/receivers/settings')});
 const existing=useQuery({queryKey:['receiver-sessions'],enabled:open,queryFn:()=>request<Receiver[]>('/receivers/sessions'),refetchInterval:open?10000:false});
 useEffect(()=>{let alive=true;if(open)void loadCast().then(()=>{if(alive)setCastReady(true);}).catch(()=>{});return()=>{alive=false;};},[open]);
 useEffect(()=>{if(settings.data)setBase(settings.data.publicBaseUrl??'');},[settings.data]);
 const save=useMutation({mutationFn:()=>mutate('/receivers/settings',{publicBaseUrl:base||null},'PUT'),onSuccess:()=>client.invalidateQueries({queryKey:['receiver-settings']})});
 const discover=useMutation({mutationFn:()=>mutate('/receivers/discover'),onSuccess:()=>client.invalidateQueries({queryKey:['render-devices']})});
 const prepare=useMutation({mutationFn:()=>mutate(`/receivers/media/${item.id}/prepare`),onSuccess:()=>client.invalidateQueries({queryKey:['product-jobs']})});
 const start=useMutation({mutationFn:(kind:Receiver['kind'])=>runtime.start(item,kind,target,position,subtitleId),onSuccess:()=>onPauseLocal?.()});
 const stop=useMutation({mutationFn:(value:Receiver)=>mutate(`/receivers/${value.id}/control`,{action:'stop',commandId:randomId(),expectedRevision:value.revision}),onSettled:()=>client.invalidateQueries({queryKey:['receiver-sessions']})});
 const unavailable=start.isPending||!!runtime.receiver;
 return <section className="personal-content"><Button variant="ghost" onPress={()=>setOpen(value=>!value)}><Cast size={16}/> TV veya hoparlöre gönder</Button>{open&&<div className="home-form">{session.role==='admin'&&<><label className="field"><span>Alıcıların ulaşacağı Home adresi</span><input value={base} onChange={event=>setBase(event.target.value)} placeholder="http://192.168.1.10:5080"/></label><Button variant="ghost" onPress={()=>save.mutate()} isDisabled={save.isPending}>Ağ adresini kaydet</Button></>}
 <div className="content-actions"><Button variant="ghost" onPress={()=>start.mutate('cast')} isDisabled={!castReady||unavailable}><Cast size={15}/> Google Cast</Button>{hasAirplay&&<Button variant="ghost" onPress={()=>start.mutate('airplay')} isDisabled={unavailable}><Radio size={15}/> AirPlay akışını hazırla</Button>}{session.role==='admin'&&<Button variant="ghost" onPress={()=>discover.mutate()} isDisabled={discover.isPending}>DLNA cihazlarını bul</Button>}</div>
 <label className="field"><span>DLNA alıcısı</span><select value={target} onChange={event=>setTarget(event.target.value)}><option value="">Cihaz seç</option>{devices.data?.map(device=><option key={device.id} value={device.id}>{device.name} · {device.address}</option>)}</select></label><Button variant="ghost" onPress={()=>start.mutate('dlna')} isDisabled={!target||unavailable}>Seçili DLNA alıcısında aç</Button>
 {item.kind==='video'&&<><Button variant="ghost" onPress={()=>prepare.mutate()} isDisabled={prepare.isPending}>HLS desteklemeyen alıcı için MP4 hazırla</Button><ProductJobs kind="receiver-prepare" scope={item.id}/></>}
 {runtime.receiver&&<p className="field-hint" role="status">{runtime.receiver.name} açık. Sayfa altındaki alıcı kumandası gezinirken de kullanılabilir.</p>}
 {existing.data?.filter(value=>value.id!==runtime.receiver?.id).map(value=><div className="session-row" key={value.id}><strong>{value.name}</strong>{value.kind==='dlna'&&<Button variant="ghost" isDisabled={!!runtime.receiver} onPress={()=>runtime.attach(value)}>Kumandaya bağlan</Button>}<Button variant="ghost" onPress={()=>stop.mutate(value)} isDisabled={stop.isPending}>Oturumu bitir</Button></div>)}
 {[devices.error,settings.error,save.error,discover.error,prepare.error,start.error,existing.error,stop.error].filter(Boolean).map((error,index)=><p className="form-error" key={index}>{error?.message}</p>)}</div>}</section>;
}
export function DeviceLinkApprovalPanel(){
 const [code,setCode]=useState('');const [preview,setPreview]=useState('');const details=useQuery({queryKey:['device-link-info',preview],enabled:preview.length===8,queryFn:()=>request<{deviceName:string}>(`/device-links/${preview}`)});const approve=useMutation({mutationFn:()=>mutate('/device-links/approve',{code:preview}),onSuccess:()=>{setCode('');setPreview('');}});
 return <section className="home-form"><h3><Link2 size={17}/> Yeni cihazı bağla</h3><p className="field-hint">Yeni cihazın giriş ekranındaki sekiz haneli kodu gir.</p><input aria-label="Cihaz bağlantı kodu" value={code} inputMode="numeric" maxLength={8} onChange={event=>setCode(event.target.value.replace(/\D/g,''))}/><Button variant="ghost" onPress={()=>setPreview(code)} isDisabled={code.length!==8}>Cihazı göster</Button>{details.data&&<><p>{details.data.deviceName}</p><Button className="secondary-button" onPress={()=>approve.mutate()} isDisabled={approve.isPending}>Bu cihazı hesabıma bağla</Button></>}{approve.isSuccess&&<p role="status">Cihaz bağlantısı onaylandı.</p>}{(details.error||approve.error)&&<p className="form-error">{details.error?.message||approve.error?.message}</p>}</section>;
}
export function DeviceLinkLogin({onLinked}:{onLinked:()=>void}){
 const [challenge,setChallenge]=useState<{id:string;secret:string;code:string;expiresAt:string}|null>(null);const [error,setError]=useState('');
 const start=useMutation({mutationFn:()=>mutate<typeof challenge>('/device-links',{deviceName:navigator.userAgent.slice(0,120)}),onSuccess:setChallenge});
 useEffect(()=>{if(!challenge)return;let stopped=false;const timer=setInterval(()=>{void mutate<{approved:boolean}>('/device-links/poll',{id:challenge.id,secret:challenge.secret}).then(value=>{if(value.approved&&!stopped){stopped=true;clearInterval(timer);onLinked();}}).catch(cause=>{clearInterval(timer);setError((cause as Error).message);});},5000);return()=>{stopped=true;clearInterval(timer);};},[challenge,onLinked]);
  return <section><Button variant="ghost" onPress={()=>{setError('');start.mutate();}} isDisabled={start.isPending}>Diğer cihazımdan bağlan</Button>{challenge&&<div className="request-card"><strong style={{fontSize:28,letterSpacing:5}}>{challenge.code}</strong><p className="field-hint">Açık Home hesabında Ayarlar → Cihaz bağlama bölümüne bu kodu gir. Kod beş dakika geçerlidir.</p></div>}{(error||start.error)&&<p className="form-error">{error||start.error?.message}</p>}</section>;
}
