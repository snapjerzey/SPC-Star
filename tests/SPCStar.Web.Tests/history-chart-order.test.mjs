import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
function fixture(){
 const nodes={reportPartNum:{value:'61033'},reportJobNum:{value:''},reportOperationCode:{value:'Production|10'},reportInspectionPhase:{value:'In Process'},reportCharacteristicName:{value:''},reportCharacteristicOptions:{}};
 const plan=(name,order,phase='In Process',operationSeq=10,partNum='61033')=>({partNum,processCode:'Production',operationSeq,inspectionPhase:phase,characteristicName:name,displayOrder:order});
 const plans=[plan('Angle',9),plan('Width',4),plan('Material Thickness',1),plan('Width',2,'Setup'),plan('Other operation',1,'In Process',20),plan('Other part',1,'In Process',10,'70306')];
 let choices=[];
 const c=vm.createContext({state:{snapshot:{inspectionPlans:plans,jobs:[{jobNum:'J61033',partNum:'61033'}]}},$:id=>nodes[id],normalizeInspectionPhase:x=>x,operationKeyFor:p=>`${p.processCode}|${p.operationSeq}`,fillDatalist:(_,rows)=>{choices=[...rows];}});
 for(const name of ['reportCharacteristicCandidates','orderedReportCharacteristicNames','refreshReportCharacteristicChoices','reportPartFilter','selectedReportOperation']){
  const start=source.indexOf(`function ${name}(`);vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
 }
 return {c,nodes,plans,choices:()=>choices};
}
test('Charts and variable picker share configured order rather than alphabetical order',()=>{
 const {c,choices,plans}=fixture();const original=JSON.stringify(plans);
 assert.deepEqual([...c.reportCharacteristicCandidates()],['Material Thickness','Width','Angle']);
 c.refreshReportCharacteristicChoices();assert.deepEqual(choices(),['Material Thickness','Width','Angle']);
 assert.equal(JSON.stringify(plans),original);
});
test('Chart order respects phase, operation, job-inferred part and typed variable selection',()=>{
 const {c,nodes}=fixture();nodes.reportPartNum.value='';nodes.reportJobNum.value='J61033';
 nodes.reportInspectionPhase.value='Setup';assert.deepEqual([...c.reportCharacteristicCandidates()],['Width']);
 nodes.reportInspectionPhase.value='In Process';nodes.reportOperationCode.value='Production|20';assert.deepEqual([...c.reportCharacteristicCandidates()],['Other operation']);
 nodes.reportCharacteristicName.value='Angle';assert.deepEqual([...c.reportCharacteristicCandidates()],['Angle']);
 nodes.reportCharacteristicName.value='';nodes.reportInspectionPhase.value='End of Spool';assert.deepEqual([...c.reportCharacteristicCandidates()],[]);
});
test('All phases de-duplicate variables using earliest configured position',()=>{
 const {c,nodes}=fixture();nodes.reportInspectionPhase.value='';assert.deepEqual([...c.reportCharacteristicCandidates()],['Material Thickness','Width','Angle']);
});
