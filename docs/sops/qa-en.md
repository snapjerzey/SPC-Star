# SPC-Star QA SOP

Updated October 21, 2026.

## 1. Sign in and navigate

Sign in using your own username/password and actual shift. Check the displayed user. Change password is on the sign-in page; enter current and new passwords, confirm and Save password.

Inspect is the production-entry screen. Setup provides Parts & Inspections, Users, Machines and Rules according to permissions. Data provides Ledger, Part Quality, Charts and Top Issues. Use the Operator SOP for the full inspection process and the Line Tech SOP for troubleshooting. Never borrow credentials for an unavailable function.

QA can export information. Data and configuration imports are not available to QA.

Admin provides Notes and Change History for QA. QA - Read only can view and export Data but cannot edit inspection records, maintain setup, or post responses in Admin > Notes. The maintenance and correction steps below require regular QA access.

## 2. Review an inspection with an operator

Confirm Job Number, Machine, Part Number, Operation and Inspection Phase, then Load. Complete the required initial inspection before advancing phases. Do not use another operation/phase to bypass a blocked load.

Check Save Job Data and Save Material Lot confirmations. Issue required initial lots with Material issue at job start; use Material change for replacements. Job data and lots persist across phases and sign-ins until changed and saved. Distinguish each material field by name as well as part number, especially supplier/internal lot fields. Verify Current lot after saving.

Machine Counter controls quantity-based due messages. Box # is entered completely. Follow item order and sample/location labels. Saved LSL/USL precision controls the expected measurement decimals; advance waits one second for further typing. Tab/Enter finishes an entry. Connected indicators must deliver the full reading without an added prefix.

Use the actual machine counter, which can start at any number and must not go backward. Required Setup and Startup establish the current run after a tooling changeover. Quantity checks stack from that starting count: if it is 100, 2,000-part checks fall at 2,100; at 4,100 include both the 2,000- and 4,000-part checks; at 8,100 include the 2,000-, 4,000- and 8,000-part checks. Use only the frequencies configured for the part. Late or extra inspections and Coil Change do not shift these scheduled points.

Attributes use one Accept/Reject for the lot while retaining the required quantity to check. Calculated Removal uses corresponding Raw minus Polished readings. Pin-based Hole Diameter stores the diameter associated with the entered pin and assigned block. Do not alter a requirement to make an incorrect entry pass.

When performing an inspection, finish all required fields, click Submit Inspection and check the success confirmation and Job History. Numbering continues by job, operation and phase across sign-ins. On-screen readings or a pending request are not proof of submission.

## 3. Correct User Error or end a failed inspection

1. Read the lock message and verify the failed measurement or attribute.
2. An operator clears their own entry mistake by selecting User Error and entering their own credentials to Clear Lock. Line Tech access is not required. A Line Tech or QA can assist with another user's entry mistake. Correct immediately and continue the same inspection. Cause/action text is not required; it does not count as a failure or separate lock-clear event.
3. For a real failure, the Line Tech selects Machine, Tooling or Material, enters Cause details and Action taken, and signs off with their credentials before End Failed Inspection. QA may also perform this authorized signoff. An operator may do so only with supervisor-approved Line Tech access assigned to their account. Clear Lock is unavailable for these categories.
4. Verify the red failed record, retained readings/reason and fresh blank inspection in the same job/operation/phase.

End Inspection opens the failure form when no lock is active. It is not a cancel button. Follow the separate process for nonconforming product; the software does not authorize product release. Refresh Lock Status rechecks the server; Reset Unlock Form clears the form, not the lock. Investigate any returned rejection instead of assuming success.

A failed inspection remains due for an immediate repeat after resolving the problem. Use the current Machine Counter, even if unchanged; do not wait for the next scheduled count. A successful repeat restores the normal schedule. Checks not involved in the failed inspection keep their own frequencies.

## 4. Maintain Parts & Inspections

1. Open Setup > Parts & Inspections. Choose the existing part under Edit Existing Part and Load Part before editing.
2. Check Part number, Part description, Product group and Operation. Maintain Blank Code/Hole Size where applicable. Finished goods, blanks and input materials need their correct descriptions.
3. Use Job Data Fields for required persistent job information. Maintain the field names, required settings and operator instructions for that part.
4. Use Materials for each input's label, part number, description and Required setting. Keep separate inputs and lot identities in separate rows; never combine different material part numbers on one line.
5. Use Inspection Items for name, Variable/Attribute type, Requirement / context, Tool and Unit. Retain source instructions. Set actual required phases, physical samples, measurement locations, order and frequency for that operation.
6. Enter LSL/USL from the current approved inspection sheet and confirmed requirements, including trailing zeros. Leave absent targets and one-sided limits blank. Do not calculate midpoint targets or copy specs into LCL/UCL. Use control limits only when supplied by the source.
7. Select the appropriate drift rule. Save Part Inspection, read the confirmation, reload the part and verify its operator display.

