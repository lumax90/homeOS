import{useState}from'react';
import{Button,Modal}from'@heroui/react';
import{useMutation,useQueryClient}from'@tanstack/react-query';
import{Dialog}from'../components/Dialogs';
import{mutate}from'../api';
import{runtimeText}from'../i18n';
import type{Game}from'./model';
import{useSession}from'../components/SessionContext';

export function PspSystemCorrectionAction({game}:{game:Game}){
  const[open,setOpen]=useState(false),cache=useQueryClient(),session=useSession();
  const correction=useMutation({mutationFn:()=>mutate(`/games/${game.id}/system`,{system:'psp',confirmed:true},'PUT'),onSuccess:()=>{setOpen(false);void cache.invalidateQueries({queryKey:['games']});}});
  if(game.system!=='ps1'||session.role!=='admin')return null;
  return <><Button variant="ghost" size="sm" onPress={()=>setOpen(true)}>PSP olarak düzelt</Button>
    <Dialog open={open} onClose={()=>{if(!correction.isPending)setOpen(false);}} title="PSP sistem düzeltmesi" className="games-edit-container" backdropClassName="games-settings-backdrop"><div className="games-edit-form games-surface">
      <Modal.Heading>Bu disk aslında PSP oyunu mu?</Modal.Heading><p>Yalnız gerçek PSP ISO yapısı doğrulanırsa konsol düzeltilir. Geçerli PS1 oyunları değiştirilmez.</p>
      <p>Oyun kimliği, favoriler, elle düzenlediğin bilgiler ve eski kayıtlar korunur. PS1 anlık kaydı PSP çekirdeğine yüklenmez; yeni PSP kayıtları ayrı tutulur.</p>
      {correction.error&&<p className="form-error" role="alert">{runtimeText(correction.error.message)}</p>}
      <footer><Button variant="ghost" onPress={()=>setOpen(false)} isDisabled={correction.isPending}>Vazgeç</Button><Button onPress={()=>correction.mutate()} isDisabled={correction.isPending}>{correction.isPending?'Disk doğrulanıyor…':'Doğrula ve PSP olarak düzelt'}</Button></footer>
    </div></Dialog></>;
}
