import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
function fixture(){
  const fields={partReviewFilter:{value:''},reviewJobNum:{value:''},reviewJobOptions:{},reviewPartOptions:{},jobOptions:{values:['unchanged']}};
  const context=vm.createContext({state:{snapshot:{parts:[{partNum:'70306'},{partNum:'703060'},{partNum:'61046'}],jobs:[{jobNum:'1',partNum:'70306'},{jobNum:'2',partNum:'70306'},{jobNum:'3',partNum:'61046'},{jobNum:'4',partNum:'703060'}]}},
    $:id=>fields[id],fillDatalist:(field,rows,value)=>{field.values=Array.from(rows,value);}});
  for(const name of ['workJobPartChoices','partNumForJob','refreshReviewJobPartChoices','updateReviewJobFromPart','updateReviewPartFromJob']){
    const start=source.indexOf(`function ${name}(`);vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),context);
  }
  return {context,fields};
}
test('Ledger part filters its jobs, without modifying other screens',()=>{
  const {context,fields}=fixture();fields.partReviewFilter.value='70306';context.updateReviewJobFromPart();
  assert.deepEqual(fields.reviewJobOptions.values,['1','2']);assert.deepEqual(fields.jobOptions.values,['unchanged']);
  fields.reviewJobNum.value='1';fields.partReviewFilter.value='61046';context.updateReviewJobFromPart();
  assert.equal(fields.reviewJobNum.value,'');assert.deepEqual(fields.reviewJobOptions.values,['3']);
});
test('Ledger job selects its part and partial job filters part suggestions',()=>{
  const {context,fields}=fixture();fields.partReviewFilter.value='61046';fields.reviewJobNum.value='1';context.updateReviewPartFromJob();
  assert.equal(fields.partReviewFilter.value,'70306');assert.deepEqual(fields.reviewPartOptions.values,['70306']);
  fields.partReviewFilter.value='';fields.reviewJobNum.value='3';context.refreshReviewJobPartChoices();
  assert.deepEqual(fields.reviewPartOptions.values,['61046']);
  fields.reviewJobNum.value='';context.refreshReviewJobPartChoices();assert.equal(fields.reviewPartOptions.values.length,3);
});
