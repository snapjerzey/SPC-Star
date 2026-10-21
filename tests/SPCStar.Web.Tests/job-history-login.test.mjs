import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const source=fs.readFileSync(new URL('../../src/SPCStar.Api/Program.cs',import.meta.url),'utf8');
test('new job note authors and timestamps come from the verified session and server, not the payload',()=>{
 const start=source.indexOf('app.MapPost("/jobs/{jobNum}/notes"');
 const handler=source.slice(start,source.indexOf('\n});',start));
 assert.match(handler,/auth\.CurrentUser\(context\.Request\.Headers\["X-SPCStar-User"\]/);
 assert.match(handler,/Headers\["X-SPCStar-Session"\]/);
 assert.match(handler,/if \(!actor\.Succeeded\)/);
 assert.match(handler,/OperatorUserId = actor\.Value!\.UserName, Timestamp = DateTimeOffset\.UtcNow/);
 assert(handler.indexOf('auth.CurrentUser')<handler.indexOf('service.Add'));
});
for(const route of ['notes','history']) {
 test(`job ${route} reads require a valid session before returning records without adding role restrictions`,()=>{
  const start=source.indexOf(`app.MapGet("/jobs/{jobNum}/${route}"`);
  assert(start>=0);
  const handler=source.slice(start,source.indexOf('\n});',start));
  assert.match(handler,/auth\.ValidateSession\(context\.Request\.Headers\["X-SPCStar-User"\]/);
  assert.match(handler,/Headers\["X-SPCStar-Session"\]/);
  assert.match(handler,/statusCode: 401/);
  assert(handler.indexOf('auth.ValidateSession')<handler.indexOf('service.GetForJob'));
  assert.doesNotMatch(handler,/UserHasPermission|CanExportQAData|CanViewChangeHistory/);
 });
}
