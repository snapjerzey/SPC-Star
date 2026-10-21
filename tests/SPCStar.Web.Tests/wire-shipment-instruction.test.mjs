import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
test('Wire-shipment instruction is beside its required persistent field',()=>{
 const elements={jobTagsForm:{innerHTML:'',querySelectorAll:()=>[]},jobTagsList:{innerHTML:''}};
 const instruction='Verify this number matches the wire shipment documentation for the material being used.';
 const context=vm.createContext({state:{snapshot:{partJobDataFields:[{partNum:'71355',fieldName:'Wire Shipment (W/S) #',instruction,isRequired:true,displayOrder:1}]}},$:id=>elements[id],jobDataFieldAppliesToSelectedSet:()=>true,isBuiltInOrPartStandardJobData:()=>false,isRedundantJobDataField:()=>false,isMaterialLotJobDataField:()=>false,partStandardJobData:()=>[],isBoxNumberJobDataField:()=>false,isPerInspectionJobDataField:()=>false,jobDataFieldIsRequiredAtLoad:f=>f.isRequired,escapeHtml:x=>String(x)});
 const start=source.indexOf('function renderConfiguredJobDataFields('),end=source.indexOf('\nfunction ',start+1);vm.runInContext(source.slice(start,end),context);
 context.renderConfiguredJobDataFields({partNum:'71355'});
 assert.match(elements.jobTagsForm.innerHTML,/class="job-tag-input"[^>]*data-per-inspection="false"[^>]*required/);
 assert.ok(elements.jobTagsForm.innerHTML.indexOf(instruction)>elements.jobTagsForm.innerHTML.indexOf('<input'));
 assert.match(elements.jobTagsForm.innerHTML,/<small[^>]*>Verify this number matches/);
});
