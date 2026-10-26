export interface ContainerUpdateCandidate {version:string;revision:string;digest:string;totalBytes:number;estimatedDownloadBytes:number|null}
export interface ManagedUpdateStatus {
  supported:boolean;configured:boolean;enabled:boolean;automaticInstall?:boolean;helperConnected?:boolean;preparing:boolean;revision:string|null;error:string|null;
  availability?:{candidate:ContainerUpdateCandidate|null;approvedDigest:string|null;phase:string;checkedAt:string|null;helperSeenAt:string|null;error:string|null};
  last?:{state:string;preparedAt:string;backupId:string}|null;
}
export function approvalFor(candidate:ContainerUpdateCandidate){
  if(!/^sha256:[0-9a-f]{64}$/.test(candidate.digest))throw new Error('Geçersiz sürüm kimliği.');
  return{digest:candidate.digest};
}
export function updatePhase(phase:string){
  switch(phase){
    case'approved':return'Güncelleme onaylandı.';
    case'downloading':return'Onaylanan sürüm indiriliyor. Home çalışmaya devam ediyor.';
    case'waiting':case'ready':return'İndirme hazır. Home kullanılmadığında yedeklenip kurulacak.';
    case'applying':return'Yedek alındı; yeni sürüme geçiliyor.';
    case'updated':return'Güncelleme tamamlandı.';
    case'failed':return'Güncelleme tamamlanamadı. Yedekler korunuyor.';
    default:return'Güncellemeler kullanıcı onayıyla kurulur.';
  }
}
