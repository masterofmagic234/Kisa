const fs = require('node:fs');
const assert = require('node:assert/strict');
const {chromium} = require('playwright');
(async()=>{
  const [source, output] = process.argv.slice(2);
  fs.mkdirSync(output,{recursive:true});
  const browser=await chromium.launch({executablePath:process.env.CHROMIUM_PATH || (fs.existsSync('/usr/bin/chromium') ? '/usr/bin/chromium' : undefined),headless:true,args:['--no-sandbox']});
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
  const first = await page.evaluate(() => window.INTRO.book.visuals.shots[0]);
  assert.equal(first.panels[0].traffic.fleet, 'escort');
  const samples = await page.evaluate(() => {
    const live = document.querySelector('canvas');
    const frame = document.createElement('canvas');
    frame.width = 1280;
    frame.height = 720;
    const ctx = frame.getContext('2d');
    const capture = (time, pixel) => {
      window.INTRO.renderAt(time);
      ctx.drawImage(live, 0, 0);
      return pixel ? Array.from(ctx.getImageData(...pixel, 1, 1).data) : frame.toDataURL('image/png');
    };
    return {
      street: [capture(.3, [80, 105]), capture(3.7, [80, 105])],
      convoy: [capture(.3), capture(3.7)],
      glance: [capture(469.82), capture(470.45)],
      still: capture(471.8, [640, 360]),
      frames: [capture(3), capture(385.5), capture(393.35)],
    };
  });
  assert.deepEqual(...samples.street, 'The fixed camera moved the avenue');
  assert.notEqual(...samples.convoy, 'The convoy stalled on the avenue');
  assert.notEqual(...samples.glance, 'Mazur held one drawing through the reaction');
  assert.deepEqual(samples.still, [20, 23, 19, 255], 'The last beat must cut to the matte');
  for (const [i, name] of ['convoy', 'roof', 'leon'].entries()) {
    fs.writeFileSync(`${output}/visual-${name}.png`, Buffer.from(samples.frames[i].split(',')[1], 'base64'));
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
  const result={status:'PASS',offline_http_requests:requests.length,javascript_errors:errors,checks:['start','actual audio playback','pause clock','word captions','mute captions','fixed street and moving convoy','reaction pose and cut to black','skip once','replay','real decoder next paragraph','real decoder ending','mobile overflow']};
  fs.writeFileSync(output+'/browser-validation.json',JSON.stringify(result,null,2));
  console.log(JSON.stringify(result));await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
