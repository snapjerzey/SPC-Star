# Schneider Inspection Sheet Import Mapping

This note documents how the Schneider inspection PDFs should be converted into the SPC-Star setup import template. The sample files reviewed were extracted from `Schneider test inspection sheets.zip`.

## Source Format

The Schneider examples are PDF inspection sheets and WinSPC/WSPC backup logs, not spreadsheet files. Text can be extracted, but the table layout is often scrambled by PDF extraction. The import workflow should therefore use extracted text for first-pass identification, then visual review for exact limits, sample counts, frequencies, and variable names before loading into SPC-Star.

## Current SPC-Star Template Columns

The current column-based setup template supports these rows:

| Row kind | Filled column | Purpose |
| --- | --- | --- |
| Job data | `Job Data Field` | Persistent fields shown in Job Data on the inspection screen. |
| Material | `Material Name` or `Material Part Number` | Materials operators must enter lot numbers for. |
| Variable | `Variable Name` | Numeric measured values with target, spec limits, control limits, sample size, and frequency. |
| Attribute | `Attribute Name` | Accept/reject checks such as visual, template, comparator, OK/NG, A/R, or go/no-go checks. |

## Standard Field Mapping

| Schneider source value | SPC-Star destination | Notes |
| --- | --- | --- |
| BOA part number, e.g. `70305`, `70306`, `70307`, `70309`, `70310`, `70311` | `Part Number` | Use the BOA/internal part number as the SPC-Star part number unless the final master part policy says otherwise. |
| Schneider description/title | `Part Description` | Example: `Schneider - Jaw, Spring, and Contact Assy.` |
| Schneider family/customer | `Product Group` | Use `Schneider` for these parts. This supports future user certification/security by product group. |
| `Start Up` / `Startup` | `Inspection Phase = Startup` | Start-of-shift requirements. |
| `Set Up` / `Setup` | `Inspection Phase = Setup` | Tooling changeover or setup requirements. |
| recurring production checks such as `3 pcs / 5,000` | `Inspection Phase = In Process` | These are the ongoing inspection frequency checks. |
| `Mach #` | built-in Job Data: Machine | Machine is already one of the required job data fields in the inspection screen. |
| `Job #` | built-in Job Data: Job Number | Job number is already a required field. |
| part number field | built-in Job Data: Part Number | Part number is already required. |
| selected inspection type | built-in Job Data: Inspection Phase | Phase drives which variables and attributes are shown. |
| `Date`, `Shift`, `Insp`, `Initials`, `WI`, `Box #`, `Bag #`, `Tote #`, `Pallet`, `Recorded in WinSPC or INSP`, `Start of Shift Box #`, `End of Shift Box #` | `Job Data Field` | Add only if the part/inspection really requires operator entry. |
| `BOA Lot #`, `Supplier Lot #`, `Supplier WO #`, `Wire shipment`, `Coil #` | `Job Data Field` only when required by the inspection plan | Do not infer job materials from the inspection sheet. Materials must come from a verified material/BOM source. |
| material/packaging references on the inspection sheet | no import mapping by default | Do not add these to the Materials section unless they are confirmed raw materials for the job from a trusted material source. |
| numeric dimensions with ranges or plus/minus tolerances | `Variable Name`, `Unit`, `Target`, `Lower Spec`, `Upper Spec` | Example `.050" +/- .001"` becomes target `.050`, lower `.049`, upper `.051`, unit `in`. |
| force or torque actual readings | `Variable Name`, `Unit`, limits | Examples: `Shear Force 280 lbs min`, `Clamp Plate Screw Torque 5 in-lbs`. |
| `A / R`, `OK / NG`, `Go/No-Go`, `Visual`, `Template`, `Comparator`, `Proper Material Orientation`, packaging checks | `Attribute Name` | These are accept/reject attributes unless the sheet clearly requires an actual numeric reading. |
| `3 pcs / 5,000`, `2 pcs / 5,000 per side`, `1 pc / 8,000` | phase-specific sample/frequency columns | Use columns such as `In Process Required`, `In Process Sample Size`, `In Process Frequency Type`, `In Process Frequency`, and `In Process Frequency Unit`. |
| `Start Up & Coil Changes (CC)` | phase-specific event columns | Do not create a separate `Coil Change` inspection phase. Use an event such as `MaterialChange` in the phase/frequency columns when the check is required. |
| tooling changeover requirements | `Setup Required`, `Setup Sample Size`, `Setup Frequency Type`, `Setup Frequency`, `Setup Frequency Unit` | Setup requirements should be attached to the item row, not copied as a separate repeated inspection block. |
| tools or gauges like Caliper, Micrometer, Comparator, Force Gauge, Keyence, GP gauges, or templates | `Inspection Method` | Keep this short. Do not import reference documents or long instructions into the setup template. |

