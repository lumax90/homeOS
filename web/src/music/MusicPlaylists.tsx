import {useState} from 'react';
import {Button} from '@heroui/react';
import {useMutation,useQuery,useQueryClient} from '@tanstack/react-query';
import {ArrowLeft,ListMusic,Play,Plus,Trash2} from 'lucide-react';
import {clock,mutate,request,type MediaItem,type Playlist} from '../api';
import {runtimeText,translate as t,useUiLanguage} from '../i18n';

export function MusicPlaylists({queue,onPlay,onQueue,notify}:{queue:MediaItem[];onPlay:(items:MediaItem[],index?:number)=>void;onQueue:(items:MediaItem[])=>void;notify:(message:string)=>void}){
  useUiLanguage();const cache=useQueryClient(),[name,setName]=useState(''),[selected,setSelected]=useState<Playlist|null>(null);
  const lists=useQuery({queryKey:['playlists'],queryFn:({signal})=>request<Playlist[]>('/playlists',{signal})});
  const content=useQuery({queryKey:['playlist-items',selected?.id],queryFn:({signal})=>request<MediaItem[]>(`/playlists/${selected!.id}/items`,{signal}),enabled:!!selected});
  const tracks=content.data?.filter(item=>item.kind==='audio')??[],audioQueue=queue.filter(item=>item.kind==='audio');
  const save=useMutation({mutationFn:()=>mutate('/playlists',{name,mediaIds:audioQueue.map(item=>item.id)}),onSuccess:()=>{setName('');void cache.invalidateQueries({queryKey:['playlists']});notify(t('Çalma listesi kaydedildi.'));}});
  const remove=useMutation({mutationFn:(id:string)=>mutate(`/playlists/${id}`,undefined,'DELETE'),onSuccess:()=>{setSelected(null);void cache.invalidateQueries({queryKey:['playlists']});}});
  return <section className="music-playlists">{selected?<><Button variant="ghost" onPress={()=>setSelected(null)}><ArrowLeft size={17}/> Listelerim</Button><div className="music-section-heading"><div><p className="eyebrow">ÇALMA LİSTESİ</p><h2>{selected.name}</h2><p>{tracks.length} parça</p></div><div className="music-inline-actions"><Button variant="primary" onPress={()=>onPlay(tracks)} isDisabled={!tracks.length}><Play size={17} fill="currentColor"/> Çal</Button><Button variant="ghost" onPress={()=>onQueue(tracks)} isDisabled={!tracks.length}><Plus size={17}/> Sıraya ekle</Button><Button isIconOnly variant="ghost" aria-label="Çalma listesini sil" isDisabled={remove.isPending} onPress={()=>remove.mutate(selected.id)}><Trash2 size={17}/></Button></div></div>
    <div className="music-playlist-tracks">{tracks.map((track,index)=><button className="music-queue-row" key={`${track.id}:${index}`} disabled={!track.available} onClick={()=>onPlay(tracks,index)}><span>{index+1}</span><strong>{track.title}</strong><small>{clock(track.durationSeconds??0)}</small><Play size={16}/></button>)}</div>{content.isPending&&<p className="music-inline-note">Liste yükleniyor…</p>}{!content.isPending&&!content.error&&!tracks.length&&<p className="music-empty-note">Bu listede erişilebilir müzik yok.</p>}
  </>:<><div className="music-section-heading"><div><h2>Kendi listelerin.</h2><p>Sıranı kur, adını ver, yeniden dinle.</p></div><ListMusic size={28}/></div><form className="music-playlist-create" onSubmit={event=>{event.preventDefault();save.mutate();}}><label><span>Mevcut müzik sırasını kaydet</span><input aria-label="Çalma listesi adı" value={name} maxLength={100} required placeholder="Liste adı" onChange={event=>setName(event.target.value)}/></label><Button type="submit" variant="secondary" isDisabled={!audioQueue.length||!name.trim()||save.isPending}><Plus size={17}/> Kaydet · {audioQueue.length}</Button></form>
    <div className="music-playlist-grid">{lists.data?.map(list=><button className="music-playlist-card" key={list.id} onClick={()=>setSelected(list)}><div><ListMusic size={44} strokeWidth={1}/></div><strong>{list.name}</strong><span>{list.count} öğe</span></button>)}</div>{!lists.isPending&&!lists.error&&!lists.data?.length&&<p className="music-empty-note">İlk çalma listen için şarkıları sıraya ekleyip yukarıdan kaydet.</p>}</>}
    {(lists.error||content.error||save.error||remove.error)&&<p className="form-error">{runtimeText((lists.error??content.error??save.error??remove.error)!.message)}</p>}
  </section>;
}
