import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
const html=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/index.html',import.meta.url),'utf8');
test('Custom drift form explicitly uses completed averages and offers no lock response',()=>{
 const form=html.slice(html.indexOf('<form id="customRuleForm"'),html.indexOf('<p id="customRuleMessage"'));
 assert.match(form,/one average per variable per completed inspection/);
 assert.match(form,/yellow Watch warning only/);
 assert.doesNotMatch(form,/Sigma threshold|Lock inspection|Recent point window/);
});
test('Custom settings always save warning-only completed-inspection count and direction',()=>{
 const nodes={customRuleName:{value:'Drift'},customWindowSize:{value:'5'},customDirection:{value:'Below'},customRuleNotes:{value:''},customRuleSummary:{}};
 const c=vm.createContext({$:id=>nodes[id]});
 for(const name of ['defaultCustomWatchOptions','customWatchFields','customWatchOptionsPayload','customRulePayload','updateCustomRuleSummary']){
  const start=source.indexOf(`function ${name}(`);vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
 }
 for(const [id,key] of Object.entries(c.customWatchFields())) {
  const value=c.defaultCustomWatchOptions()[key];
  nodes[id]={type:typeof value==='boolean'?'checkbox':typeof value==='number'?'number':'select-one',value:String(value),checked:value};
 }
 nodes.customZonePercentLabel={classList:{toggle(){}}};nodes.customZoneDistanceLabel={classList:{toggle(){}}};
 const payload=c.customRulePayload();
 assert.equal(payload.windowSize,5);assert.equal(payload.direction,'Below');assert.equal(payload.warningBehavior,'Warning');assert.equal(payload.includeWesternElectric,false);
 assert.equal(payload.options.zonePercent,10);assert.equal(payload.options.nearLimitEnabled,true);
 c.updateCustomRuleSummary();assert.match(nodes.customRuleSummary.textContent,/5 consecutive completed inspection averages/);assert.match(nodes.customRuleSummary.textContent,/lower specification limit/);
});
