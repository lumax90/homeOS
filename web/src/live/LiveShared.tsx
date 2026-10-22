import {useEffect,useState} from 'react';
import {Button} from '@heroui/react';
import {useMutation,useQueryClient} from '@tanstack/react-query';
import {Heart,Radio,Tv} from 'lucide-react';
import {appUrl,mutate} from '../api';
import {runtimeText,translate as t,uiLocale} from '../i18n';
import {programmeProgress,type TvChannel,type TvChannelCard,type TvProgramme} from './types';

export const liveKeys=['live-library','live-channel','live-player-channels','live-neighbours','live-channels','live-sources','live-public-lists','live-guide','dvr-recordings','dvr-rules','product-jobs'];
export function useLiveRefresh(){const cache=useQueryClient();return()=>Promise.all(liveKeys.map(key=>cache.invalidateQueries({queryKey:[key]})));}
export function tvTime(value:string){return new Date(value).toLocaleTimeString(uiLocale(),{hour:'2-digit',minute:'2-digit'});}
const genreLabels:Record<string,string>={News:'Haber',Music:'Müzik',Documentary:'Belgesel',Education:'Eğitim',Entertainment:'Eğlence',General:'Genel',Kids:'Çocuk',Sports:'Spor',Relax:'Dinlenme',Movies:'Filmler',Series:'Diziler',Culture:'Kültür',Business:'Ekonomi',Weather:'Hava durumu',Outdoor:'Doğa',Travel:'Gezi',Family:'Aile'};
export function tvGroup(value:string){return value.split(';').map(part=>t(genreLabels[part]??part)).join(' · ')||t('Diğer');}
export function ChannelLogo({channel}:{channel:TvChannel}){const[failed,setFailed]=useState(false);useEffect(()=>setFailed(false),[channel.id,channel.logo,channel.available,channel.enabled]);return <div className="live-channel-logo">{channel.logo&&!failed?<img src={appUrl(channel.logo)} alt="" loading="lazy" onError={()=>setFailed(true)}/>:<>{channel.radio?<Radio size={28}/>:<Tv size={28}/>}<span aria-hidden="true">{channel.name.slice(0,2).toLocaleUpperCase()}</span></>}</div>;}
export function LiveFavorite({item}:{item:TvChannelCard}){const refresh=useLiveRefresh();const change=useMutation({mutationFn:()=>mutate(`/live/channels/${item.channel.id}/favorite`,{favorite:!item.favorite},'PUT'),onSuccess:refresh});return <><Button isIconOnly variant="ghost" className="live-favorite" aria-label={item.favorite?t('Favorilerimden çıkar'):t('Favorilerime ekle')} aria-pressed={item.favorite} onPress={()=>change.mutate()} isDisabled={change.isPending}><Heart size={18} fill={item.favorite?'currentColor':'none'}/></Button>{change.error&&<span className="live-action-error" role="status">{runtimeText(change.error.message)}</span>}</>;}
export function OnAir({programme,now=Date.now(),compact=false}:{programme:TvProgramme|null;now?:number;compact?:boolean}){const progress=programmeProgress(programme,now);return programme?<div className={`live-on-air${compact?' is-compact':''}`}><span>{tvTime(programme.startsAt)} — {tvTime(programme.endsAt)}</span><strong title={programme.title}>{programme.title}</strong>{progress!==null&&<div className="live-programme-progress" role="progressbar" aria-label={programme.title} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(progress*100)}><i style={{width:`${progress*100}%`}}/></div>}</div>:<div className="live-on-air live-no-epg"><span>Canlı yayın</span><strong>Program bilgisi yok</strong></div>;}
