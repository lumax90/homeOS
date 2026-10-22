import { Button } from '@heroui/react';
import { useMutation,useQuery,useQueryClient } from '@tanstack/react-query';
import { mutate,request } from '../api';

export interface ProductJob { id:string; kind:string; scopeId:string|null; status:string; revision:number; progress:number; message:string; createdAt:string; result:unknown; }
export function ProductJobs({kind,scope,onApply}:{kind?:string;scope?:string;onApply?:(job:ProductJob)=>void}){
 const client=useQueryClient();const rows=useQuery({queryKey:['product-jobs',kind,scope],queryFn:({signal})=>request<ProductJob[]>(`/product-jobs?${new URLSearchParams({...kind?{kind}:{},...scope?{scope}:{}})}`,{signal}),refetchInterval:query=>query.state.data?.some(job=>['queued','running','applying'].includes(job.status))?2000:false});
 const cancel=useMutation({mutationFn:(job:ProductJob)=>mutate(`/product-jobs/${job.id}/cancel`,{revision:job.revision}),onSuccess:()=>client.invalidateQueries({queryKey:['product-jobs']})});
 const labels:Record<string,string>={queued:'Sırada',running:'İşleniyor',proposed:'İnceleme bekliyor',applying:'Uygulanıyor',completed:'Tamamlandı',failed:'Sorun var',cancelled:'İptal'};
 return <div>{rows.data?.slice(0,10).map(job=><article className="request-card" key={job.id}><div className="request-summary"><strong>{labels[job.status]??job.status}</strong><small>{job.kind}</small></div><p className="field-hint">{job.message}</p>{job.status==='running'&&<progress aria-label="İş ilerlemesi" value={job.progress} max={100}/ >}{['queued','running','proposed'].includes(job.status)&&<Button variant="ghost" size="sm" onPress={()=>cancel.mutate(job)} isDisabled={cancel.isPending}>İptal et</Button>}{job.status==='proposed'&&onApply&&<Button variant="ghost" onPress={()=>onApply(job)}>İncele ve uygula</Button>}</article>)}{(rows.error||cancel.error)&&<p className="form-error">{rows.error?.message||cancel.error?.message}</p>}</div>;
}
