import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';
const source = fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/app.js', import.meta.url), 'utf8');
function fixture() {
  const context = vm.createContext({
    document: {createElement: () => ({children: [], append(...items) {this.children.push(...items);}})},
    formatDateTime: String, historyEntryUser: () => 'Operator'
  });
  for (const name of ['renderHistoryList', 'historyEntryTitle', 'failedInspectionHistoryText']) {
    const start = source.indexOf(`function ${name}(`);
    vm.runInContext(source.slice(start, source.indexOf('\nfunction ', start + 1)), context);
  }
  return context;
}
test('Job History omits individual locks and shows the failed inspection with its cause and action', () => {
  const context = fixture();
  const list = {children: [], appendChild(item) {this.children.push(item);}};
  context.renderHistoryList(list, [
    {entryType: 'Lock', characteristicName: 'Width'},
    {entryType: 'InspectionFailed', inspectionPhase: 'In Process', failureNumber: 6,
      detail: 'Width: Entered value 0.210 is above the upper specification limit 0.205',
      causeCategory: 'Tooling', causeText: 'Worn tool', solutionText: 'Replaced tool', measurementIds: []}
  ]);
  assert.equal(list.children.length, 1);
  assert.match(list.children[0].className, /failed-inspection-history-item/);
  const [meta, text] = list.children[0].children;
  assert.equal(meta.children[0].textContent, 'In Process inspection 6 failed');
  assert.match(text.textContent, /Width: Entered value 0\.210.*limit 0\.205/);
  assert.match(text.textContent, /Tooling - Worn tool/);
  assert.match(text.textContent, /Action: Replaced tool/);
});
test('A job with only lock audit records has no displayed inspection history', () => {
  const list = {};
  fixture().renderHistoryList(list, [{entryType:'Lock'}]);
  assert.equal(list.textContent, 'No history for this job yet.');
});

test('Ledger omits standalone lockouts but retains the actual failed inspection', () => {
  const rendered = [];
  const container = {};
  const context = vm.createContext({
    $: () => container,
    filterLedgerItemsByDate: items => items,
    renderReviewHistoryEvent: (_container, entry) => rendered.push(entry),
    reviewJobDataBelongsToCompletion: () => false,
    reviewTraceabilityEventBelongsToCompletion: () => false
  });
  const start = source.indexOf('function renderReviewMeasurements(');
  vm.runInContext(source.slice(start, source.indexOf('\nfunction ', start + 1)), context);
  const failure = {id:'failed',entryType:'InspectionFailed',timestamp:'2026-10-17',causeCategory:'Machine',causeText:'Broken fixture'};
  context.renderReviewMeasurements([], [{id:'lock',entryType:'Lock'},failure]);
  assert.deepEqual(rendered, [failure]);
  rendered.length = 0;
  context.renderReviewMeasurements([], [{id:'lock',entryType:'Lock'}]);
  assert.equal(rendered.length, 0);
  assert.equal(container.textContent, 'No job history for this job.');
});

test('Ledger failed inspection remains red and includes the recorded failure reason', () => {
  const rows = [];
  const context = vm.createContext({
    document:{createElement:()=>({querySelector:()=>null})},
    inspectionCompletionMeasurements:()=>[], inspectionCompletionJobData:()=>[],
    inspectionCompletionMaterials:()=>[], inspectionCompletionMetadata:()=>[],
    failedInspectionHistoryText: entry=>`Failed: ${entry.detail}. Cause: ${entry.causeText}. Action: ${entry.solutionText}.`,
    formatDateTime:String, escapeHtml:String, historyEntryTitle:()=> 'In Process inspection 6 failed',
    historyEntryUser:()=> 'Operator'
  });
  const start = source.indexOf('function renderReviewHistoryEvent(');
  vm.runInContext(source.slice(start, source.indexOf('\nfunction ', start + 1)), context);
  const container={appendChild:row=>rows.push(row)};
  context.renderReviewHistoryEvent(container,{entryType:'Lock'});
  assert.equal(rows.length,0);
  context.renderReviewHistoryEvent(container,{id:'failed',entryType:'InspectionFailed',detail:'Width above USL',causeText:'Worn tool',solutionText:'Replaced tool'});
  assert.match(rows[0].className,/measurement-out-spec/);
  assert.match(rows[0].innerHTML,/Width above USL.*Worn tool.*Replaced tool/);
});

test('Ledger date filters select inspections without dropping their earlier sample readings', () => {
  let linkedReadings;
  const context=vm.createContext({$:()=>({}),
    filterLedgerItemsByDate:items=>items.filter(item=>item.timestamp>='2026-10-17T10:01:00Z'),
    renderReviewHistoryEvent:(_container,_entry,measurements)=>{linkedReadings=measurements;}
  });
  const start=source.indexOf('function renderReviewMeasurements(');
  vm.runInContext(source.slice(start,source.indexOf('\nfunction ',start+1)),context);
  context.renderReviewMeasurements([{id:'earlier-sample',timestamp:'2026-10-17T10:00:00Z'}],
    [{id:'complete',entryType:'PhaseComplete',measurementIds:['earlier-sample'],timestamp:'2026-10-17T10:02:00Z'}]);
  assert.ok(linkedReadings.has('earlier-sample'));
});
