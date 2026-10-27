import {useEffect,useState}from'react';
import {Button,Modal}from'@heroui/react';
import {useMutation,useQueryClient}from'@tanstack/react-query';
import {LoaderCircle,Trash2}from'lucide-react';
import {mutate}from'../api';
import {Dialog}from'../components/Dialogs';
import {useSession}from'../components/SessionContext';
import {runtimeText}from'../i18n';
import type{Game}from'./model';

export function GameUninstallAction({game,onRemoved}:{game:Game;onRemoved:()=>void}){
  const session=useSession(),cache=useQueryClient();const[open,setOpen]=useState(false),[requested,setRequested]=useState(false);
  const busy=['queued','running','applying'].includes(game.uninstallStatus??'');
  const remove=useMutation({mutationFn:()=>mutate(`/games/${game.id}/uninstall`,{confirmed:true,keepSaves:true}),
    onSuccess:()=>{setOpen(false);setRequested(true);void cache.invalidateQueries({queryKey:['games']});}});
  useEffect(()=>{if(requested&&game.uninstallStatus==='completed'&&!game.state.inLibrary){setRequested(false);void cache.invalidateQueries({queryKey:['games']});onRemoved();}},[requested,game.uninstallStatus,game.state.inLibrary,cache,onRemoved]);
  if(session.role!=='admin'&&!session.canAcquire)return null;
  if(game.installed===false&&!game.state.inLibrary&&!busy)return null;
  return <>
    <Button variant="ghost" size="sm" onPress={()=>setOpen(true)} isDisabled={busy||remove.isPending}>{busy?<LoaderCircle size={15} className="spin"/>:<Trash2 size={15}/>} {busy?'Kaldırılıyor…':'Oyunu kaldır'}</Button>
    {game.uninstallStatus==='failed'&&<p className="form-error">{runtimeText(game.uninstallMessage)}</p>}
    <Dialog open={open} onClose={()=>{if(!remove.isPending)setOpen(false);}} title="Oyunu kaldır" className="games-edit-container" backdropClassName="games-settings-backdrop"><div className="games-edit-form games-surface">
      <Modal.Heading>Oyun kaldırılsın mı?</Modal.Heading><p><strong>{game.title}</strong></p>
      <p>Bu profilden kaldırılır. Home'un yönettiği oyun kopyası başka bir profilde kullanılmıyorsa silinir; paylaşılan ve uygulamayla gelen paketler korunur.</p>
      <p><strong>Kayıtların, ilerlemen, kapak ve elle düzenlediğin bilgiler korunur.</strong> Özgün koleksiyon dosyan değiştirilmez. Aktif oyundan önce çıkmalısın.</p>
      {remove.error&&<p className="form-error" role="alert">{runtimeText(remove.error.message)}</p>}
      <footer><Button variant="ghost" isDisabled={remove.isPending} onPress={()=>setOpen(false)}>Vazgeç</Button><Button variant="danger" isDisabled={remove.isPending} onPress={()=>remove.mutate()}>{remove.isPending?'Kaldırma hazırlanıyor…':'Kayıtları koruyarak kaldır'}</Button></footer>
    </div></Dialog>
  </>;
}
