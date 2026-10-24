// Commons videoinfo/extmetadata reviewed 2026-10-21. Only user-selected visual mode loads media.
import scenes from './sceneSources.json' with {type:'json'};
export interface StockVideo {id:string;scene:string;label:string;url:string;author:string;license:string;licenseUrl:string;source:string;commonsTitle:string}
export const stockVideos:readonly StockVideo[]=scenes;
export function videosForScene(scene:string){return stockVideos.filter(video=>video.scene===scene);}
export function selectedVideo(scene:string,id=''){return stockVideos.find(video=>video.scene===scene&&video.id===id)??stockVideos.find(video=>video.scene===scene);}
export const galleryWorks=[
  {id:16568,title:'Water Lilies',artist:'Claude Monet',image:'3c27b499-af56-f0d5-93b5-a7f2f1ad5813'},
  {id:16571,title:'Arrival of the Normandy Train, Gare Saint-Lazare',artist:'Claude Monet',image:'0f1cc0e0-e42e-be16-3f71-2022da38cb93'},
  {id:64818,title:'Stacks of Wheat (End of Summer)',artist:'Claude Monet',image:'a38e2828-ec6f-ece1-a30f-70243449197b'},
];
