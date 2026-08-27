# Changelog

## 0.7.1
- Added DGH Tools publisher/company metadata to the Revit plugin assembly.
- Added DGH Tools company/product metadata to the Windows installer.
- Added an embedded DGH Tools installer icon.
- Installer build now verifies Company, Product, Description and Version metadata.

## 0.7.0
- Switched auto-update to a public GitHub `update.json` manifest.
- Split the add-in into `App.cs`, `AlignGridEndsCommand.cs`, and `UpdateManager.cs`.
- Added silent background update checks every 24 hours.
- Updates are downloaded while Revit is running and applied after Revit closes.
- Successful Align Grid Ends operations remain silent.
- Ribbon tool uses the English name **Align Grid Ends**.

## 0.4.0
- Added ribbon icon and English naming.
- Removed success popup after execution.
