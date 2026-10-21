import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import assert from 'node:assert/strict';
const js=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
const html=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/index.html',import.meta.url),'utf8');
test('Global capability threshold controls and handlers are removed',()=>{
 assert.doesNotMatch(html,/id="capabilityThresholdForm"|id="capabilityYellowMinimum"|id="capabilityGreenMinimum"/);
 assert.doesNotMatch(js,/function capabilityClass\(|function capabilityThresholds\(|saveCapabilityThresholds|renderCapabilityThresholds/);
});
test('Every calculated capability badge is neutral, never an automatic pass or failure',()=>{
 const c=vm.createContext({escapeHtml:String});
 for(const name of ['formatCapabilityNumber','capabilityBadge']){
  const start=js.indexOf(`function ${name}(`);vm.runInContext(js.slice(start,js.indexOf('\nfunction ',start+1)),c);
 }
 for(const value of [null,-1,0,.7,1,1.33,2,25]){
  const badge=c.capabilityBadge(value,'Pooled within-inspection standard deviation');
  assert.match(badge,/capability-neutral/);assert.doesNotMatch(badge,/capability-good|capability-warn|capability-bad/);
  assert.match(badge,/no capability acceptance requirement/);
 }
});
