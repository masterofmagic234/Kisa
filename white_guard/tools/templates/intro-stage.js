// Same normalized shot/layer contract as scripts/intro_stage.gd.
window.drawIntroStage=function(ctx,visuals,images,seconds,width=1280,height=720){
  const shot=visuals.shots.find(s=>seconds>=s.start&&seconds<s.end)||visuals.shots.at(-1);
  const elapsed=seconds-shot.start,t=Math.max(0,Math.min(1,elapsed/(shot.end-shot.start)));
  const mix=(a,b)=>a.map((v,i)=>v+(b[i]-v)*t),image=images[shot.asset],qw=image.width/2,qh=image.height/2;
  const qx=shot.quadrant%2*qw,qy=Math.floor(shot.quadrant/2)*qh,view=mix(shot.view_from,shot.view_to);
  let sx=qx+view[0]*qw,sy=qy+view[1]*qh,sw=view[2]*qw,sh=view[3]*qh;
  if(sw/sh>width/height){const next=sh*width/height;sx+=(sw-next)/2;sw=next}else{const next=sw*height/width;sy+=(sh-next)/2;sh=next}
  ctx.clearRect(0,0,width,height);ctx.drawImage(image,sx,sy,sw,sh,0,0,width,height);
  for(const layer of shot.layers){
    const actor=images[layer.asset],aw=actor.width/2,ah=actor.height/2,rect=mix(layer.from,layer.to);
    ctx.globalAlpha=layer.alpha??1;ctx.drawImage(actor,(layer.quadrant%2)*aw,Math.floor(layer.quadrant/2)*ah,aw,ah,rect[0]*width,rect[1]*height,rect[2]*width,rect[3]*height);ctx.globalAlpha=1;
  }
  if(shot.label&&elapsed<3.5){
    const alpha=Math.max(0,Math.min(1,(3.5-elapsed)*2)),scale=width/1280,fs=18*scale,x=34*scale,y=48*scale;
    ctx.font=`${fs}px Body`;ctx.textAlign='left';ctx.globalAlpha=alpha;const length=ctx.measureText(shot.label).width;
    ctx.fillStyle='#10120ddd';ctx.fillRect(x-12,y-fs-8,length+24,fs+19);ctx.fillStyle='#eee5cc';ctx.fillText(shot.label,x,y);ctx.globalAlpha=1;
  }
  if(shot.effect==='four_attempts'){
    ctx.strokeStyle='#ad3b2e';ctx.lineWidth=5;for(let i=0;i<4;i++){if(elapsed<i*.65)continue;const x=.065+i*.032;ctx.beginPath();ctx.moveTo(x*width,.72*height);ctx.lineTo((x+.017)*width,.63*height);ctx.stroke()}
  }else if(shot.effect==='rifle_fire'&&elapsed%0.43<.075&&elapsed<1.8){
    ctx.fillStyle='#ffda80cc';for(const [x,y] of [[.49,.21],[.75,.24]]){ctx.beginPath();ctx.arc(x*width,y*height,8*width/1280,0,2*Math.PI);ctx.fill()}
  }else if(shot.effect==='rocket'){
    const p=Math.max(0,Math.min(1,elapsed/2)),x=(.22+(.89-.22)*p)*width,y=(.65+(.08-.65)*p)*height;ctx.strokeStyle='#fff1b6';ctx.lineWidth=3;ctx.beginPath();ctx.moveTo(x-.02*width,y+.02*height);ctx.lineTo(x,y);ctx.stroke();
  }
  return shot;
};
