import {useState}from'react';
import {Button,Modal}from'@heroui/react';
import {useMutation,useQuery,useQueryClient}from'@tanstack/react-query';
import {ArrowDownToLine,RefreshCw}from'lucide-react';
import {appUrl,bytes,date,mutate,request}from'../api';
import {approvalFor,updatePhase,type ContainerUpdateCandidate,type ManagedUpdateStatus}from'../containerUpdates';
import {Dialog}from'./Dialogs';

export function UpdateAvailableBadge({onOpen}:{onOpen:()=>void}){
  const status=useQuery({queryKey:['container-updates'],queryFn:()=>request<ManagedUpdateStatus>('/operations/container-updates'),refetchInterval:60000});
  if(!status.data?.supported||!status.data.availability?.candidate)return null;
  return <Button size="sm" variant="ghost" onPress={onOpen} aria-label="Yeni Home sürümü mevcut"><ArrowDownToLine size={15}/>Güncelleme mevcut</Button>;
}
export function ContainerUpdates(){
  const client=useQueryClient();
  const status=useQuery({queryKey:['container-updates'],queryFn:()=>request<ManagedUpdateStatus>('/operations/container-updates'),refetchInterval:15000});
  const [confirm,setConfirm]=useState<{kind:'install';candidate:ContainerUpdateCandidate}|{kind:'automatic'}|null>(null);
  const refresh=()=>client.invalidateQueries({queryKey:['container-updates']});
  const settings=useMutation({mutationFn:(input:{enabled:boolean;automaticInstall:boolean})=>mutate('/operations/container-updates',input,'PUT'),onSuccess:refresh});
  const check=useMutation({mutationFn:()=>mutate('/operations/container-updates/check'),onSuccess:refresh});
  const apply=useMutation({mutationFn:(candidate:ContainerUpdateCandidate)=>mutate('/operations/container-updates/apply',approvalFor(candidate)),onSuccess:()=>{setConfirm(null);void refresh();}});
  const cancel=useMutation({mutationFn:()=>mutate('/operations/container-updates/apply',undefined,'DELETE'),onSuccess:refresh});
  const data=status.data;if(!data?.supported)return null;
  const available=data.availability;const candidate=available?.candidate;const pending=!!available?.approvedDigest;
  const error=status.error||settings.error||check.error||apply.error||cancel.error;
  return <div className="restore-pending">
    <strong>Home güncellemeleri</strong><p>Yeni sürümler arka planda kontrol edilir. Varsayılan olarak indirme ve kurulum için onayın gerekir.</p>
    {!data.configured?<p className="field-hint">Güncelleme yardımcısını CasaOS kurulumuna bir kez ekle. Veri ve medya bağlamalarını koru.</p>:<>
      {!data.helperConnected&&<p className="field-hint">Güncelleme yardımcısı henüz bağlanmadı. Kurulum başlatılmayacak.</p>}
      <div className="content-actions"><Button variant="ghost" onPress={()=>check.mutate()} isDisabled={!data.enabled||check.isPending||pending}><RefreshCw size={15}/>{check.isPending?'Sürüm kontrol ediliyor…':'Güncellemeleri kontrol et'}</Button><Button variant="ghost" onPress={()=>settings.mutate({enabled:!data.enabled,automaticInstall:!!data.automaticInstall})} isDisabled={settings.isPending||pending||data.preparing}>{data.enabled?'Otomatik kontrolü duraklat':'Otomatik kontrolü aç'}</Button></div>
      <p className="field-hint">{data.automaticInstall?'Home boşken otomatik kurulum açık.':'Kurulum için kullanıcı onayı gerekir.'}</p>
      <Button variant="ghost" size="sm" onPress={()=>data.automaticInstall?settings.mutate({enabled:data.enabled,automaticInstall:false}):setConfirm({kind:'automatic'})} isDisabled={settings.isPending||pending||data.preparing}>{data.automaticInstall?'Kullanıcı onaylı kuruluma geç':'Boştayken otomatik kurulumu aç'}</Button>
      {available?.checkedAt&&<p className="field-hint">Son kontrol: {date(available.checkedAt)}</p>}
    </>}
    {candidate&&<div><strong>Home {candidate.version} mevcut</strong>
      <p className="field-hint">{candidate.estimatedDownloadBytes===null?'İmaj boyutu:':'Tahmini yeni indirme:'} {bytes(candidate.estimatedDownloadBytes??candidate.totalBytes)}. Yerel önbelleğe göre değişebilir.</p>
      <a href={`https://github.com/lumax90/homeOS/commit/${candidate.revision}`} target="_blank" rel="noreferrer">Sürüm değişiklikleri</a>
      <p className="field-hint">Medya, ayarlar ve kayıtlar mevcut veri klasöründe kalır. Aktif oyun, oynatma ve indirme bitmeden kurulum yapılmaz.</p>
      <p role="status">{updatePhase(available?.phase??'available')}</p>
      <div className="content-actions">{!pending?<Button className="primary-button" onPress={()=>setConfirm({kind:'install',candidate})} isDisabled={!data.enabled||!data.helperConnected||apply.isPending}>İndir ve güncelle</Button>:<Button variant="ghost" onPress={()=>cancel.mutate()} isDisabled={data.preparing||available?.phase==='applying'||cancel.isPending}>Güncellemeyi iptal et</Button>}</div>
    </div>}
    {data.last&&<p className="field-hint">{date(data.last.preparedAt)} · <a href={appUrl(`/api/operations/backups/${data.last.backupId}`)} download>Güncelleme öncesi yedeği indir</a></p>}
    {(data.error||available?.error)&&<p className="form-error">Güncelleme bilgisi veya kurulum durumu incelenmeli. Çalışan sürüm ve yedekler korunuyor.</p>}
    {error&&<p className="form-error" role="alert">{error.message}</p>}
    <Dialog open={confirm!==null} onClose={()=>setConfirm(null)} title="Güncelleme onayı"><div className="dialog-content">
      <Modal.Heading className="dialog-title">{confirm?.kind==='automatic'?'Otomatik kurulum açılsın mı?':'Güncelleme kurulsun mu?'}</Modal.Heading>
      <p>{confirm?.kind==='automatic'?'Gelecekteki doğrulanan sürümler Home boşken yedek alınıp onay sorulmadan kurulacak. Bu tercihi istediğin zaman kapatabilirsin.':'Seçtiğin sürüm indirilecek. Home kullanılmadığında uygulama yedeği alınacak ve kısa bir yeniden başlatmayla kurulacak.'}</p>
      {confirm?.kind==='install'&&<p>Home {confirm.candidate.version} · <code>{confirm.candidate.revision.slice(0,12)}</code></p>}
      <div className="content-actions"><Button variant="ghost" onPress={()=>setConfirm(null)}>Vazgeç</Button><Button className="primary-button" isDisabled={apply.isPending||settings.isPending} onPress={()=>{if(confirm?.kind==='install')apply.mutate(confirm.candidate);else if(confirm?.kind==='automatic'){settings.mutate({enabled:true,automaticInstall:true});setConfirm(null);}}}>Onayla</Button></div>
      {apply.error&&<p className="form-error" role="alert">{apply.error.message}</p>}
    </div></Dialog>
  </div>;
}
