# SPC-Star Operator SOP

Updated October 21, 2026.

Use this procedure to load a job, record its inspections, correct entry mistakes and report problems.

## 1. Open SPC-Star and sign in

Open the SPC-Star address provided for your work area. The sign-in page has Operator SOP, Line Tech SOP and QA SOP buttons. Select English or Spanish in the open SOP.

1. Enter your own User name and Password.
2. Select the Shift you are actually working and click Sign In.
3. Check your name at the top. Never enter inspections under another person's account or share your password.

Use Change password on the sign-in page to change your password: enter your username, current password and new password twice, then Save password. Ask QA for a reset if you cannot sign in.

## 2. Select and load the correct inspection

1. In Inspect, enter Job Number and select the Machine you are running.
2. Enter or scan Part Number. Part and job suggestions narrow to matching records. If the intended job is absent, check the number with your line tech; do not use a different job.
3. Select the Operation for the work performed. A separate operation, such as Test Polish, has its own inspections.
4. Select the required Inspection Phase and click Load.
5. Check the job, machine, part, operation and phase above Inspection Items before entering results.

Complete and submit the required initial inspection before moving to the next phase. If SPC-Star blocks a phase, read the message and finish the missing inspection. Setup, Startup, In Process, Coil Change, Spool and End of Spool are not interchangeable. Not every operation uses every phase. Do not select an unrelated phase to bypass a blocked inspection.

## 3. Save job data and issue materials

Complete the part-specific fields under Job Data. Click Save Job Data and check the confirmation. For Wire Shipment Number, compare the number with the material identification before saving. Job data remains across phases and later sign-ins until changed and saved.

1. In Materials, read every material name, part number and description.
2. At job start, enter each required lot and select Material issue at job start. Click Save Material Lot.
3. Check that Current lot shows the saved lot for every required row. Required initial lots must be saved before inspection entry begins.
4. When material changes, enter the replacement lot in the correct row, select Material change and click Save Material Lot. Check Current lot again.

Separate rows require separate entries, including internal and supplier lot fields for the same part. Saved lots remain valid across phases and sign-ins until replaced. Do not reissue unchanged lots for every inspection. Ask your line tech or QA to arrange a recorded correction if a saved lot is wrong.

## 4. Enter Machine Counter and Box #

Enter the current Machine Counter completely. Read the message directly beside it: it shows whether inspection is due or the next count at which it is due. Press Tab or Enter to continue through the displayed inspection order. Different items can have different frequencies; follow their due messages.

Enter the complete Box # when required. Use Tab or Enter when finished. These are count/identification fields, not dimensions. Do not change a count to force a different inspection schedule.

Use the machine's actual counter, not a count starting at zero for the job. A job can begin at any number. Counts must not go backward. After a tooling changeover, perform the required Setup and Startup before resuming In Process.

Quantity checks stack from the run's starting counter. For example, if the starting counter is 100 and checks are required every 2,000, 4,000 and 8,000 parts: at 2,100 do the 2,000 checks; at 4,100 do the 2,000 and 4,000 checks; at 8,100 do all three groups. Use the frequencies displayed for your part. A late inspection or Coil Change does not move these scheduled points. Follow the separate repeat instructions below after a failure.

## 5. Read the inspection item

Work in the displayed order. Read the item name, requirement, tool, unit, phase, sample size and frequency. Check the sample labels for sides, legs, spindles or other locations so each reading goes in the correct field.

LSL is the lowest acceptable reading and USL the highest. A reading equal to either limit passes. A minimum-only item has no upper limit; a maximum-only item has no lower limit. A blank Target is not a missing operator entry. LCL/UCL, if shown, are process-control limits, not substitutes for specification limits.

If a requirement, limit, sample count or phase looks wrong, stop and ask your line tech or QA. Do not invent a limit or enter a convenient number to continue.

## 6. Enter measurements and attributes

1. Measure with the listed tool and enter the actual reading, including decimal places and trailing zeros.
2. If a decimal prefix is provided, complete that reading rather than adding a second prefix. Device-fed fields should receive the full device reading without an added prefix.
3. A dimension advances after the expected decimals and a one-second pause. Another digit restarts the pause. Tab or Enter finishes the entry when needed, including readings with fewer digits. Check the value before moving on.
4. For an attribute, inspect the required quantity and select one Accept or Reject for the lot. Any checked piece failing requires Reject. The choice advances to the next entry.
5. Complete every required sample and location. Never copy a reading into another sample unless it is that sample's actual result.

For a keyboard-connected indicator, select the sample field and send the reading. If Connect Gauge appears for a configured serial gauge, click it, choose the assigned device and check the connection status. Use Disconnect when finished. If a reading appears in the wrong field or is joined to an existing prefix, stop and ask for help; do not change device settings yourself.

