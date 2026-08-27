# DGH Tools for Revit

DGH Tools is a lightweight Revit 2023 add-in focused on fast datum/grid editing workflows.

## Current tool

- **Align Grid Ends** — align one end of multiple parallel straight grids in the active view using 2D/View Specific extents.

## Publisher

- Company / product publisher metadata: **DGH Tools**
- Revit add-in vendor ID: **DGH**
- Revit vendor description: **DGH Tools for Autodesk Revit**

> Windows SmartScreen/UAC trusted Publisher status requires a separate Authenticode code-signing certificate. File metadata alone does not create a trusted Windows publisher identity.

## Installer

`installer/bin/DGH_Tools_Revit2023_Setup.exe` is the permanent GUI bootstrap installer. Every time it runs, it reads `update/update.json` and installs the newest published DGH Tools version from this repository.

The installer has an embedded DGH Tools application icon and Windows file metadata (Company, Product, Description and Version). It detects Revit 2023, shows installed/latest versions, downloads the current source and updater, compiles against the local Revit 2023 API, and registers the add-in for the current Windows user.

## Auto-update

Installed builds check `update/update.json` from this repository in the background. A newer source version is downloaded and compiled against the local Revit 2023 API after Revit closes, becoming active on the next launch.

Current plugin version: **0.7.1**
