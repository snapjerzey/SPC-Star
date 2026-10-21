import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
for(const version of ['1.1.0',null]) test(version?'login and console show the running server version':'version lookup failure does not block the app',async()=>{
 const labels=[{},{}];const calls=[];
 const context=vm.createContext({api:async(path,options)=>{calls.push({path,options});if(!version)throw Error('offline');return {version};},document:{querySelectorAll:()=>labels}});
 const start=source.indexOf('async function loadApplicationVersion(');
 vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start)),context);
 await context.loadApplicationVersion();
 assert(labels.every(label=>label.textContent===(version?'Version '+version:'Version unavailable')));
 assert.equal(calls[0].path,'/health');assert.equal(calls[0].options.cache,'no-store');
});