Verify changes against the current approved source, not an old setup export or backup. Export Parts & Inspections provides a setup copy; QA does not import configuration.

A minimum-only reading passes at or above LSL; a maximum-only reading passes at or below USL; two-sided readings at either limit pass. Preserve limit decimals rather than adding precision workarounds. Clear resets the setup form without saving.

Follow source sheets and confirmed operating decisions for phases. Test Polish is a separate operation. Multiple sides/locations need their own readings. One attribute lot choice does not reduce the physical sample requirement.

## 5. Maintain Users and Machines

In Users, expand the appropriate access-level group and select an account, or select New User. Enter username, an available access level, shift and allowed product groups. Save User and check the result. Only assign access levels offered to your account. Reset Password applies to the selected user; provide it only to that person. Use Delete User only for intentional account maintenance, not correcting inspection records. Do not create shared accounts or assign access beyond the person's work/training. Export Users provides a reference list; bulk import is not a QA function.

Assign Line Tech access to an operator only after their supervisor approves it. Use the person's own account; do not share a Line Tech login to obtain signoff.

In Machines, select a machine or New Machine. Check Machine ID, Description and Allowed product groups. Blank group restrictions allow all groups. Save Machine, then verify the operator's available list. Select Keyboard input or Serial text gauge to match the equipment; use the device's specified baud rate, not a guess. Export Machines provides a reference list. Delete removes a machine record, not job history.

## 6. Select a drift rule

In Setup > Rules, choose Global default rule set and Save Global Rule. Items using Use Global Default inherit it; explicit item rules override it. Review Details before selecting a rule. A global change may affect many parts.

Spec Limit Only checks measured values against specifications without drift patterns. No Automatic Rule disables measured-variable automatic checks and must not be selected casually; rejected attributes still lock. Other families use their displayed patterns and data requirements. Do not invent control limits for a rule.

## 7. Configure Custom Rule

Click Configure on Custom Rule. Set its name and the following controls, then review the summary and Save Custom Protocol:

1. Enable custom Watch rules enables its advisory warnings. Disabling it does not remove individual out-of-spec checks for an item using Custom Rule.
2. Directional Drift uses one variable average per completed inspection. Select consecutive completed inspections and upper, lower or either-limit direction. The first average starts comparison; later averages must move in the selected direction. Choose whether equal averages and reversing averages restart the count.
3. Near Specification Limits checks individual samples in a warning zone. Use percent of LSL-to-USL span or exact distance inside a limit. One-sided specs require exact distance because no two-sided span exists.
4. Repeated Near-Limit Inspections sets how many of the last completed inspections contain a warning-zone sample. Upper/lower results count separately.
5. Show Watch when uses Any or All enabled conditions. With All, the combined warning is evaluated on submission.
6. Normal completed inspections before OK sets recovery. All enabled conditions must clear; a new warning restarts recovery.
7. Add protocol notes and verify the item uses Custom Rule or inherits a Custom global default.

Sides/spindles remain separate variables. Incomplete/failed inspections do not count as completed trend averages; sample size one uses its reading. WATCH is yellow and advisory; individual out-of-spec samples still lock under Custom Rule. WATCH returns to OK after configured recovery. Warning measurements are yellow in Ledger/export; standalone WATCH events are not added to Job History.

## 8. Data > Ledger and corrections

Ledger, Part Quality, Charts and Top Issues use the same filters: Part, Job, Operation, Phase, Machine, Shift, Variable, From and To. Selections carry between these tabs. Blank optional fields and All options include every matching value. Run the selected view after changing filters; Ledger Export XLSX uses the Ledger selection.

1. Open Data > Ledger. Enter a part, a job or both. Suggestions link matching parts/jobs.
2. Choose Operation, Phase, Shift and dates as needed, then Review Job. All shifts includes every shift. The selected shift applies to Ledger history, capability summaries and Export XLSX. A part-only review covers matching jobs; a selected job shows Job Capability and Inspection / Job History.
3. Open Details for readings and context, not only Machine Counter. Check operator, time, operation, phase and sample identities.
4. Review red failures for the failed reading/attribute, violation, cause category, original Cause details and Action taken. User Error and standalone WATCH are not genuine failed inspections.
5. Use authorized history edits for saved readings/Machine Counter, or Edit Lot for a material record. Check the saved change. Do not create a new inspection or guessed link to repair old data.
6. Export XLSX exports the selected review. Check filters and use Job Notes for saved notes from the selected job/date scope, including other phases.

