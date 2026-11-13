const fs = require('node:fs');
const assert = require('node:assert/strict');
const {chromium} = require('playwright');
(async()=>{
  const [source, output] = process.argv.slice(2);
  fs.mkdirSync(output,{recursive:true});
  const browser=await chromium.launch({executablePath:'/usr/bin/chromium',headless:true,args:['--no-sandbox']});
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  const errors=[],requests=[];
  page.on('pageerror',e=>errors.push(e.message));
  page.on('request',r=>{if(/^https?:/.test(r.url()))requests.push(r.url())});
  await page.setContent(fs.readFileSync(source,'utf8'),{waitUntil:'load'});
  await page.evaluate(()=>document.fonts.ready);
  await page.waitForFunction(()=>!document.querySelector('#start').disabled);
  await page.screenshot({path:output+'/browser-title.png'});
  await page.click('#start');
  await page.waitForFunction(()=>window.INTRO.getState().time>.15);
  await page.click('#pause');
  assert(await page.locator('#captions').isHidden(),'Captions dominate the default picture');
  const paused=await page.evaluate(()=>window.INTRO.getState().time);
  await page.waitForTimeout(300);
  assert(Math.abs((await page.evaluate(()=>window.INTRO.getState().time))-paused)<.06,'Pause drifted');
  const cue=await page.evaluate(()=>window.INTRO.book.blocks[0].timed_captions[1]);
  await page.evaluate(t=>window.INTRO.seek(t),cue.start+.01);
  assert.equal(await page.locator('#line').innerText(),cue.text);
  await page.click('#subtitles');assert(await page.locator('#captions').isVisible());
  await page.click('#subtitles');assert(await page.locator('#captions').isHidden());
  await page.click('#mute');assert(await page.locator('#captions').isVisible());
  await page.click('#mute');
  const staging=await page.evaluate(()=>{
    const visuals=window.INTRO.book.visuals;
    const flatten=nodes=>nodes.flatMap(node=>[node,...flatten(node.children||[])]);
    let articulation=null;
    for(const scene of Object.values(visuals.art.scenes)){
      for(const node of flatten(scene.nodes)){
        let previousTime=0,previousValue=node.transform?.rotation||0;
        for(const key of node.keys||[]){
          if(key.rotation===undefined)continue;
          if(key.time>previousTime&&Math.abs(key.rotation-previousValue)>.001){
            const a=window.drawIntroStage.stateAt(node,previousTime);
            const b=window.drawIntroStage.stateAt(node,(previousTime+key.time)/2);
            articulation={a:a.rotation,b:b.rotation,reversible:JSON.stringify(a)===JSON.stringify(window.drawIntroStage.stateAt(node,previousTime))};
            break;
          }
          previousTime=key.time;previousValue=key.rotation;
        }
        if(articulation)break;
      }
      if(articulation)break;
    }
    const shot=visuals.shots.find(s=>(s.panels||[]).length>1&&s.panels.some(p=>p.reveal>0));
    let reveal=null;
    if(shot){
      const index=shot.panels.findIndex(p=>p.reveal>0),panel=shot.panels[index];
      const [x,y,w,h]=window.drawIntroStage.panelRects(shot,1280,720)[index];
      const canvas=document.querySelector('canvas'),context=canvas.getContext('2d');
      const hashAt=t=>{
        window.INTRO.renderAt(t);
        const data=context.getImageData(Math.ceil(x)+1,Math.ceil(y)+1,Math.floor(w)-2,Math.floor(h)-2).data;
        let hash=2166136261;
        for(let i=0;i<data.length;i++)hash=Math.imul(hash^data[i],16777619)>>>0;
        return hash;
      };
      const before=shot.start+Math.max(0,panel.reveal-.04);
      const after=shot.start+Math.min(shot.end-shot.start-.001,panel.reveal+.25);
      const first=hashAt(before),second=hashAt(after),back=hashAt(before);
      reveal={first,second,back,shot:shot.id,panel:index};
    }
    return {schema:visuals.schema,articulation,reveal};
  });
  assert.equal(staging.schema,2,'The old bitmap staging was packaged');
  assert(staging.articulation,'No articulated pose was available to render');
  assert.notEqual(staging.articulation.a,staging.articulation.b,'The actual joint animation stayed rigid');
  assert(staging.articulation.reversible,'Seeking back changed a keyed pose');
  assert(staging.reveal,'No composition reveals a detail during narration');
  assert.notEqual(staging.reveal.first,staging.reveal.second,'A revealed panel remained visually empty');
  assert.equal(staging.reveal.first,staging.reveal.back,'Seeking back did not conceal a future panel');
  for(const [name,time] of [['convoy',3],['roof',380],['leon',395]]){
    const png=await page.evaluate(t=>{window.INTRO.renderAt(t);return document.querySelector('canvas').toDataURL('image/png')},time);
    fs.writeFileSync(output+'/visual-'+name+'.png',Buffer.from(png.split(',')[1],'base64'));
  }
  await page.evaluate(()=>window.INTRO.playBlock(6));
  await page.waitForTimeout(100);
  await page.screenshot({path:output+'/browser-appointment.png'});
  await page.click('#skip');
  await page.evaluate(()=>window.INTRO.finish(true));
  assert.equal((await page.evaluate(()=>window.INTRO.getState())).completionCount,1);
  await page.click('#replay');
  await page.waitForFunction(()=>window.INTRO.getState().time>.1);
  await page.evaluate(()=>window.INTRO.seek(window.INTRO.book.blocks[0].duration-.12));
  await page.waitForFunction(()=>window.INTRO.getState().index===1,{},{timeout:5000});
  await page.evaluate(()=>{window.INTRO.playBlock(11);window.INTRO.seek(window.INTRO.book.blocks[11].duration-.12)});
  await page.waitForFunction(()=>window.INTRO.getState().state==='finished',{},{timeout:5000});
  assert.equal((await page.evaluate(()=>window.INTRO.getState())).completionCount,2);
  await page.screenshot({path:output+'/browser-ending.png'});
  await page.setViewportSize({width:390,height:844});
  assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Mobile overflow');
  assert.deepEqual(errors,[]);assert.deepEqual(requests,[]);
  const result={status:'PASS',offline_http_requests:requests.length,javascript_errors:errors,staging,visual_quality_assessed:false,checks:['start','actual audio playback','pause clock','word captions','mute captions','keyed joint and reverse seek','rendered detail reveal and reverse seek','skip once','replay','real decoder next paragraph','real decoder ending','mobile overflow']};
  fs.writeFileSync(output+'/browser-validation.json',JSON.stringify(result,null,2));
  console.log(JSON.stringify(result));await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