## Schneider Patterns That Matter

- Many plans mention Front/Back, Lower/Upper, A/B, D/N, or side-specific sample pulls. Treat that as sampling context, not separate characteristics, unless the sheet has different limits or a different actual measurement for each side.
- A/R and OK/NG are attributes, not numeric variables. The operator screen should show Accept/Reject style inputs for these.
- Material thickness often appears as an actual reading taken at startup and coil changes. This should usually be a numeric variable, while the material itself is a material row where the operator enters the lot number.
- Packaging, pallet label, desiccant bag, box label, wash cycle, material orientation, ID stamp orientation, visual checks, and template checks are attributes unless a numeric value is explicitly required.
- The WinSPC logs often provide the clearest legacy variable names and sample-entry layout. The inspection sheets provide the broader inspection instructions. Both should be reviewed together for each part.

## Remaining Review Risks

The current template can load Schneider job data, materials, variables, attributes, tools/methods, phase-specific requirements, sample sizes, frequencies, and display order. The main risks before production use are review/validation risks:

| Review risk | Why it matters |
| --- | --- |
| Verified material source | The inspection sheet alone is not enough to decide what materials belong to a job. Materials should be imported only from a BOM/material master or another verified source. |
| Sample source context | Schneider plans may require pulling samples from specific places, but that should not create duplicate characteristics unless the actual measurement or limit is different. |
| Event trigger detail | `Start Up & Coil Changes (CC)` and tooling-change requirements must be represented through phase/event requirement columns without creating duplicate full inspection blocks. |
| Drawing number and revision metadata | Several sheets include Schneider drawing numbers, part drawing revisions, pallet drawing numbers, and control plan references. |

## Recommended Import Rule

For each Schneider part, create one row per inspection item. Do not copy the same full inspection into separate Startup, Setup, and In Process blocks. Use phase-specific columns on that one row to say which phases require the item and what timing applies.

Example:

| Part Number | Part Description | Product Group | Operation | Variable Name | Unit | Target | Lower Spec | Upper Spec | Startup Required | Startup Sample Size | Startup Frequency Type | Startup Frequency | Startup Frequency Unit | In Process Required | In Process Sample Size | In Process Frequency Type | In Process Frequency | In Process Frequency Unit |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 70307 | Schneider - HOM Jaw Terminal | Schneider | Inspection | Material Thickness | in | .050 | .049 | .051 | true | 2 | Event | 1 | StartOfJob | true | 2 | Quantity | 5000 | Pieces |

## Reviewed Source Examples

The reviewed Schneider files include:

- `INSP-0246` and `INSP-0247`: blade terminal/contact assemblies with setup/startup, material thickness, contact tape, comparator/template checks, and mixed numeric/attribute checks.
- `INSP-0375`, `INSP-0388`, `INSP-0408`: jaw/spring/contact and jaw terminal plans with Front/Back requirements, material thickness, visual checks, force checks, jaw dimensions, comparator/template checks, packaging fields, and WinSPC logs.
- `INSP-0387`: terminal screw clamp assembly with torque, tapped holes, material thickness, wire clamp checks, visual checks, and multiple template/gauge references.
- `INSP-0389`: HOM jaw spring with Lower/Upper samples and per-side inspection frequency.
- `INSP-0409` and `INSP-0411`: low amp compensator/armature plans with many variables, OK/NG attributes, heat-test logic, wash-cycle checks, material orientation, coil-change affected dimensions, and multi-page layouts.

## Bottom Line

SPC-Star can now map the main Schneider inspection data into job data, numeric variables, accept/reject attributes, and short inspection method/tool details. The biggest remaining risks are exact table extraction from the PDFs and material assignment. Before bulk importing Schneider plans, use visual review to confirm each variable name, limit, sample size, frequency, and attribute classification. Import materials only from a verified material/BOM source.
