/** A scene request is valid only for the current choice and explicit playback intent. */
export class RadioSceneIntent {
  private epoch=0;
  scene:string|null=null;
  playing=true;
  select(scene:string|null){this.scene=scene;return ++this.epoch;}
  pause(){this.playing=false;++this.epoch;}
  play(){if(this.playing)return this.epoch;this.playing=true;return ++this.epoch;}
  current(epoch:number){return this.playing&&this.epoch===epoch;}
  ticket(){return this.epoch;}
}
