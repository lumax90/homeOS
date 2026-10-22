import {useState} from 'react';
import {Button,Modal} from '@heroui/react';
import {useMutation,useQuery,useQueryClient} from '@tanstack/react-query';
import {ArrowLeft,CheckCircle2,Clock3,FileText,Heart,ImagePlus,Pencil,Play,ShieldCheck} from 'lucide-react';
import {appUrl,bytes,date,mutate,request} from '../api';
import {Dialog} from '../components/Dialogs';
import {runtimeText,translate as t,useUiLanguage} from '../i18n';
import type {Game} from './model';
import {hasGameCheckpoint} from './model';
import {GameSettingsButton} from './GameSettings';
import {GameCoreSelector} from './NativeCoreSettings';
import {RomMetadataCard} from './RomMetadataCard';
import {ConsoleArt,consoleStyle} from './ConsoleArt';
import {systemName} from './libraryModel';
import './games-refresh.css';

export function RomGameDetails({game,onBack,onOpen,onCover}:{game:Game;onBack:()=>void;onOpen?:(path:string)=>void;onCover:()=>void}){
  useUiLanguage();const cache=useQueryClient(),[editing,setEditing]=useState(false),[title,setTitle]=useState(game.title),[category,setCategory]=useState(game.category??'');
  const availability=useQuery({queryKey:['games','native-availability'],queryFn:()=>request<{available:boolean;reason:string|null}>('/games/native/status'),staleTime:30000});
  const favorite=useMutation({mutationFn:()=>mutate(`/games/${game.id}/preference`,{inLibrary:game.state.inLibrary,favorite:!game.state.favorite},'PUT'),onSuccess:()=>cache.invalidateQueries({queryKey:['games']})});
  const edit=useMutation({mutationFn:()=>mutate(`/games/${game.id}/presentation`,{title,category},'PUT'),onSuccess:async()=>{await cache.invalidateQueries({queryKey:['games']});setEditing(false);}});
  const cover=appUrl(`/api/games/${game.id}/artwork/cover.png`),canPlay=game.playable&&availability.data?.available&&!!onOpen;
  return <article className="rom-game-page games-surface" data-home-position-scope={`game:${game.id}`} style={consoleStyle(game.system)}>
    <div className="rom-game-topbar"><Button variant="ghost" onPress={onBack}><ArrowLeft size={18}/> Oyunlara dön</Button><span>{game.systemLabel??systemName(game.system)}</span></div>
    <section className="rom-game-hero"><div className="rom-game-atmosphere" aria-hidden="true">{game.hasArtwork&&<img src={cover} alt=""/>}<ConsoleArt system={game.system}/></div>
      <div className="rom-game-identity"><div className="rom-game-cover">{game.hasArtwork?<img src={cover} alt={game.title}/>:<div className="rom-game-cover-empty"><ConsoleArt system={game.system}/><span>{systemName(game.system)}</span></div>}</div>
        <div className="rom-game-copy"><p className="eyebrow">{game.systemLabel??systemName(game.system)}</p><h1>{game.title}</h1><div className="rom-game-facts">{game.metadata?.game.year&&<span>{game.metadata.game.year}</span>}{game.publisher&&<span>{game.publisher}</span>}{game.metadata?.game.regions?.length?<span>{game.metadata.game.regions.join(' · ')}</span>:null}<span>{bytes(game.sizeBytes)}</span></div>
          <div className="rom-game-actions"><Button className="play-button" isDisabled={!canPlay} onPress={()=>onOpen?.(`/games/${game.id}/play`)}><Play size={18} fill="currentColor"/>{hasGameCheckpoint(game)?t('Devam et'):t('Oyna')}</Button><GameSettingsButton game={game}/><Button isIconOnly variant="secondary" aria-label={game.state.favorite?t('Favorilerimden çıkar'):t('Favorilere ekle')} aria-pressed={game.state.favorite} isDisabled={favorite.isPending} onPress={()=>favorite.mutate()}><Heart size={19} fill={game.state.favorite?'currentColor':'none'}/></Button></div>
          {!canPlay&&(availability.data?.reason||game.blockReason)&&<p className="rom-game-blocked">{runtimeText(availability.data?.reason??game.blockReason)}</p>}
          <div className="rom-game-utility"><Button variant="ghost" size="sm" onPress={onCover}><ImagePlus size={15}/> Kapağı düzenle</Button><Button variant="ghost" size="sm" onPress={()=>{setTitle(game.title);setCategory(game.category??'');setEditing(true);}}><Pencil size={15}/> Bilgileri düzenle</Button></div>
        </div>
      </div>
    </section>
    <div className="rom-game-summary"><div><ShieldCheck size={19}/><span>{t('İlerlemen')}<strong>{game.state.hasSave?t('Kayıt mevcut'):t('Yeni başlangıç')}</strong></span></div><div><Clock3 size={19}/><span>{t('Son oynama')}<strong>{game.state.lastPlayedAt?date(game.state.lastPlayedAt):t('Henüz oynanmadı')}</strong></span></div><div><CheckCircle2 size={19}/><span>{t('Emülatör')}<strong>{game.coreName??game.coreId??'RetroArch'}</strong></span></div></div>
    {favorite.error&&<p className="form-error">{runtimeText(favorite.error.message)}</p>}
    <div className="rom-game-columns"><div><RomMetadataCard game={game}/></div><aside className="rom-game-management"><section className="rom-game-info-card"><h2><FileText size={17}/> Dosya ve kayıt</h2><dl><div><dt>Dosya boyutu</dt><dd>{bytes(game.sizeBytes)}</dd></div><div><dt>Konsol</dt><dd>{game.systemLabel??systemName(game.system)}</dd></div>{game.category&&<div><dt>Kategori</dt><dd>{game.category}</dd></div>}</dl><p>Kayıtların bu profilde saklanır. Oyun bilgisi veya kapak güncellenince ilerlemen değişmez.</p><details><summary>Dosya kimliği</summary><code>{game.romSha256}</code></details></section><section className="rom-game-info-card"><GameCoreSelector game={game}/></section></aside></div>
    <Dialog open={editing} onClose={()=>{if(!edit.isPending)setEditing(false);}} title="Oyun bilgilerini düzenle" className="games-edit-container" backdropClassName="games-settings-backdrop"><form className="games-edit-form games-surface" onSubmit={event=>{event.preventDefault();edit.mutate();}}><Modal.Heading>Oyun bilgilerini düzenle</Modal.Heading><label className="games-field">Oyun adı<input value={title} maxLength={160} onChange={event=>setTitle(event.target.value)}/></label><label className="games-field">Kategori<input value={category} maxLength={60} placeholder="Örneğin, Yarış" onChange={event=>setCategory(event.target.value)}/></label><p className="games-help">Elle düzenlediğin ad ve kategori otomatik eşleştirmede korunur.</p>{edit.error&&<p className="form-error">{runtimeText(edit.error.message)}</p>}<footer><Button variant="ghost" onPress={()=>setEditing(false)} isDisabled={edit.isPending}>Vazgeç</Button><Button type="submit" isDisabled={!title.trim()||edit.isPending}>Kaydet</Button></footer></form></Dialog>
  </article>;
}
