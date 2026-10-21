# SPC-Star Line Tech SOP

Updated October 21, 2026.

## 1. Sign in and identify the work

Open SPC-Star, enter your own username/password, select your working shift and Sign In. Check the displayed user. Change password is on the sign-in page; ask QA for a reset if needed. Never share credentials or leave your account for someone else.

Use the Operator SOP for the complete entry procedure. Line techs may inspect, make authorized User Error corrections and sign off failed inspections using their own accounts. Line Tech access does not include Setup or Data reports. Do not use another person's login to obtain an unavailable function.

## 2. Check the job before supporting production

1. Confirm Job Number, Machine, Part Number, Operation and Inspection Phase against the assigned work.
2. Click Load and check the context above Inspection Items. Different operations have separate requirements.
3. Confirm the required initial inspection is submitted before advancing phases. Read a phase-block message rather than bypassing it.
4. Ask QA to review earlier saved inspections or failure records when those records are needed for troubleshooting.

The part/job fields suggest matching records. If the intended job is missing, confirm the numbers and access with QA; do not choose an unrelated job or phase to obtain an entry screen.

## 3. Confirm job data and materials

Check required job data and Save Job Data confirmation. Verify Wire Shipment Number against the material identification. Saved job data persists across phases and sign-ins until changed and saved.

Review every material field and Current lot. Required initial lots must be saved with Material issue at job start before entry begins. For a replacement, select Material change, enter the new lot in the correct row and Save Material Lot. Check the displayed Current lot afterward.

Separate rows represent separate inputs or lot identities even if part numbers match. Do not combine supplier/internal lots or assume one row covers another. Ask QA to correct a wrong saved lot; do not invent another material issue to hide the mistake.

## 4. Support measurement entry

Check Machine Counter and the adjacent due message. Items can have different frequencies. Tab/Enter follows the displayed order. Enter a complete Box # where required; do not alter counters to force due inspections.

Use the machine's actual counter. It may start at any number and must not go backward. After a tooling changeover, complete the required Setup and Startup before In Process. Checks stack from the run's starting counter: with a start of 100, checks every 2,000/4,000/8,000 fall at 2,100, 4,100 and 8,100, including the smaller groups due at each point. A late inspection or Coil Change does not move the schedule. A failure requires an immediate repeat, as described below.

Follow item order, tool, units, limits and sample/location labels. Enter the actual measurement including trailing zeros. Automatic advance waits one second after the expected decimals; another digit restarts the pause. Tab/Enter completes the entry when needed. Box # is not limited to a fixed digit count and is completed with Tab/Enter.

Attributes use one Accept/Reject lot choice, but the operator still checks the required sample quantity. Any sample failing requires Reject.

For indicators, select the field before transmission and verify the full reading without an added prefix. Connect Gauge is for a configured serial gauge; select the assigned device and check status. Ask QA to check the machine profile if output is wrong. Do not guess baud rates or reduce precision to make entry work.

For Hole Diameter pin entry, check the assigned block. SPC-Star stores the corresponding diameter; pin 7 fails. For calculated Removal, check the matching Raw and Polished readings: Removal is Raw minus Polished. Do not overwrite a calculated result with a convenient number.

## 5. Interpret OK, WATCH and failure

Green OK reflects the current accepted result. Yellow WATCH warns of drift or a result near a limit. Read its reason, review Trend and investigate before bad parts are produced. WATCH is advisory, not a lock or failed-inspection count. Recovery follows the configured rule; one normal reading does not necessarily clear it.

Red identifies a failed reading or rejected attribute. The lock message identifies the item and violation. LSL/USL are acceptance limits, not adjustable troubleshooting settings. Do not change limits, counts or rules to clear a production problem.

## 6. Clear only an entry mistake

