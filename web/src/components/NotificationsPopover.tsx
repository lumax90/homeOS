import {useEffect,useRef,useState} from 'react';
import {Button,Popover} from '@heroui/react';
import {useMutation,useQuery,useQueryClient} from '@tanstack/react-query';
import {Bell,Check,CheckCheck,Download,LoaderCircle,Settings2,Sparkles,X} from 'lucide-react';
import {date,mutate,request,type NotificationItem} from '../api';
import {runtimeText,translate as t,useUiLanguage} from '../i18n';
import {useControllerCommands} from '../controller/react';
import './notifications.css';

export function NotificationBell({onSettings}:{onSettings?:()=>void}){
  useUiLanguage();const [open,setOpen]=useState(()=>new URLSearchParams(location.hash.slice(1)).has('notifications')),[unreadOnly,setUnreadOnly]=useState(false);
  const cache=useQueryClient(),panel=useRef<HTMLDivElement>(null),trigger=useRef<HTMLButtonElement>(null);
  const notifications=useQuery({queryKey:['notifications'],queryFn:()=>request<NotificationItem[]>('/notifications'),refetchInterval:20000});
  const read=useMutation({mutationFn:(id:string|null)=>mutate(id?`/notifications/${id}/read`:'/notifications/read'),onSuccess:()=>cache.invalidateQueries({queryKey:['notifications']})});
  const unread=notifications.data?.filter(item=>!item.readAt).length??0,items=(notifications.data??[]).filter(item=>!unreadOnly||!item.readAt);
  function close(){setOpen(false);requestAnimationFrame(()=>trigger.current?.focus({preventScroll:true}));}
  useControllerCommands(panel,action=>{if(action==='back'||action==='menu'){close();return true;}return false;});
  useEffect(()=>{if(!('serviceWorker' in navigator))return;const received=(event:MessageEvent)=>{if(event.data?.type==='home.notification.open'){setOpen(true);void cache.invalidateQueries({queryKey:['notifications']});}};navigator.serviceWorker.addEventListener('message',received);return()=>navigator.serviceWorker.removeEventListener('message',received);},[cache]);
  return <Popover isOpen={open} onOpenChange={setOpen}>
    <Button ref={trigger} isIconOnly variant="ghost" className="notification-button" aria-label={t('Bildirimler')+(unread?' · '+t('{{v0}} okunmamış',{v0:unread}):'')}><Bell size={20}/>{unread>0&&<span className="notification-dot" aria-hidden="true"/>}</Button>
    <Popover.Content ref={panel} className="home-notifications-popover" placement="bottom end" offset={12} containerPadding={14} isNonModal data-controller-scope="notifications">
      <Popover.Dialog className="home-notifications-panel" aria-label="Bildirimler">
        <header className="home-notifications-header"><div><Popover.Heading>Bildirimler</Popover.Heading><p>{unread?t('{{v0}} okunmamış',{v0:unread}):t('Tüm bildirimlerin okundu.')}</p></div><Button isIconOnly variant="ghost" aria-label="Bildirimleri kapat" onPress={close}><X size={18}/></Button></header>
        <div className="home-notifications-toolbar"><div aria-label="Bildirim filtresi"><Button variant="ghost" size="sm" aria-pressed={!unreadOnly} onPress={()=>setUnreadOnly(false)}>Tümü</Button><Button variant="ghost" size="sm" aria-pressed={unreadOnly} onPress={()=>setUnreadOnly(true)}>Okunmamış</Button></div><Button isIconOnly variant="ghost" size="sm" aria-label="Tümünü okundu işaretle" isDisabled={unread===0||read.isPending} onPress={()=>read.mutate(null)}><CheckCheck size={18}/></Button></div>
        <div className="home-notifications-list" tabIndex={0} aria-label="Bildirim listesi">
          {notifications.isPending?<div className="home-notifications-empty"><LoaderCircle className="spin" size={24}/><p>Bildirimler yükleniyor…</p></div>:items.length?items.map(item=><article key={item.id} className={`home-notification-item${item.readAt?'':' is-unread'}`}><div className="home-notification-symbol">{item.kind.includes('download')?<Download size={18}/>:item.kind==='request'?<Sparkles size={18}/>:<Bell size={17}/>}</div><div className="home-notification-copy"><strong>{runtimeText(item.title)}</strong><p>{runtimeText(item.message)}</p><time dateTime={item.createdAt}>{date(item.createdAt)}</time></div>{!item.readAt&&<Button isIconOnly variant="ghost" size="sm" aria-label={t('Okundu işaretle: {{v0}}',{v0:item.title})} isDisabled={read.isPending} onPress={()=>read.mutate(item.id)}><Check size={15}/></Button>}</article>):<div className="home-notifications-empty"><CheckCheck size={28}/><strong>{unreadOnly?t('Hepsini yakaladın.'):t('Henüz bildirim yok.')}</strong><p>Yeni bir haber olduğunda burada göreceksin.</p></div>}
        </div>
        {(notifications.error||read.error)&&<p className="home-notifications-error" role="alert">{runtimeText((notifications.error??read.error)!.message)}</p>}
        {onSettings&&<footer className="home-notifications-footer"><Button variant="ghost" onPress={()=>{setOpen(false);requestAnimationFrame(onSettings);}}><Settings2 size={16}/> Bildirim ayarları</Button></footer>}
      </Popover.Dialog>
    </Popover.Content>
  </Popover>;
}
