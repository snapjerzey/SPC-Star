# SPC-Star SOPs

Updated October 21, 2026.

The login screen provides Operator, Line Tech, and QA SOPs in English and Spanish. Edit the six Markdown sources in this folder, run `node tools/prepare-sops.mjs`, then run `python tools/export-sops.py --publish-json .tmp/sop-content.json` to update the matching sections in `src/SPCStar.Api/wwwroot/index.html`. Use `--downloads <folder>` to regenerate matching Word copies. Run the SOP web tests to check synchronization and role boundaries.

The current instructions cover sign-in and session recovery, job data and materials, actual machine counters and stacked frequencies, measurement precision, User Error versus genuine failures, immediate repeats, notes and replies, role permissions, reporting, rule configuration and read-only Change History. Certificate workflows are not included.

Keep operator instructions brief and focused on using the software. Do not include private account names, administrator access details, part-specific examples, or implementation details in the operator SOP. Machine, Tooling, and Material failures must be ended as failed, not cleared. User Error alone permits an authorized correction.

Verify setup changes against current approved source documents and confirmed requirements. Preserve exact limits, one-sided specifications, and absent targets; do not invent values. Company source sheets and configuration workbooks stay outside GitHub.

Local SOP edits reach the pilot only through IT's next packaged patch. For the one-script Windows Service update process, see `deploy/IT-SERVER-REFERENCE.md`. Historical patch-verification documents remain historical records, not current operating procedures.