1. For their own mistaken entry, the operator selects User Error, enters their own credentials and clicks Clear Lock. Line Tech access is not required for their own entry correction.
2. Assist if the operator needs help confirming the actual reading. An authorized Line Tech or QA may clear another user's mistaken entry using their own credentials.
3. Cause details and Action taken are not required for User Error.
4. Have the operator correct immediately, check the new status and continue the same inspection.

User Error does not count as a failed inspection or a separate lock-clear event. The corrected inspection must still be submitted. If the clear or submission is rejected, read the returned message; a closed panel alone is not proof the server accepted the request.

Refresh Lock Status checks the current server lock. Reset Unlock Form clears the form entries, not the lock. Neither bypasses a failed inspection.

## 7. End a genuinely failed inspection

1. Select Machine, Tooling or Material according to the actual problem.
2. Enter Cause details describing the condition found. Do not guess; state when the specific cause is still unknown.
3. Enter Action taken describing the actual response. Follow the normal nonconforming-product instructions outside SPC-Star as well.
4. Enter your Line Tech username/password to sign off, then click End Failed Inspection. Check confirmation and the blank inspection in the same job context. Ask QA to verify the saved failure if status is unclear.
5. Resolve the process problem before starting the fresh inspection.

Clear Lock is unavailable for these categories. If there is no active lock but the current inspection has failed, use the red End Inspection button and complete its failure form. It is not a cancel button for an unfinished draft that has not failed. A failed ending does not authorize release of bad product.

The Line Tech enters the failure details and signs off. An operator may do this only with supervisor-approved Line Tech access assigned to their account. After the problem is resolved, repeat immediately using the current Machine Counter, including the same count as the failure. The repeat remains due until a successful inspection; unrelated items retain their own frequencies.

## 8. Verify submission and numbering

Have the operator finish required entries, click Submit Inspection and check the success message. If success is not confirmed, stop and ask QA to check the saved status. Saved individual readings are not a submitted inspection.

Numbers continue separately for the job, part, operation and phase across sign-ins. Submitted completions and explicitly ended failures count; unsubmitted drafts do not. Investigate apparent duplicates before making replacement entries. After quantity-based submission, check the next-due message against each item's frequency.

## 9. Review the inspection screen and record what was done

Review the current inspection, lock message and material Current lot. Give QA the details needed to investigate saved readings or correct material records. Line Tech access does not include Data reports.

Trend can show Individuals, Run Chart, Moving Range, Histogram or Control / Spec Limits. These show reading order, adjacent changes, distribution or configured boundaries. The legend distinguishes normal, WATCH and failed measurements; a red point identifies a failed reading, not every reading in that job.

Job Capability summarizes saved readings across phases and machines for that job/operation/variable. Read the data counts and limits before drawing conclusions. A blank index can mean insufficient data, zero variation, a one-sided specification or conflicting limits. Capability never authorizes accepting an individual bad part.

Use the inspection screen's Note field and Submit Note for adjustments, findings and unresolved concerns. Check the save confirmation. Notes remain associated with the job across phases. Ask QA to retrieve saved notes or export records when needed.

Job History on Inspect shows notes and responses. When history loads again, including after an inspection is submitted, a note with a new response moves to the top. The note keeps its original author/date; each response shows its own author/date. Responses do not update automatically. Reload the job when no entry is in progress to check for replies. Ask QA to respond through the central notes list.

## 10. Escalate and hand off

Escalate repeated failures, wrong requirements, missing measurements, unexpected numbering, incorrect material identities, device errors, failed submissions or unavailable correction fields. Give QA the job, part, operation, phase, inspection number, item, value, message and time. Do not reconstruct missing records or guess data.

For pending/offline entries, ask QA to verify saved status after connection returns. Before switching computers or handing off, check submission confirmations and communicate unfinished work. Sign Out before the next person uses the station.

A browser refresh or routine server restart normally keeps a valid sign-in. If Sign In Required appears, enter your password in that prompt; the current entries stay on screen. Retry the interrupted save or submission after signing in. It is not sent automatically. Do not refresh or sign out to dismiss the prompt and risk losing unsaved work.