Where Hole Diameter uses a pin entry, use the assigned gage block and enter the pin used. SPC-Star stores that pin's configured diameter. Pins 1 through 6 pass; pin 7 fails. Where Removal is calculated, enter the matching Raw and Polished readings and check the calculated result; do not overwrite it.

## 7. Understand the colored rail

Green OK means the current result is acceptable. Yellow WATCH means the configured rule detected drift or a result near a limit. Read its reason and notify your line tech or QA. WATCH alone does not lock inspection; it returns to OK when the configured recovery conditions are met.

Red means a failed reading or rejected attribute. Corrected entries are evaluated using their new values. Grey NEW or EDIT is not proof of acceptance or submission; read any message and finish or correct the entry.

## 8. Correct an entry mistake after a lock

1. Read the lock message to identify the item and failed value.
2. For your own mistaken entry, not an actual bad part, select User Error yourself. Enter your own username and password and click Clear Lock. You do not need Line Tech access to correct your own entry mistake.
3. Correct the reading immediately and continue that same inspection. User Error does not count as a failed inspection.

Do not use User Error to accept a genuinely out-of-spec part. If the lock remains or correction is not possible, read the error and ask your line tech or QA. Do not keep trying to submit against an active lock.

## 9. Record an actual failed inspection

1. Stop entry and call your Line Tech for an actual failed inspection.
2. The Line Tech selects Machine, Tooling or Material, enters factual Cause details and Action taken, and enters their username/password to sign off.
3. The Line Tech clicks End Failed Inspection. Check the confirmation and fresh blank inspection for the same job, operation and phase. Ask QA to review the saved failure if confirmation is unclear.
4. An operator may perform this signoff and enter the failure details only if their supervisor has approved Line Tech access and that access is assigned to their account. Otherwise the Line Tech must do it.

These causes do not allow Clear Lock. Resolve the actual process problem and follow work instructions before starting again. Ending a failed inspection does not authorize release of nonconforming parts.

After resolving the problem, repeat the failed inspection immediately at the current Machine Counter, even if that count has not changed. Do not wait for the next scheduled count. The failed inspection does not satisfy the requirement; successful submission of the repeat returns it to the normal schedule.

If the inspection has genuinely failed but no lock is open, the red End Inspection button opens the same failure form. Have the Line Tech enter the details and sign off as above. This records a failure; it is not a cancel or pause button.

## 10. Submit and verify

1. Check Machine Counter, Box # when required, saved job data/materials and every required sample.
2. Click Submit Inspection once and wait for the success confirmation.
3. Read the submission message. If it shows an error or does not confirm success, stop and notify your line tech or QA.
4. Start the next inspection only after receiving the success confirmation.

Numbering continues for the same job, operation and phase after sign-in on another day. Green fields or readings on screen do not mean the inspection was submitted. If submission fails, follow the message or contact your line tech/QA. Do not create a replacement inspection until you know the first one's status.

## 11. Trend and job notes

Trend shows saved job readings for the selected variable. Select the variable and chart type to review changes over the job. Job Capability summarizes saved measurements; it does not authorize accepting an individual bad reading. Ask your line tech or QA to investigate a concerning trend.

Use the Note field on the inspection screen and click Submit Note. Check the save confirmation. Notes remain associated with the job across phases and can explain adjustments, repairs or concerns. Ask QA to review saved records when needed. Notes do not replace the failure form's cause and action fields.

Read notes and responses in Job History on the Inspect screen. A note with a new response moves to the top when Job History is loaded again, including after submitting an inspection. The original note keeps its author and date; the response shows who replied and when. Replies do not appear automatically while you leave the screen unchanged. Reload the job when no inspection entry is in progress to check for responses. Operators do not have access to the separate Data reporting tabs.

## 12. Handoff, connection problems and sign out

Before leaving, make sure you received the success confirmations for submitted inspections, material changes and notes. Tell the next operator about unfinished work or unresolved issues. Do not assume an unfinished entry transferred to another device or was submitted; ask QA to confirm saved status before re-entering anything.

If SPC-Star is offline or reports pending/rejected entries, stop and notify your line tech or QA. After the connection returns, have QA confirm saved status before continuing. Never assume a pending entry is saved.

Click Sign Out when finished or handing over the station. The next person must use their own account.

A browser refresh or routine server restart normally keeps a valid sign-in. If Sign In Required appears, enter your own password in that prompt. Your current entries stay on the screen. After signing in, retry the save or submission that was interrupted; it is not sent automatically. Do not refresh or sign out to dismiss the prompt, because that can discard unsaved work.
