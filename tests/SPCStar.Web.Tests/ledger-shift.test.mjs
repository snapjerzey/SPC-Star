import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js',import.meta.url),'utf8');
test('Ledger shift filters inspection events without changing dates or phases',()=>{
  const c=vm.createContext({ledgerItemMatchesOperation:()=>true,ledgerItemMatchesPhase:()=>true});
  const start=source.indexOf('function filterLedgerItemsByDate(');
  vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),c);
  const rows=[{id:1,timestamp:'2026-10-19T12:00:00Z',operatorShift:'1st Half Days'},{id:2,timestamp:'2026-10-19T12:00:00Z',operatorShift:'1st Half Nights'}];
  assert.deepEqual(Array.from(c.filterLedgerItemsByDate(rows,{shift:'1st Half Days'},'timestamp'),r=>r.id),[1]);
  assert.equal(c.filterLedgerItemsByDate(rows,{},'timestamp').length,2);
  assert.equal(c.filterLedgerItemsByDate(rows,{shift:'5 Day'},'timestamp').length,0);
});
test('Ledger shift is passed to both reviews and XLSX export',()=>{
  assert.match(source,/review\/job\?\$\{query\}/);
  assert.match(source,/review\/part\?\$\{query\}/);
  assert.match(source,/query.shift = query.operatorShift/);
  assert.match(source,/shift: \$\("reviewShift"\)\.value \|\| null/);
  assert.match(source,/\$\("reviewShift"\)\.addEventListener\("change", loadReview\)/);
});
