import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
const html=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/index.html',import.meta.url),'utf8');
const css=fs.readFileSync(new URL('../../src/SPCStar.Api/wwwroot/styles.css',import.meta.url),'utf8');
test('all data filters use the common grid before JavaScript runs',()=>{
  for(const id of ['qualityForm','reportForm','topIssuesForm'])assert.match(html,new RegExp(`id="${id}" class="[^"]*data-filter-grid`));
  assert.match(html,/class="review-filter-bar data-filter-grid"/);
  assert.doesNotMatch(css,/\.quality-filters/);
  assert.match(css,/#dataPanel \.data-filter-grid\s*\{[^}]*grid-template-columns: repeat\(3, minmax\(0, 1fr\)\)/);
  const version=fs.readFileSync(new URL('../../Directory.Build.props',import.meta.url),'utf8').match(/<Version>([^<]+)<\/Version>/)[1];
  assert.ok(html.includes(`styles.css?v=${version}`));
});
