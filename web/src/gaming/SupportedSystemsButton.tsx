import{useRef,useState}from'react';
import{Button,Popover}from'@heroui/react';
import{useQuery}from'@tanstack/react-query';
import{CheckCircle2,ChevronDown,FlaskConical,Gamepad2,LoaderCircle,MinusCircle,X}from'lucide-react';
import{request}from'../api';
import{runtimeText,translate as t,useUiLanguage}from'../i18n';
import{useControllerCommands}from'../controller/react';
import'./supported-systems.css';

interface SupportedSystem{id:string;name:string;state:'supported'|'experimental'|'unsupported';installed:boolean;ready:boolean;core:string|null;backends:string[];reason:string|null}
interface SupportView{serverPlatform:string;items:SupportedSystem[];note:string}
export function SupportedSystemsButton(){
  useUiLanguage();const[open,setOpen]=useState(false),trigger=useRef<HTMLButtonElement>(null),panel=useRef<HTMLDivElement>(null);
  const support=useQuery({queryKey:['games','supported-systems'],queryFn:()=>request<SupportView>('/games/supported-systems'),enabled:open,staleTime:20_000});
  function close(){setOpen(false);requestAnimationFrame(()=>trigger.current?.focus({preventScroll:true}));}
  useControllerCommands(panel,action=>{if(action==='back'||action==='menu'){close();return true;}return false;});
  return <Popover isOpen={open} onOpenChange={setOpen}>
    <Button ref={trigger} variant="ghost" className="games-supported-trigger" aria-label="Desteklenen cihazlar"><Gamepad2 size={18}/><span>Desteklenen cihazlar</span><ChevronDown size={14}/></Button>
    <Popover.Content ref={panel} className="games-supported-popover" placement="bottom end" offset={10} containerPadding={12} isNonModal data-controller-scope="supported-systems">
      <Popover.Dialog className="games-supported-panel games-surface" aria-label="Desteklenen cihazlar">
        <header><div><Popover.Heading>Desteklenen oyun sistemleri</Popover.Heading><p>Bu Home sunucusunun güncel kapsamı.</p></div><Button isIconOnly variant="ghost" size="sm" aria-label="Destek listesini kapat" onPress={close}><X size={17}/></Button></header>
        {support.isPending&&<p className="games-inline-status"><LoaderCircle className="spin" size={16}/> Destek bilgisi yükleniyor…</p>}
        {support.error&&<p className="form-error" role="alert">{runtimeText(support.error.message)}</p>}
        {support.data&&<><div className="games-supported-list" tabIndex={0} aria-label="Oyun sistemi destek listesi">
          {([{state:'supported',label:'Desteklenen',icon:CheckCircle2},{state:'experimental',label:'Deneysel / hazırlık aşamasında',icon:FlaskConical},{state:'unsupported',label:'Henüz desteklenmeyen',icon:MinusCircle}]as const).map(group=><section key={group.state} className={'games-supported-group is-'+group.state}><h3><group.icon size={15}/>{t(group.label)}</h3><ul>{support.data.items.filter(item=>item.state===group.state).map(item=><li key={item.id}><div><strong>{item.name}</strong>{item.reason&&item.state!=='unsupported'&&<small>{runtimeText(item.reason)}</small>}</div><span className={'games-support-state'+(item.ready?' is-ready':'')}>{item.state==='unsupported'?t('Hazır destek yok'):item.ready?t('Bu sunucuda hazır'):item.installed?t('Doğrulama / ayar gerekli'):t('Paket gerekli')}</span></li>)}</ul></section>)}
        </div><footer><p>{runtimeText(support.data.note)}</p><small>{support.data.serverPlatform}</small></footer></>}
      </Popover.Dialog>
    </Popover.Content>
  </Popover>;
}