Numbering continues by job, part, operation and phase across sign-ins. Only submitted completions and explicitly ended failures count. Unsubmitted readings cannot become invented completions. Partial failures contain only readings actually taken. Escalate missing links; do not reconstruct them from timestamps.

## 9. Data > Part Quality

Enter Part and optional Job, Operation, Phase, Machine, Shift and dates, then Load Summary. Read finalized-inspection totals and variable rows in inspection order. Find variable narrows the table.

Select a variable row or use the Variable selector for its failures. Review phase, readings, violation, cause category, cause details, action, notes, job, machine, counter and operator. Previous/Next shows more failures for that variable rather than every failure in the system.

Part pass rate is passed finalized inspections divided by finalized inspections, not scrap rate. Variable counts reflect inspections actually checking the variable, not sample fields or all produced pieces. Unreached variables in a partial failure are not counted as inspected. Missing-data notices require investigation, not invented readings or reasons.

## 10. Charts and Job Capability

In Data > Charts, choose part/job and any available operation, machine, variable, phase/date filters. Choose chart type and Run Report. Check whether the report covers one phase or All phases. Variables follow inspection order.

Individuals and Run Chart show readings in order. Moving Range shows adjacent changes; Histogram shows distribution; Control / Spec Limits shows configured boundaries. The legend marks normal, yellow WATCH and red failed measurements. A red point means that measurement failed, not all readings in the inspection.

Job Capability displays minimum, maximum, mean, standard deviation and available Cp/Cpk/Pp/Ppk. The inspection panel and Ledger job summary include saved readings across phases/machines for the same job, operation and variable. Genuine failures remain; unsubmitted drafts are not completed data. Compare identical filters and limits before comparing reports.

Cp/Cpk use within-inspection variation; Pp/Ppk use overall variation. Missing limits, little data, zero variation or differing phase limits can leave indices blank. One-sided specs can support Cpk/Ppk but not two-sided Cp/Pp. Indices are not a global pass/fail verdict or product-release authorization; review stability and process suitability.

## 11. Top Issues and exports

In Data > Top Issues, select the available part/job/machine/shift/variable/date filters and result limit, then Run Top Issues. Use it for recurring out-of-spec or rejected-attribute events. Custom WATCH and User Error corrections are excluded. Review explanations in Ledger or Part Quality.

Ledger > Export XLSX provides detailed inspection records and Job Notes.

## 12. Read and respond to job notes

1. Open Admin > Notes. Use Search to find note or response text. Part, Job, Machine, Posted by and Replied by narrow the list. Click Show Notes after changing filters; Clear Filters removes them.
2. From and To apply to the original note's date, not the response date. Leave them blank when searching for a recent response to an older note. Use Previous/Next for more notes.
3. Read the original note and its responses. Click Reply, enter the response and click Send Response. Check the saved response and your displayed name/date. Responses are limited to 1,000 characters.
4. The original author, note and date remain unchanged. Responses cannot be edited or deleted here. The operator sees the response under that note in Inspect > Job History after history loads again, including after submitting an inspection. It does not appear automatically on an unchanged screen.

Notes do not replace failure Cause details or Action taken. A response provides information; it does not clear a lock, correct a reading or complete an inspection.

## 13. Review Change History

Open Admin > Change History. Filter by Search, Changed by, Area, Part, From or To, then click Show Changes. Review who saved the change, when, what was changed and the old/new values where provided. Use Previous/Next for more entries or Export CSV for the selected results. Imports appear as summary entries. This view is read-only; it does not undo changes. Unsaved form edits are not logged.

## 14. Save, escalate and sign out

Read every save confirmation. Escalate problems with job, part, operation, phase, inspection number, item, actual value, time and message. Pending/offline readings are not submitted inspections. Do not delete history, invent entries or change specs to bypass a failure.

Before handoff, save notes and corrections and communicate unresolved problems. Sign Out when finished.

A browser refresh or routine server restart normally preserves a valid sign-in. If Sign In Required appears, enter your own password in that prompt. Current entries remain on screen. Retry the interrupted action after signing in; the software does not automatically replay a failed save or submission. Do not refresh or sign out to dismiss the prompt and risk losing unsaved work.
