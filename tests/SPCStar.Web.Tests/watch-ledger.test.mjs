import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
test('Ledger shows Watch reason on its measurement row; red failures take precedence',()=>{
 const rows=[];
 const c=vm.createContext({document:{createElement:()=>({dataset:{},querySelector:()=>({addEventListener(){}})})},
  canEditInspectionHistory:()=>true,formatDateTime:String,normalizeInspectionPhase:String,escapeHtml:value=>String(value).replaceAll('<','&lt;'),reviewMeasurementValueControl:()=>'',saveReviewMeasurement(){}});
 const start=source.indexOf('function renderReviewMeasurementDetail(');
 vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
 const measurement={id:'one',timestamp:'now',characteristicName:'Width',inspectionPhase:'In Process',resourceId:'M',processCode:'OP',operationSeq:10,operatorUserId:'operator',isWatch:true,watchReason:'Near upper limit <test>'};
 c.renderReviewMeasurementDetail({appendChild:row=>rows.push(row)},'run',measurement);
 assert.match(rows[0].className,/measurement-watch/);
 assert.match(rows[0].innerHTML,/Watch: Near upper limit &lt;test>/);
 c.renderReviewMeasurementDetail({appendChild:row=>rows.push(row)},'run',{...measurement,isOutOfSpec:true});
 assert.match(rows[1].className,/measurement-out-spec/);
 assert.doesNotMatch(rows[1].className,/measurement-watch/);
});
