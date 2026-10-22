import { useEffect,useRef,useState } from 'react';
import { Button } from '@heroui/react';
import { useMutation,useQuery,useQueryClient } from '@tanstack/react-query';
import { SkipForward,Scissors } from 'lucide-react';
import { clock,mutate,request,type MediaItem } from '../api';
import { ProductJobs,type ProductJob } from './ProductJobs';

interface Segment { kind:string;startSeconds:number;endSeconds:number;source:string;confidence:number }
interface SegmentSet { mediaId:string;revision:number;fingerprint:string;matchRevision:number;segments:Segment[] }
interface Preferences { autoSkipIntro:boolean;autoSkipRecap:boolean;autoSkipCredits:boolean;autoSkipCommercials:boolean }
const names:Record<string,string>={intro:'Girişi atla',recap:'Özeti atla',credits:'Jeneriği atla',commercial:'Reklamı atla'};
export function SkipSegments({mediaId,position,onSeek,canControl=true}:{mediaId:string;position:number;onSeek:(seconds:number)=>void;canControl?:boolean}){
 const rows=useQuery({queryKey:['media-segments',mediaId],queryFn:({signal})=>request<SegmentSet>(`/media/${mediaId}/segments`,{signal})});
 const preferences=useQuery({queryKey:['player-preferences'],queryFn:()=>request<Preferences>('/player/preferences')});
 const skipped=useRef(new Set<string>());useEffect(()=>{skipped.current.clear();},[mediaId,rows.data?.revision]);
 const current=rows.data?.segments.find(segment=>position>=segment.startSeconds&&position<segment.endSeconds-.2);
 useEffect(()=>{if(!current||!canControl||!preferences.data)return;const enabled=current.kind==='intro'?preferences.data.autoSkipIntro:current.kind==='recap'?preferences.data.autoSkipRecap:current.kind==='credits'?preferences.data.autoSkipCredits:preferences.data.autoSkipCommercials;const key=current.kind+':'+current.startSeconds;if(enabled&&!skipped.current.has(key)){skipped.current.add(key);onSeek(current.endSeconds);}},[current,canControl,preferences.data,onSeek]);
 return current&&canControl?<div className="skip-segment-control"><Button className="secondary-button" onPress={()=>onSeek(current.endSeconds)}><SkipForward size={16}/>{names[current.kind]}</Button></div>:null;
}
export function SkipPreferences(){
 const client=useQueryClient();const data=useQuery({queryKey:['player-preferences'],queryFn:()=>request<Preferences>('/player/preferences')});const save=useMutation({mutationFn:(value:Preferences)=>mutate('/player/preferences',value,'PUT'),onSuccess:()=>client.invalidateQueries({queryKey:['player-preferences']})});
 return data.data?<div className="home-form">{([['autoSkipIntro','Girişi otomatik atla'],['autoSkipRecap','Özeti otomatik atla'],['autoSkipCredits','Jeneriği otomatik atla'],['autoSkipCommercials','İşaretli reklamları otomatik atla']] as const).map(([key,label])=><label className="check-field" key={key}><input type="checkbox" checked={data.data![key]} disabled={save.isPending} onChange={event=>save.mutate({...data.data!,[key]:event.target.checked})}/>{label}</label>)}{save.error&&<p className="form-error">{save.error.message}</p>}</div>:null;
}
export function SegmentEditor({item}:{item:MediaItem}){
 const client=useQueryClient();const [open,setOpen]=useState(false);const [draft,setDraft]=useState<Segment[]|null>(null);const [proposal,setProposal]=useState<ProductJob|null>(null);
 const rows=useQuery({queryKey:['media-segments',item.id],enabled:open,queryFn:()=>request<SegmentSet>(`/media/${item.id}/segments`)});const segments=draft??rows.data?.segments??[];
 const refresh=()=>Promise.all(['media-segments','product-jobs'].map(key=>client.invalidateQueries({queryKey:[key]})));
 const save=useMutation({mutationFn:()=>mutate(`/media/${item.id}/segments`,{expectedRevision:rows.data!.revision,expectedMatchRevision:rows.data!.matchRevision,fingerprint:rows.data!.fingerprint,segments},'PUT'),onSuccess:()=>{setDraft(null);void refresh();}});
 const analyze=useMutation({mutationFn:()=>mutate(`/media/${item.id}/segments/analyze`,{sharedAudio:true,chapters:true,credits:true,recap:true,autoApply:false}),onSuccess:refresh});
 const apply=useMutation({mutationFn:()=>mutate(`/segments/jobs/${proposal!.id}/apply`,{revision:proposal!.revision}),onSuccess:()=>{setProposal(null);setDraft(null);void refresh();}});
 const result=proposal?.result as {segments:Segment[];evidence:string[];warnings:string[]}|undefined;
 return <section className="personal-content"><Button variant="ghost" onPress={()=>setOpen(value=>!value)}><Scissors size={15}/> Giriş, özet, jenerik ve reklam aralıkları</Button>{open&&<div className="home-form"><Button variant="ghost" onPress={()=>analyze.mutate()} isDisabled={analyze.isPending}>Dosya ve ortak bölüm seslerinden algıla</Button><p className="field-hint">Algılama gerçek sahne/altyazı işaretleri ve farklı bölümlerdeki ortak sese dayanır. Kaydettiğin aralıkları oynatıcı atlayabilir.</p>
 {segments.map((segment,index)=><div className="form-columns" key={index}><label className="field"><span>Tür</span><select value={segment.kind} onChange={event=>setDraft(segments.map((value,i)=>i===index?{...value,kind:event.target.value,source:'manual'}:value))}>{Object.entries(names).map(([kind,name])=><option key={kind} value={kind}>{name}</option>)}</select></label><label className="field"><span>Başlangıç · sn</span><input type="number" min={0} step={.1} value={segment.startSeconds} onChange={event=>setDraft(segments.map((value,i)=>i===index?{...value,startSeconds:Number(event.target.value),source:'manual'}:value))}/></label><label className="field"><span>Bitiş · sn</span><input type="number" min={0} step={.1} value={segment.endSeconds} onChange={event=>setDraft(segments.map((value,i)=>i===index?{...value,endSeconds:Number(event.target.value),source:'manual'}:value))}/></label><Button variant="ghost" onPress={()=>setDraft(segments.filter((_,i)=>i!==index))}>Kaldır</Button></div>)}
 <div className="content-actions"><Button variant="ghost" onPress={()=>setDraft([...segments,{kind:'intro',startSeconds:0,endSeconds:Math.min(item.durationSeconds??60,60),source:'manual',confidence:1}])}>Aralık ekle</Button><Button className="secondary-button" onPress={()=>save.mutate()} isDisabled={!rows.data||save.isPending}>Aralıkları kaydet</Button></div>
 <ProductJobs kind="segment-analysis" scope={item.id} onApply={setProposal}/>{result&&<article className="request-card">{result.segments.map((segment,index)=><p key={index}>{names[segment.kind]} · {clock(segment.startSeconds)}–{clock(segment.endSeconds)} · %{Math.round(segment.confidence*100)}</p>)}{[...result.evidence,...result.warnings].map((line,index)=><p className="field-hint" key={index}>{line}</p>)}<Button variant="ghost" onPress={()=>apply.mutate()} isDisabled={!result.segments.length||apply.isPending}>Önerilen aralıkları uygula</Button></article>}
 {(rows.error||save.error||analyze.error||apply.error)&&<p className="form-error">{rows.error?.message||save.error?.message||analyze.error?.message||apply.error?.message}</p>}</div>}</section>;
}
