# Changelog

## 0.8.1
- Fixed plugin compilation on Revit 2023/.NET Framework where `BitmapImage.UriSource` required an unreferenced `PresentationFramework.dll`.
- Ribbon PNG loading now uses `BitmapDecoder`/`BitmapFrame`, avoiding the `IUriContext` dependency that caused the installer error.
- Removed the invalid assignment to read-only `Leader.Anchor` in **Add Grid Elbows**.
- Grid bubble movement now adjusts `Leader.Elbow` and lets Revit calculate the bubble anchor, with repeated `SetLeader` calls to compensate for Revit's datum-leader offset behavior.
- External updater now references `PresentationFramework.dll` as an additional safeguard for future WPF code.

## 0.8.0
- Added **Add Grid Elbows** (beta) to `DGH Tools -> Grids`.
- Automatically groups selected straight grids by direction.
- Processes visible bubbles on both physical sides of each grid group.
- Detects clashes using a scale-aware minimum spacing of 10 mm on paper.
- Uses a least-squares minimum-spacing layout so clashing bubbles spread symmetrically instead of drifting to one side.
- Moved bubble centers stay on one common annotation line; leaders use an L-shaped elbow back to the grid.
- Existing leaders are repositioned when they still require an offset; successful runs remain silent.
- Added a dedicated Add Grid Elbows ribbon icon embedded in the plugin source.
- Updated the external updater so future releases can compile any number of downloaded C# source files.

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
