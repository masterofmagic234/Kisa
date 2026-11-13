// Shared vector scene contract: data/intro_art.json + data/intro_shots.json.
// No wall clock is read here: pause, seek, game and export use the same timeline.
(function(){
  const clamp=x=>Math.max(0,Math.min(1,x));
  const interpolate=(a,b,t)=>Array.isArray(a)&&Array.isArray(b)&&a.length===b.length
    ?a.map((v,i)=>interpolate(v,b[i],t))
    :typeof a==='number'&&typeof b==='number'?a+(b-a)*t:t<1?a:b;
  function stateAt(node,seconds){
    const base={x:0,y:0,rotation:0,scale_x:1,scale_y:1,alpha:1,...node.transform};
    base.points=node.points||[];
    if(!node.keys?.length)return base;
    const time=node.loop>0?((seconds%node.loop)+node.loop)%node.loop:seconds;
    const state={...base};
    // A sparse key for one joint must not reset another joint/property.
    for(const name of ['x','y','rotation','scale_x','scale_y','alpha','points']){
      let previous=base[name],at=0;
      for(const key of node.keys||[]){
        if(!(name in key))continue;
        const next=Number(key.time);
        if(time<next){state[name]=interpolate(previous,key[name],clamp((time-at)/Math.max(1e-9,next-at)));break;}
        previous=key[name];at=next;state[name]=previous;
      }
    }
    return state;
  }
  function panelRects(shot,width=1280,height=720){
    if(!shot.panels?.length)return [[0,0,width,height]];
    const sx=width/1280,sy=height/720,mx=40*sx,my=40*sy,gx=22*sx,gy=22*sy;
    const w=width-2*mx,h=height-2*my;
    if(shot.layout==='split')return [[mx,my,w,(h-gy)/2],[mx,my+(h+gy)/2,w,(h-gy)/2]];
    if(shot.layout==='triptych'){
      const top=(h-gy)*.45,bottom=h-gy-top;
      return [[mx,my,w,top],[mx,my+top+gy,(w-gx)/2,bottom],[mx+(w+gx)/2,my+top+gy,(w-gx)/2,bottom]];
    }
    if(shot.layout==='strip')return [0,1,2].map(i=>[mx+i*(w+gx)/3,my,(w-2*gx)/3,h]);
    return [[mx,my,w,h]];
  }
  function polygonPoints(source){
    let points=[];
    for(const pair of source){
      const p=[Math.fround(pair[0]),Math.fround(pair[1])],last=points.at(-1);
      if(!last||(p[0]-last[0])**2+(p[1]-last[1])**2>1e-10)points.push(p);
    }
    if(points.length>1&&(points[0][0]-points.at(-1)[0])**2+(points[0][1]-points.at(-1)[1])**2<=1e-10)points.pop();
    if(points.length<3)return [];
    const cross=(a,b,c)=>(b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]);
    const crossing=(a,b,c,d)=>cross(a,b,c)*cross(a,b,d)<0&&cross(c,d,a)*cross(c,d,b)<0;
    if(points.length===4&&(crossing(...points)||crossing(points[1],points[2],points[3],points[0]))){
      const sorted=[...points].sort((a,b)=>a[0]-b[0]||a[1]-b[1]),lower=[],upper=[];
      for(const p of sorted){while(lower.length>=2&&cross(lower.at(-2),lower.at(-1),p)<=0)lower.pop();lower.push(p);}
      for(const p of sorted.reverse()){while(upper.length>=2&&cross(upper.at(-2),upper.at(-1),p)<=0)upper.pop();upper.push(p);}
      points=lower.slice(0,-1).concat(upper.slice(0,-1));
    }
    let area=0;for(let i=1;i<points.length-1;i++)area+=cross(points[0],points[i],points[i+1]);
    return Math.abs(area)*.5>=.05?points:[];
  }
  function drawNode(ctx,node,seconds){
    if(seconds<(node.visible_from??0)||seconds>=(node.visible_until??Infinity))return;
    const state=stateAt(node,seconds);
    if(state.alpha<=0)return;
    ctx.save();ctx.translate(state.x,state.y);ctx.rotate(state.rotation*Math.PI/180);ctx.scale(state.scale_x,state.scale_y);ctx.globalAlpha*=state.alpha;
    ctx.lineWidth=node.line_width??1;ctx.lineJoin='miter';ctx.lineCap='butt';
    const points=node.type==='polygon'?polygonPoints(state.points):state.points;
    if((node.type==='polygon'||node.type==='line')&&points.length){
      ctx.beginPath();ctx.moveTo(points[0][0],points[0][1]);
      for(let i=1;i<points.length;i++)ctx.lineTo(points[i][0],points[i][1]);
      if(node.type==='polygon')ctx.closePath();
      if(node.type==='polygon'&&node.fill){ctx.fillStyle=node.fill;ctx.fill();}
      if(node.stroke){ctx.strokeStyle=node.stroke;ctx.stroke();}
    }else if(node.type==='ellipse'){
      const [x,y,w,h]=node.rect;
      if(Math.abs(w*h)*Math.PI/4>=.05){
        ctx.beginPath();ctx.ellipse(x+w/2,y+h/2,Math.abs(w/2),Math.abs(h/2),0,0,Math.PI*2);
        if(node.fill){ctx.fillStyle=node.fill;ctx.fill();}
        if(node.stroke){ctx.strokeStyle=node.stroke;ctx.stroke();}
      }
    }
    for(const child of node.children||[])drawNode(ctx,child,seconds);
    ctx.restore();
  }
  function drawVector(ctx,visuals,seconds,width,height,shot){
    const elapsed=seconds-shot.start,progress=clamp(elapsed/(shot.end-shot.start));
    const art=visuals.art,aw=art.width||1280,ah=art.height||720;
    const panels=shot.panels?.length?shot.panels:[{scene:shot.scene}];
    const rects=panelRects(shot,width,height);
    ctx.fillStyle='#080b0c';ctx.fillRect(0,0,width,height);
    panels.forEach((panel,index)=>{
      if(elapsed<(panel.reveal||0)||!rects[index])return;
      const scene=art.scenes[panel.scene||shot.scene];if(!scene)return;
      const [x,y,w,h]=rects[index];
      const from=panel.view_from||[0,0,1,1],to=panel.view_to||from;
      const view=interpolate(from,to,progress);
      let vx=view[0]*aw,vy=view[1]*ah,vw=view[2]*aw,vh=view[3]*ah;
      const contain=panel.fit==='contain';
      if((vw/vh>w/h)!==contain){const next=vh*w/h;vx+=(vw-next)/2;vw=next;}else{const next=vw*h/w;vy+=(vh-next)/2;vh=next;}
      ctx.save();ctx.beginPath();ctx.rect(x,y,w,h);ctx.clip();
      ctx.fillStyle=scene.background||'#1a2327';ctx.fillRect(x,y,w,h);
      ctx.translate(x,y);ctx.scale(w/vw,h/vh);ctx.translate(-vx,-vy);
      const panelTime=Math.max(0,elapsed-(panel.reveal||0))+(panel.time_offset||0);
      for(const node of scene.nodes||[])drawNode(ctx,node,panelTime);
      ctx.restore();
    });
  }
  // Keep earlier preview packages readable; new staging never uses this path.
  function drawLegacy(ctx,images,seconds,width,height,shot){
    const elapsed=seconds-shot.start,t=clamp(elapsed/(shot.end-shot.start)),image=images[shot.asset];if(!image)return;
    const mix=(a,b)=>interpolate(a,b,t),qw=image.width/2,qh=image.height/2;
    const view=mix(shot.view_from,shot.view_to);let sx=shot.quadrant%2*qw+view[0]*qw,sy=Math.floor(shot.quadrant/2)*qh+view[1]*qh,sw=view[2]*qw,sh=view[3]*qh;
    if(sw/sh>width/height){const next=sh*width/height;sx+=(sw-next)/2;sw=next;}else{const next=sw*height/width;sy+=(sh-next)/2;sh=next;}
    ctx.drawImage(image,sx,sy,sw,sh,0,0,width,height);
    for(const layer of shot.layers||[]){const actor=images[layer.asset],aw=actor.width/2,ah=actor.height/2,rect=mix(layer.from,layer.to);ctx.save();ctx.globalAlpha=layer.alpha??1;ctx.drawImage(actor,layer.quadrant%2*aw,Math.floor(layer.quadrant/2)*ah,aw,ah,rect[0]*width,rect[1]*height,rect[2]*width,rect[3]*height);ctx.restore();}
  }
  window.drawIntroStage=function(ctx,visuals,images,seconds,width=1280,height=720){
    const shot=visuals.shots.find(s=>seconds>=s.start&&seconds<s.end)||visuals.shots.at(-1);
    ctx.clearRect(0,0,width,height);if(!shot)return null;
    if(visuals.art&&shot.scene)drawVector(ctx,visuals,seconds,width,height,shot);
    else drawLegacy(ctx,images,seconds,width,height,shot);
    const elapsed=seconds-shot.start;
    if(shot.label&&elapsed<3){
      ctx.save();ctx.globalAlpha=clamp((3-elapsed)/.35);ctx.font=`${16*width/1280}px Body`;ctx.textAlign='left';ctx.textBaseline='top';
      ctx.shadowColor='#080b0cf2';ctx.shadowOffsetX=width/1280;ctx.shadowOffsetY=2*height/720;ctx.fillStyle='#d2c9ad';ctx.fillText(shot.label,64*width/1280,54*height/720);ctx.restore();
    }
    return shot;
  };
  window.drawIntroStage.stateAt=stateAt;
  window.drawIntroStage.panelRects=panelRects;
  window.drawIntroStage.polygonPoints=polygonPoints;
})();
