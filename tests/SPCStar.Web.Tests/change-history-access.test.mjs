import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
const html=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/index.html',import.meta.url),'utf8');
const endpoints=fs.readFileSync(new URL('../../src/SPCStar.Api/ChangeHistoryEndpoints.cs',import.meta.url),'utf8');
test('Change History is under Admin and only exposes read/export endpoints',()=>{
 assert.match(source,/admin: \["Archive", "ChangeHistory", "Notes"\]/);
 assert.match(source,/ChangeHistory: canViewChangeHistory\(\)/);
 assert.match(html,/id="setupChangeHistorySectionTab"/);
 assert.equal((endpoints.match(/app\.MapGet/g)||[]).length,2);
 assert.doesNotMatch(endpoints,/MapPost|MapPut|MapPatch|MapDelete/);
 assert.match(endpoints,/ValidateSession/);assert.match(endpoints,/CanViewChangeHistory/);
 const section=html.slice(html.indexOf('<section id="setupChangeHistorySection"'),html.indexOf('</main>'));
 assert.doesNotMatch(section,/Delete|Save Change|Edit Change/);
});
test('inspection entry and lock clearing do not call the change history recorder',()=>{
 for(const file of ['InspectionMeasurementService.cs','AlertOverrideService.cs','InspectionFrequencyService.cs']) {
  const text=fs.readFileSync(new URL('../../src/SPCStar.Core/Services/'+file,import.meta.url),'utf8');
  assert.doesNotMatch(text,/ChangeHistory|RecordSaved|AddAction/);
 }
});
