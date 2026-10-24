import {useState} from 'react';
import {Button,Modal} from '@heroui/react';
import {useMutation,useQuery,useQueryClient} from '@tanstack/react-query';
import {CheckCircle2,Gamepad2,Globe2,Library,LoaderCircle,Monitor,RefreshCw,Settings2,ShieldCheck} from 'lucide-react';
import {Dialog} from '../components/Dialogs';
import {request,mutate} from '../api';
import {runtimeText,translate as t,useUiLanguage} from '../i18n';
import {useSession} from '../components/SessionContext';
import {NativeCoreSettings} from './NativeCoreSettings';
import {nativeDisplayPreferences,saveNativeDisplay,type NativeDisplayPreferences} from './nativePreferences';
import {nativeStreamingBackend,saveNativeStreamingBackend,parseStreamingBackend,type NativeStreamingStatus} from './nativeStreaming';
import './game-settings.css';
import './games-refresh.css';

export interface GamesLibraryOptions{revision:number;automaticMetadata:boolean;matchByName:boolean;coverRegion:string;refreshGeneration:number}
export interface GamesLibraryStatus{options:GamesLibraryOptions;total:number;complete:number;active:number;attention:number;failures:number;waiting:number;remaining:number;provider:string;keyRequired:boolean}
export function GamesSettingsButton(){
  const [open,setOpen]=useState(false);return <><Button isIconOnly variant="ghost" className="games-settings-trigger" aria-label="Oyunlar ayarları" onPress={()=>setOpen(true)}><Settings2 size={21}/></Button>{open&&<GamesSettingsDialog onClose={()=>setOpen(false)}/>}</>;
}
function GamesSettingsDialog({onClose}:{onClose:()=>void}){
  useUiLanguage();const [section,setSection]=useState<'library'|'emulation'|'systems'>('library');
  return <Dialog open onClose={onClose} title="Oyunlar ayarları" className="games-settings-container" backdropClassName="games-settings-backdrop"><div className="games-settings-panel games-surface">
    <header className="games-modal-header"><div className="games-modal-symbol"><Gamepad2 size={23}/></div><div><p className="eyebrow">HOME · GAMES</p><Modal.Heading>Oyunlar ayarları</Modal.Heading><p>Koleksiyonun, görünümün ve emülatörlerin.</p></div></header>
    <div className="games-settings-layout"><nav className="games-settings-nav" aria-label="Oyunlar ayarı bölümleri">{([{id:'library',label:'Kütüphane ve bilgiler',icon:Library},{id:'emulation',label:'Emülasyon ayarları',icon:Monitor},{id:'systems',label:'Konsollar ve çekirdekler',icon:Gamepad2}] as const).map(item=><Button key={item.id} variant="ghost" aria-pressed={section===item.id} onPress={()=>setSection(item.id)}><item.icon size={18}/><span>{t(item.label)}</span></Button>)}</nav>
      <div className="games-settings-body">{section==='library'?<LibraryPreferences/>:section==='emulation'?<EmulationPreferences/>:<NativeCoreSettings/>}</div>
    </div>
  </div></Dialog>;
}
function LibraryPreferences(){
  const cache=useQueryClient(),session=useSession(),editable=session.role==='admin'||session.canAcquire;
  const info=useQuery({queryKey:['games','library-settings'],queryFn:()=>request<GamesLibraryStatus>('/games/library/settings'),refetchInterval:query=>query.state.data?.active||query.state.data?.options.automaticMetadata&&query.state.data.waiting?5000:false});
  const change=useMutation({mutationFn:(value:Partial<GamesLibraryOptions>)=>mutate('/games/library/settings',{...info.data!.options,...value},'PUT'),onSuccess:()=>cache.invalidateQueries({queryKey:['games','library-settings']})});
  const refresh=useMutation({mutationFn:()=>mutate('/games/library/refresh',{revision:info.data!.options.revision}),onSuccess:()=>cache.invalidateQueries({queryKey:['games']})});
  const value=info.data;
  return <section className="games-preferences-section"><header><h3>Koleksiyonun kendini tamamlasın.</h3><p>Oyun bilgileri ve kapaklar yeni ve mevcut ROM’ların için arka planda eşleştirilir.</p></header>
    {info.isPending&&<p className="games-inline-status"><LoaderCircle className="spin" size={17}/> Ayarlar yükleniyor…</p>}
    {value&&<><div className="games-automation-progress"><div><Globe2 size={20}/><strong>{value.complete} / {value.total}</strong><span>Oyun bilgisi ve kapak hazır</span></div><progress aria-label="Koleksiyon eşleştirme ilerlemesi" value={value.complete} max={Math.max(1,value.total)}/><p>{value.active?t('Arka planda işleniyor: {{v0}}',{v0:value.active}):value.options.automaticMetadata&&value.remaining?t('Kalan oyunlar sırayla işlenecek.'):t('Koleksiyon eşleştirme durumu güncel.')}{value.attention>0?' '+t('Eşleşmesi belirsiz: {{v0}}',{v0:value.attention}):''}</p></div>
      <label className="games-toggle-row"><span><strong>Oyun bilgilerini ve kapakları otomatik getir</strong><small>Yüzlerce oyun da olsa tek tek seçim yapman gerekmez. Yalnız belirsiz eşleşmeler incelemeye kalır.</small></span><input type="checkbox" checked={value.options.automaticMetadata} disabled={!editable||change.isPending} onChange={event=>change.mutate({automaticMetadata:event.target.checked})}/></label>
      <label className="games-toggle-row"><span><strong>Kesin ad ve konsol eşleşmelerini kullan</strong><small>Dosya özeti bulunamazsa aynı oyun adı ve konsolu kullan. Yakın isimli oyunlar otomatik seçilmez.</small></span><input type="checkbox" checked={value.options.matchByName} disabled={!editable||change.isPending} onChange={event=>change.mutate({matchByName:event.target.checked})}/></label>
      <label className="games-field">Kapak bölgesi<select aria-label="Kapak bölgesi" value={value.options.coverRegion} disabled={!editable||change.isPending} onChange={event=>change.mutate({coverRegion:event.target.value})}><option value="auto">ROM bölgesine göre · otomatik</option><option value="world">Dünya</option><option value="us">Amerika</option><option value="eu">Avrupa</option><option value="jp">Japonya</option></select></label>
      <div className="games-preferences-footer"><span><CheckCircle2 size={16}/> Hasheous + libretro · {t('API anahtarı gerekmez')}</span><Button variant="secondary" isDisabled={!editable||!value.options.automaticMetadata||refresh.isPending||value.active>0} onPress={()=>refresh.mutate()}><RefreshCw size={15}/> Koleksiyonu yeniden tara</Button></div>
    </>}{(info.error||change.error||refresh.error)&&<p className="form-error">{runtimeText((info.error??change.error??refresh.error)!.message)}</p>}
  </section>;
}
function EmulationPreferences(){
  const session=useSession(),cache=useQueryClient();const [display,setDisplay]=useState(()=>nativeDisplayPreferences(session.userId??'')),[notice,setNotice]=useState('');
  const [streaming,setStreaming]=useState(()=>nativeStreamingBackend(session.userId??''));
  const backends=useQuery({queryKey:['games','streaming-backends'],queryFn:()=>request<NativeStreamingStatus>('/games/native/backends')});
  const status=useQuery({queryKey:['games','native-availability'],queryFn:()=>request<{available:boolean;enabled:boolean;canConfigure:boolean;prepared:boolean;version:string;reason:string|null}>('/games/native/status')});
  const options=useQuery({queryKey:['games','emulation-options'],queryFn:()=>request<{revision:number;quality:string;audioFilter:boolean;solarSensor:number}>('/games/native/options')});
  const quality=useMutation({mutationFn:(value:string)=>mutate('/games/native/options',{...options.data,quality:value},'PUT'),onSuccess:()=>cache.invalidateQueries({queryKey:['games','emulation-options']})});
  const toggle=useMutation({mutationFn:()=>mutate('/games/native/setup',{enabled:!status.data?.enabled,expectedVersion:status.data?.version}),onSuccess:()=>cache.invalidateQueries({queryKey:['games']})});
  function save(){saveNativeDisplay(session.userId??'',display);saveNativeStreamingBackend(session.userId??'',streaming);setNotice(t('Genel tercihler kaydedildi.'));}
  return <section className="games-preferences-section"><header><h3>Genel emülasyon tercihleri</h3><p>Bu tarayıcıdaki profilinin varsayılan görünümü. Oyuna özel tercihler önceliklidir.</p></header>
    <div className="games-form-grid"><label className="games-field">Görüntü boyutu<select aria-label="Görüntü boyutu" value={display.fit} onChange={event=>setDisplay(old=>({...old,fit:event.target.value as NativeDisplayPreferences['fit']}))}><option value="contain">Orijinal oran · otomatik</option><option value="integer">Tam kat piksel ölçekleme</option><option value="fill">Ekranı doldur</option></select></label><label className="games-field">Oyun ses düzeyi<input aria-label="Oyun ses düzeyi" type="range" min="0" max="100" value={display.volume} onChange={event=>setDisplay(old=>({...old,volume:Number(event.target.value)}))}/><small>{display.volume}%</small></label></div>
    <label className="games-toggle-row"><span><strong>Görüntüyü yumuşat</strong><small>Ölçeklenen görüntüyü yumuşatır; iç çözünürlük ayarı her emülatöre aittir.</small></span><input type="checkbox" checked={display.smooth} onChange={event=>setDisplay(old=>({...old,smooth:event.target.checked}))}/></label>
    <details className="games-advanced-disclosure"><summary>Gelişmiş yayın tercihi</summary><label className="games-field">Yayın altyapısı<select aria-label="Yayın altyapısı" value={streaming} onChange={event=>setStreaming(parseStreamingBackend(event.target.value))}><option value="standard">Standart · varsayılan</option><option value="selkies" disabled={!backends.data?.items.find(item=>item.id==='selkies')?.available}>Selkies · deneysel</option></select><small>Bu tarayıcı ve profil için hatırlanır. Sonraki oyun açılışında uygulanır; her oturumda tek yayın yöntemi çalışır.</small></label>{backends.data?.items.find(item=>item.id==='selkies')?.reason&&<p className="field-hint">{runtimeText(backends.data.items.find(item=>item.id==='selkies')!.reason!)}</p>}{streaming==='selkies'&&<p className="field-hint">İlk Selkies dilimi: GB, GBC, GBA, NES, SNES, Mega Drive ve PlayStation. Görüntü ve ses Selkies üzerinden; kumanda ve kayıtlar Home tarafından yönetilir.</p>}{backends.error&&<p className="form-error">{runtimeText(backends.error.message)}</p>}</details>
    <Button variant="secondary" onPress={save}>Genel tercihleri kaydet</Button>{notice&&<p className="games-inline-status" role="status"><CheckCircle2 size={16}/>{notice}</p>}
    <div className="games-preferences-divider"><h4>Sunucuda oynatma</h4><p>Yayın kalitesi ve emülasyon yürütücüsü. Oyun içi ayarlardan her oyun için özel tercih kaydedebilirsin.</p></div>
    {options.data&&<label className="games-field">Yayın kalitesi<select aria-label="Yayın kalitesi" value={options.data.quality} disabled={!status.data?.canConfigure||quality.isPending} onChange={event=>quality.mutate(event.target.value)}><option value="auto">Otomatik · düşük gecikme</option><option value="quality">Daha net görüntü</option><option value="low">Daha az ağ kullanımı</option></select><small>Sunucu varsayılanını değiştirmeden önce açık oyundan çık.</small></label>}
    <div className="games-runtime-status"><ShieldCheck size={18}/><span>{status.data?.available?t('Emülasyon hazır'):t('Sunucu emülasyonu')}{status.data?.reason&&<small>{runtimeText(status.data.reason)}</small>}</span></div>
    {status.data?.canConfigure&&<details className="games-advanced-disclosure"><summary>Sunucu emülasyonunu yönet</summary><p className="field-hint">Emülatör sunucuda çalışır. Bu cihaz görüntü, ses ve kumanda bağlantısını kullanır.</p><Button variant="ghost" isDisabled={toggle.isPending||!status.data.enabled&&!status.data.prepared} onPress={()=>toggle.mutate()}>{status.data.enabled?t('Sunucu emülasyonunu kapat'):t('Sunucu emülasyonunu etkinleştir')}</Button></details>}
    {(status.error||options.error||quality.error||toggle.error)&&<p className="form-error">{runtimeText((status.error??options.error??quality.error??toggle.error)!.message)}</p>}
  </section>;
}
