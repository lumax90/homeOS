import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Button, Modal } from '@heroui/react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { FileUp, LoaderCircle, ShieldCheck } from 'lucide-react';
import { request, mutate, bytes } from '../api';
import { runtimeText, translate as t, useUiLanguage } from '../i18n';
import { Dialog } from '../components/Dialogs';
import type { Game } from './model';
import type {NativeSystemInfo} from './NativeCoreSettings';
import {visibleNativeSystems} from './nativeSelection';
import {defaultRomEntry,romEntryFiles,detectRomFiles,romUploadBody,type RomProbeResult} from './nativeRomImport';
import {RomGameDetails} from './RomGameDetails';

export function NativeRomImportDialog({ open, onClose, onAdded }: { open: boolean; onClose: () => void; onAdded: (id: string) => void }) {
  useUiLanguage();const [files,setFiles]=useState<File[]>([]),[entry,setEntry]=useState(''),[manualSystem,setManualSystem]=useState(''),[authorized,setAuthorized]=useState(false),[advanced,setAdvanced]=useState(false);
  const [probe,setProbe]=useState<RomProbeResult|null>(null),[detecting,setDetecting]=useState(false),[probeError,setProbeError]=useState(''),fileInput=useRef<HTMLInputElement|null>(null);
  const system=manualSystem||probe?.systemId||'';
  const systems=useQuery({queryKey:['games','systems'],queryFn:()=>request<NativeSystemInfo[]>('/games/native/systems'),enabled:open});
  const definition=systems.data?.find(item=>item.id===system);const file=files.find(file=>file.name===entry);
  const visibleSystems=visibleNativeSystems(systems.data??[],advanced,system);
  const controller = useRef<AbortController | null>(null), client = useQueryClient();
  useEffect(() => () => { controller.current?.abort(); }, []);
  useEffect(()=>{
    setProbe(null);setProbeError('');setDetecting(false);if(!open||!files.length||!entry)return;
    let active=true;const abort=new AbortController();setDetecting(true);
    void detectRomFiles(files,entry,abort.signal,body=>request<RomProbeResult>('/games/roms/detect',{method:'POST',body:JSON.stringify(body),signal:abort.signal})).then(result=>{if(active)setProbe(result);}).catch(error=>{if(active)setProbeError(error instanceof Error?error.message:String(error));}).finally(()=>{if(active)setDetecting(false);});
    return()=>{active=false;abort.abort();};
  },[open,files,entry]);
  const importRom = useMutation({
    mutationFn: () => {
      if (!file || !authorized) throw new Error(t('Kullanma hakkın olan bir ROM seç ve onayla.'));
      if(!definition||files.reduce((total,file)=>total+file.size,0)>definition.maximumBytes||files.length>128)throw new Error(t('ROM dosyaları sistemin boyut veya dosya sayısı sınırını aşıyor.'));
      if(probe?.missingFiles.length)throw new Error(t('CUE ve istediği tüm BIN dosyalarını birlikte seç.'));
      if(system==='ps1'&&/\.bin$/i.test(file.name))throw new Error(t('PS1 için BIN dosyasını tek başına ekleme; CUE ve BIN dosyalarını birlikte seç.'));
      controller.current = new AbortController();
      const headers={'X-Home-Rom-Name':encodeURIComponent(file.name),'X-Home-Rom-System':system};
      const upload=romUploadBody(files,entry);return request<{id:string}>('/games/roms',{method:'POST',headers:upload.multipart?headers:{...headers,'Content-Type':'application/octet-stream'},body:upload.body,signal:controller.current.signal});
    },
    onSuccess:async result=>{await client.invalidateQueries({queryKey:['games']});setFiles([]);setEntry('');setManualSystem('');setAuthorized(false);if(fileInput.current)fileInput.current.value='';onAdded(result.id);onClose();},
  });
  function close(){controller.current?.abort();void client.invalidateQueries({queryKey:['games']});setFiles([]);setEntry('');setManualSystem('');setAuthorized(false);setProbe(null);setProbeError('');if(fileInput.current)fileInput.current.value='';importRom.reset();onClose();}
  function submit(event: FormEvent) { event.preventDefault(); if (!importRom.isPending) importRom.mutate(); }
  return <Dialog open={open} onClose={close} title="ROM ekle" className="games-edit-container" backdropClassName="games-settings-backdrop"><div className="games-edit-form games-surface">
    <div className="dialog-symbol"><FileUp size={27} /></div><p className="eyebrow">KENDİ OYUN DOSYAN</p><Modal.Heading className="dialog-title">ROM’unu ekle.</Modal.Heading>
    <p className="muted dialog-description">Önce oyun dosyalarını seç. Home sistemi algılamaya çalışır; belirsiz disklerde konsolu sen seçersin. Özgün dosyalar değiştirilmez.</p>
    <form className="home-form" onSubmit={submit}>
      <label className="field"><span>ROM dosyaları</span><input ref={fileInput} type="file" multiple disabled={importRom.isPending} onChange={event=>{const selected=Array.from(event.target.files??[]),next=defaultRomEntry(selected);setProbe(null);setProbeError('');setDetecting(!!selected.length&&!!next);setFiles(selected);setEntry(next);importRom.reset();}}/>{files.length>0&&<span className="field-hint">{files.length} {t('dosya')} · {bytes(files.reduce((total,file)=>total+file.size,0))}</span>}<small>PS1 CUE/BIN için iki dosyayı aynı anda seç. CUE başlangıçtır; BIN oyun verisidir.</small></label>
      {files.length>1&&<label className="field">Disk başlangıç dosyası<select aria-label="Disk başlangıç dosyası" value={entry} disabled={importRom.isPending} onChange={event=>{setProbe(null);setProbeError('');setDetecting(!!event.target.value);setEntry(event.target.value);importRom.reset();}}><option value="">Disk başlangıç dosyası seç.</option>{romEntryFiles(files).map(file=><option key={file.name} value={file.name}>{file.name}</option>)}</select><small>CUE/BIN veya M3U için tüm ilgili dosyaları aynı anda seç. Dosyalar aynı klasörde olmalı.</small></label>}
      {detecting&&<p className="form-note"><LoaderCircle size={15} className="spin"/> Oyun sistemi algılanıyor…</p>}
      <label className="field">Oyun sistemi<select aria-label="Oyun sistemi" value={manualSystem||'auto'} disabled={importRom.isPending} onChange={event=>{setManualSystem(event.target.value==='auto'?'':event.target.value);importRom.reset();}}><option value="auto">{t('Otomatik algıla')}{probe?.systemId?' · '+(systems.data?.find(item=>item.id===probe.systemId)?.name??probe.systemId):''}</option>{visibleSystems.map(item=><option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
      <label><input type="checkbox" checked={advanced} disabled={importRom.isPending} onChange={event=>setAdvanced(event.target.checked)}/> Diğer sistemleri göster</label>
      {probe?.systemId&&!manualSystem&&<p className="rom-detection-status" role="status"><ShieldCheck size={16}/>{t('Algılandı: {{v0}}',{v0:definition?.name??probe.systemId})}</p>}
      {probe&&!system&&<div className="rom-detection-choice" role="status"><p>{probe.reason==='unsupported'?t('Oyun sistemi algılanamadı. Uygun sistemi elle seçebilirsin.'):t('Bu dosya birkaç konsolda kullanılabiliyor. Devam etmek için konsolu bir kez seç.')}</p><div>{systems.data?.filter(item=>probe.candidates.includes(item.id)).slice(0,8).map(item=><Button key={item.id} variant="secondary" onPress={()=>setManualSystem(item.id)}>{item.name}</Button>)}</div></div>}
      {probe?.missingFiles.length? <p className="form-error">{t('CUE dosyasının istediği eksik dosyalar: {{v0}}',{v0:probe.missingFiles.join(', ')})}</p>:null}
      {probeError&&<p className="form-error">{runtimeText(probeError)}</p>}
      {definition&&<p className="form-note">{definition.extensions.map(value=>'.'+value).join(', ')} · {bytes(definition.maximumBytes)} {t('sınır')}<br/>{definition.reason&&runtimeText(definition.reason)}</p>}
      <label className="rom-rights-confirmation"><input type="checkbox" checked={authorized} disabled={importRom.isPending} onChange={event => setAuthorized(event.target.checked)} /><span>Bu ROM’u kullanma hakkım var.</span></label>
      <p className="muted">Home ROM veya BIOS indirmez. ROM eklemek oyunu başlatmaz; oynamak için çekirdeğin Home entegrasyonu ve gerekli BIOS dosyaları hazır olmalı.</p>
      {importRom.error && <p className="form-error" role="alert">{runtimeText(importRom.error.message)}</p>}
      {systems.error&&<p className="form-error">{runtimeText(systems.error.message)}</p>}<Button type="submit" className="primary-button form-submit" isDisabled={!file||!authorized||!definition||importRom.isPending||!manualSystem&&(detecting||!!probeError)||!!probe?.missingFiles.length}>{importRom.isPending?<LoaderCircle size={17} className="spin"/>:<FileUp size={17}/>} {importRom.isPending?t('ROM doğrulanıyor…'):detecting&&!manualSystem?t('Oyun sistemi algılanıyor…'):!system&&!!file?t('Önce konsolu seç'):t('ROM’u kütüphaneme ekle')}</Button>
      <Button variant="ghost" onPress={close}>{importRom.isPending ? 'Aktarımı iptal et' : 'Vazgeç'}</Button>
    </form></div></Dialog>;
}

export function NativeGameDetails({ game, onBack, onOpen }: { game: Game; onBack: () => void; onOpen?: (path: string) => void }) {
  const [covers,setCovers]=useState(false);return <><RomGameDetails game={game} onBack={onBack} onOpen={onOpen} onCover={()=>setCovers(true)}/><RomCoverDialog game={game} open={covers} onClose={()=>setCovers(false)}/></>;
}
function RomCoverDialog({game,open,onClose}:{game:Game;open:boolean;onClose:()=>void}){
  const cache=useQueryClient(),[query,setQuery]=useState(game.title),[results,setResults]=useState<Array<{name:string;title:string;sourceUrl:string}>>([]);
  const search=useMutation({mutationFn:()=>request<typeof results>(`/games/${game.id}/covers?query=${encodeURIComponent(query)}`),onSuccess:setResults});
  const pick=useMutation({mutationFn:(name:string)=>mutate(`/games/${game.id}/cover`,{name}),onSuccess:async()=>{await cache.invalidateQueries({queryKey:['games']});onClose();}});
  const upload=useMutation({mutationFn:(file:File)=>request(`/games/${game.id}/cover/upload`,{method:'POST',headers:{'Content-Type':'image/png'},body:file}),onSuccess:async()=>{await cache.invalidateQueries({queryKey:['games']});onClose();}});
  return <Dialog open={open} onClose={onClose} title="Oyun kapağı" className="game-options-container" backdropClassName="games-settings-backdrop"><div className="games-edit-form games-surface"><Modal.Heading className="dialog-title">Oyun kapağı</Modal.Heading><p className="muted">Kapaklar otomatik tamamlanır. Farklı bir bölge veya özel görsel istersen buradan değiştirebilirsin.</p><form className="home-form" onSubmit={event=>{event.preventDefault();search.mutate();}}><label className="games-field">Oyun adı<input value={query} onChange={event=>setQuery(event.target.value)} maxLength={160}/></label><Button type="submit" isDisabled={search.isPending||query.trim().length<3}>{search.isPending?'Kapaklar aranıyor…':'Kapak ara'}</Button></form><div className="rom-cover-candidates">{results.map(cover=><button key={cover.name} disabled={pick.isPending} onClick={()=>pick.mutate(cover.name)}><img src={cover.sourceUrl} alt={cover.title} loading="lazy" referrerPolicy="no-referrer"/><span>{cover.title}</span></button>)}</div>{(search.error||pick.error||upload.error)&&<p className="form-error">{runtimeText((search.error??pick.error??upload.error)!.message)}</p>}<label className="games-field">Kendi kapağını yükle<input type="file" accept="image/png" disabled={upload.isPending} onChange={event=>{const file=event.target.files?.[0];if(file)upload.mutate(file);}}/><small>PNG · en fazla 4 MiB</small></label><p className="form-note">Kapaklar ilgili oyun geliştiricisi/yayıncısının görselleridir; ROM veya BIOS indirilmez.</p></div></Dialog>;
}
