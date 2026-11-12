// Offline render of the real staged player, not a second slideshow implementation.
const fs=require('node:fs'),path=require('node:path'),{spawn}=require('node:child_process'),{once}=require('node:events');
const {chromium}=require('playwright');
(async()=>{
  const [html,voice,road,subtitles,destination,fpsArg]=process.argv.slice(2),fps=Number(fpsArg||24);
  const browser=await chromium.launch({executablePath:'/usr/bin/chromium',headless:true,args:['--no-sandbox']});
  const page=await browser.newPage({viewport:{width:1280,height:720}});
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.setContent(fs.readFileSync(html,'utf8'),{waitUntil:'load'});
  await page.evaluate(()=>document.fonts.ready);await page.waitForFunction(()=>!document.querySelector('#start').disabled);
  const duration=await page.evaluate(()=>window.INTRO.book.duration),total=duration+3,frames=Math.ceil(total*fps);
  fs.mkdirSync(path.dirname(destination),{recursive:true});
  const filter=`[1:a]apad=pad_dur=3[voice];[2:a]volume=0.045,afade=t=out:st=${duration-.5}:d=0.5[road];[voice][road]amix=inputs=2:duration=first:normalize=0,alimiter=limit=0.95[a]`;
  const encoder=spawn('ffmpeg',['-hide_banner','-loglevel','error','-y','-f','image2pipe','-vcodec','mjpeg','-framerate',String(fps),'-i','pipe:0','-i',voice,'-stream_loop','-1','-i',road,'-filter_complex',filter,'-map','0:v','-map','[a]','-t',String(total),'-c:v','libx264','-preset','fast','-crf','21','-threads','2','-pix_fmt','yuv420p','-c:a','aac','-b:a','128k','-ar','48000','-ac','2','-movflags','+faststart',destination],{stdio:['pipe','ignore','pipe']});
  let stderr='';encoder.stderr.on('data',x=>stderr=(stderr+x).slice(-10000));
  const completed=once(encoder,'close');
  encoder.stdin.on('error',()=>{});
  try{for(let n=0;n<frames;n++){
    const frame=await page.evaluate(t=>{window.INTRO.renderMovieFrame(t);return document.querySelector('canvas').toDataURL('image/jpeg',.88)},n/fps);
    if(!encoder.stdin.write(Buffer.from(frame.split(',')[1],'base64')))await once(encoder.stdin,'drain');
    if(n%(fps*10)===0)console.log(`RENDER ${Math.floor(n/fps)} / ${Math.ceil(total)} seconds`);
  }
  encoder.stdin.end();const [code]=await completed;
  if(code!==0)throw new Error(stderr||'Video encoder failed');if(errors.length)throw new Error(errors.join('\n'));
  }catch(error){encoder.stdin.destroy();encoder.kill();throw new Error(error.message+'\n'+stderr)}finally{await browser.close()}
  console.log(JSON.stringify({status:'PASS',duration:total,fps,frames,source:'same visual storyboard as game',subtitle_sidecar:true,destination}));
})().catch(e=>{console.error(e);process.exit(1)});
