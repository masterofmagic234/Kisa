const fs = require('node:fs');
const {chromium} = require('playwright');
(async () => {
  const [source, destination] = process.argv.slice(2);
  const browser = await chromium.launch({executablePath:'/usr/bin/chromium',headless:true,args:['--no-sandbox']});
  const page = await browser.newPage({viewport:{width:1280,height:900}});
  await page.setContent(fs.readFileSync(source,'utf8'),{waitUntil:'load'});
  await page.evaluate(() => document.fonts.ready);
  const checks = await page.evaluate(() => ({
    title:document.title,
    sections:document.querySelectorAll('section.document').length,
    tables:document.querySelectorAll('table').length,
    images:[...document.images].every(image=>image.complete&&image.naturalWidth>0),
    navigation:[...document.querySelectorAll('nav a')].every(a=>document.querySelector(a.hash))
  }));
  if(checks.sections!==5 || !checks.images || !checks.navigation) throw new Error(JSON.stringify(checks));
  await page.pdf({path:destination,format:'A4',printBackground:true,margin:{top:'14mm',bottom:'16mm',left:'13mm',right:'13mm'},displayHeaderFooter:true,headerTemplate:'<span></span>',footerTemplate:'<div style="font-size:8px;width:100%;text-align:center;color:#777">Белая гвардия · план экранизации · <span class="pageNumber"></span> / <span class="totalPages"></span></div>'});
  await page.emulateMedia({media:'screen'});
  await page.screenshot({path:destination.replace(/\.pdf$/,'.png'),fullPage:false});
  await browser.close();
  console.log(JSON.stringify(checks));
})().catch(error=>{console.error(error);process.exit(1)});
