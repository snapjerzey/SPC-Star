# GitHub Data Boundary

GitHub contains SPC-Star source code, automated tests using constructed examples,
deployment tools and the role SOPs. Part-specific behavior and the default account
reference in source code are retained intentionally. A source checkout is not a
copy of an installed server.

Keep the following outside Git:

- Live or test-instance databases, session stores, backups and archives.
- Parts and inspection workbooks, source sheets, imports and audit extracts.
- Actual users, passwords, session tokens and machine configuration exports.
- Inspection results, job notes and generated reports.
- Company certificate templates and generated documents.

The ignore rules protect these file locations and spreadsheet/database formats.
They do not inspect file contents. Review every staged file before committing;
do not force-add ignored files.

The full private source-audit tests and their fixtures remain on the local
workstation. They are not included in the repository. The repository's remaining
tests run without those inputs. Certificate generation requires the separately
supplied company templates in the application's CertTemplates folders; those
files are not distributed through GitHub.

Removing a tracked file affects the latest revision only. Older commits can still
contain previously committed files. Rewriting existing Git history requires a
separate coordinated operation; this update does not rewrite it.
