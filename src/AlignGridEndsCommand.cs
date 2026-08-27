using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DghTools.Revit
{
    [Transaction(TransactionMode.Manual)]
    public class AlignGridEndsCommand : IExternalCommand
    {
        private const double ParallelTolerance = 0.9999;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            View view = doc.ActiveView;

            try
            {
                List<Grid> grids = GetSelectedGrids(uiDoc, doc);
                if (grids.Count < 2)
                {
                    TaskDialog.Show("Align Grid Ends", "Select at least two parallel straight grids.");
                    return Result.Cancelled;
                }

                Line firstModelLine = grids[0].Curve as Line;
                if (firstModelLine == null)
                {
                    TaskDialog.Show("Align Grid Ends", "Arc grids are not supported yet.");
                    return Result.Cancelled;
                }

                XYZ referenceDirection = firstModelLine.Direction.Normalize();
                foreach (Grid grid in grids)
                {
                    Line modelLine = grid.Curve as Line;
                    if (modelLine == null)
                    {
                        TaskDialog.Show("Align Grid Ends", "Only straight grids are supported.");
                        return Result.Cancelled;
                    }

                    double dot = Math.Abs(modelLine.Direction.Normalize().DotProduct(referenceDirection));
                    if (dot < ParallelTolerance)
                    {
                        TaskDialog.Show("Align Grid Ends", "Select one parallel grid set per command.");
                        return Result.Cancelled;
                    }
                }

                var visibleLines = new Dictionary<ElementId, Line>();
                foreach (Grid grid in grids)
                {
                    Line visibleLine = GetVisibleLine(grid, view);
                    if (visibleLine == null)
                    {
                        TaskDialog.Show("Align Grid Ends", "Could not read grid extents in the active view.");
                        return Result.Cancelled;
                    }
                    visibleLines[grid.Id] = visibleLine;
                }

                XYZ pickedPoint = uiDoc.Selection.PickPoint(ObjectSnapTypes.None, "Pick the new grid end position");
                double targetCoordinate = pickedPoint.DotProduct(referenceDirection);

                Line firstVisibleLine = visibleLines[grids[0].Id];
                double first0 = firstVisibleLine.GetEndPoint(0).DotProduct(referenceDirection);
                double first1 = firstVisibleLine.GetEndPoint(1).DotProduct(referenceDirection);
                bool moveMaxSide = targetCoordinate >= (first0 + first1) * 0.5;

                int changed = 0;
                var failed = new List<string>();

                using (Transaction tx = new Transaction(doc, "Align Grid Ends"))
                {
                    tx.Start();

                    foreach (Grid grid in grids)
                    {
                        try
                        {
                            Line currentLine = visibleLines[grid.Id];
                            XYZ p0 = currentLine.GetEndPoint(0);
                            XYZ p1 = currentLine.GetEndPoint(1);
                            double c0 = p0.DotProduct(referenceDirection);
                            double c1 = p1.DotProduct(referenceDirection);

                            int movingCurveEnd = moveMaxSide
                                ? (c0 >= c1 ? 0 : 1)
                                : (c0 <= c1 ? 0 : 1);

                            XYZ movingPoint = movingCurveEnd == 0 ? p0 : p1;
                            XYZ fixedPoint = movingCurveEnd == 0 ? p1 : p0;

                            Line modelLine = grid.Curve as Line;
                            XYZ ownDirection = modelLine.Direction.Normalize();
                            if (ownDirection.DotProduct(referenceDirection) < 0)
                                ownDirection = ownDirection.Negate();

                            double denominator = ownDirection.DotProduct(referenceDirection);
                            if (Math.Abs(denominator) < 1e-9)
                            {
                                failed.Add(grid.Name + " (direction calculation failed)");
                                continue;
                            }

                            double movingCoordinate = movingPoint.DotProduct(referenceDirection);
                            double distanceAlongGrid = (targetCoordinate - movingCoordinate) / denominator;
                            XYZ newMovingPoint = movingPoint + ownDirection * distanceAlongGrid;

                            if (newMovingPoint.DistanceTo(fixedPoint) < 1e-6)
                            {
                                failed.Add(grid.Name + " (zero length)");
                                continue;
                            }

                            XYZ model0 = modelLine.GetEndPoint(0);
                            XYZ model1 = modelLine.GetEndPoint(1);
                            double modelC0 = model0.DotProduct(referenceDirection);
                            double modelC1 = model1.DotProduct(referenceDirection);

                            DatumEnds datumEnd = moveMaxSide
                                ? (modelC0 >= modelC1 ? DatumEnds.End0 : DatumEnds.End1)
                                : (modelC0 <= modelC1 ? DatumEnds.End0 : DatumEnds.End1);

                            grid.SetDatumExtentType(datumEnd, view, DatumExtentType.ViewSpecific);

                            Line newLine = movingCurveEnd == 0
                                ? Line.CreateBound(newMovingPoint, fixedPoint)
                                : Line.CreateBound(fixedPoint, newMovingPoint);

                            grid.SetCurveInView(DatumExtentType.ViewSpecific, view, newLine);
                            changed++;
                        }
                        catch (Exception ex)
                        {
                            failed.Add(grid.Name + ": " + ex.Message);
                        }
                    }

                    tx.Commit();
                }

                // Successful execution is intentionally silent.
                if (failed.Count > 0)
                {
                    string warning = "Some grids could not be processed.\n\nChanged: " + changed + " of " + grids.Count + "\n\n" +
                                     string.Join("\n", failed.Take(8).ToArray());
                    if (failed.Count > 8) warning += "\n...and " + (failed.Count - 8) + " more.";
                    TaskDialog.Show("Align Grid Ends - Warning", warning);
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }

        private static List<Grid> GetSelectedGrids(UIDocument uiDoc, Document doc)
        {
            List<Grid> grids = uiDoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Grid>()
                .ToList();

            if (grids.Count >= 2) return grids;

            IList<Reference> picked = uiDoc.Selection.PickObjects(
                ObjectType.Element,
                new GridSelectionFilter(),
                "Select at least two parallel grids and click Finish");

            return picked.Select(r => doc.GetElement(r))
                .OfType<Grid>()
                .GroupBy(g => g.Id.IntegerValue)
                .Select(g => g.First())
                .ToList();
        }

        private static Line GetVisibleLine(Grid grid, View view)
        {
            try
            {
                Line line = grid.GetCurvesInView(DatumExtentType.ViewSpecific, view).OfType<Line>().FirstOrDefault();
                if (line != null) return line;
            }
            catch { }

            try
            {
                return grid.GetCurvesInView(DatumExtentType.Model, view).OfType<Line>().FirstOrDefault();
            }
            catch { return null; }
        }

        private class GridSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) { return elem is Grid; }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }
    }
}
