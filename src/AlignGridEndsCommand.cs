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
                List<Grid> grids = GridToolHelpers.GetSelectedGrids(uiDoc, doc, "Select at least two parallel grids and click Finish");
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
                Dictionary<ElementId, Line> visibleLines = new Dictionary<ElementId, Line>();

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

                    Line visibleLine = GridToolHelpers.GetVisibleLine(grid, view);
                    if (visibleLine == null)
                    {
                        TaskDialog.Show("Align Grid Ends", "Could not read grid extents in the active view.");
                        return Result.Cancelled;
                    }

                    visibleLines[grid.Id] = visibleLine;
                }

                XYZ pickedPoint = uiDoc.Selection.PickPoint(ObjectSnapTypes.None, "Pick the new grid end position");
                double targetCoordinate = pickedPoint.DotProduct(referenceDirection);

                Line firstVisible = visibleLines[grids[0].Id];
                double f0 = firstVisible.GetEndPoint(0).DotProduct(referenceDirection);
                double f1 = firstVisible.GetEndPoint(1).DotProduct(referenceDirection);
                bool moveMaxSide = targetCoordinate >= (f0 + f1) * 0.5;

                int changed = 0;
                List<string> failed = new List<string>();

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
    }

    [Transaction(TransactionMode.Manual)]
    public class AddGridElbowsCommand : IExternalCommand
    {
        private const double MinimumPaperSpacingMm = 10.0;
        private const double MoveTolerancePaperMm = 0.20;
        private const double DirectionTolerance = 0.9995;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            View view = doc.ActiveView;

            try
            {
                List<Grid> grids = GridToolHelpers.GetSelectedGrids(uiDoc, doc, "Select grids and click Finish");
                if (grids.Count < 2)
                {
                    TaskDialog.Show("Add Grid Elbows", "Select at least two straight grids.");
                    return Result.Cancelled;
                }

                List<GridRecord> records = new List<GridRecord>();
                List<string> failed = new List<string>();

                foreach (Grid grid in grids)
                {
                    Line visible = GridToolHelpers.GetVisibleLine(grid, view);
                    Line model = grid.Curve as Line;

                    if (visible == null || model == null)
                    {
                        failed.Add(grid.Name + " (only straight visible grids are supported)");
                        continue;
                    }

                    XYZ direction = ProjectToViewPlane(visible.Direction.Normalize(), view.ViewDirection);
                    if (direction == null)
                    {
                        failed.Add(grid.Name + " (invalid direction in this view)");
                        continue;
                    }

                    direction = CanonicalizeDirection(direction, view);
                    records.Add(new GridRecord(grid, visible, model, direction));
                }

                if (records.Count < 2)
                {
                    TaskDialog.Show("Add Grid Elbows", "Not enough supported grids were found.");
                    return Result.Cancelled;
                }

                List<DirectionGroup> groups = BuildDirectionGroups(records);
                double minimumSpacing = PaperMmToModelFeet(MinimumPaperSpacingMm, view.Scale);
                double moveTolerance = PaperMmToModelFeet(MoveTolerancePaperMm, view.Scale);
                int changed = 0;

                using (Transaction tx = new Transaction(doc, "Add Grid Elbows"))
                {
                    tx.Start();

                    foreach (DirectionGroup group in groups)
                    {
                        if (group.Items.Count < 2) continue;

                        XYZ direction = group.Direction;
                        XYZ transverse = view.ViewDirection.CrossProduct(direction);
                        if (transverse.GetLength() < 1e-9) continue;
                        transverse = transverse.Normalize();

                        List<BubbleCandidate> low = new List<BubbleCandidate>();
                        List<BubbleCandidate> high = new List<BubbleCandidate>();

                        foreach (GridRecord record in group.Items)
                        {
                            AddCandidate(record, DatumEnds.End0, view, direction, transverse, low, high);
                            AddCandidate(record, DatumEnds.End1, view, direction, transverse, low, high);
                        }

                        changed += ProcessSide(low, view, direction, transverse, minimumSpacing, moveTolerance, failed);
                        changed += ProcessSide(high, view, direction, transverse, minimumSpacing, moveTolerance, failed);
                    }

                    tx.Commit();
                }

                if (failed.Count > 0)
                {
                    string warning = "Some grids could not be processed.\n\nChanged leaders: " + changed + "\n\n" +
                                     string.Join("\n", failed.Take(8).ToArray());
                    if (failed.Count > 8) warning += "\n...and " + (failed.Count - 8) + " more.";
                    TaskDialog.Show("Add Grid Elbows - Warning", warning);
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

        private static int ProcessSide(
            List<BubbleCandidate> candidates,
            View view,
            XYZ direction,
            XYZ transverse,
            double spacing,
            double tolerance,
            List<string> failed)
        {
            if (candidates.Count < 2) return 0;

            candidates = candidates.OrderBy(c => c.GridTransverse).ToList();
            double[] desired = candidates.Select(c => c.GridTransverse).ToArray();
            double[] targets = SolveMinimumSpacing(desired, spacing);
            double commonLevel = Median(candidates.Select(c => c.CurrentAnchor.DotProduct(direction)).ToArray());
            XYZ viewDirection = view.ViewDirection.Normalize();
            int changed = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                BubbleCandidate candidate = candidates[i];
                if (Math.Abs(targets[i] - candidate.GridTransverse) <= tolerance)
                    continue;

                SubTransaction sub = new SubTransaction(candidate.Grid.Document);
                sub.Start();

                try
                {
                    Leader leader = candidate.Grid.GetLeader(candidate.DatumEnd, view);
                    if (leader == null)
                        leader = candidate.Grid.AddLeader(candidate.DatumEnd, view);

                    double depth = leader.Anchor.DotProduct(viewDirection);
                    XYZ targetAnchor = FromViewCoordinates(direction, transverse, viewDirection, commonLevel, targets[i], depth);

                    MoveLeaderAnchorByElbow(candidate.Grid, candidate.DatumEnd, view, leader, targetAnchor, tolerance);
                    sub.Commit();
                    changed++;
                }
                catch (Exception ex)
                {
                    try { sub.RollBack(); } catch { }
                    failed.Add(candidate.Grid.Name + ": " + ex.Message);
                }
            }

            return changed;
        }

        private static void MoveLeaderAnchorByElbow(
            Grid grid,
            DatumEnds datumEnd,
            View view,
            Leader leader,
            XYZ targetAnchor,
            double tolerance)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                XYZ currentAnchor = leader.Anchor;
                XYZ delta = targetAnchor - currentAnchor;
                if (delta.GetLength() <= tolerance) return;

                leader.Elbow = leader.Elbow + delta;
                grid.SetLeader(datumEnd, view, leader);
                grid.SetLeader(datumEnd, view, leader);

                leader = grid.GetLeader(datumEnd, view);
                if (leader == null)
                    throw new InvalidOperationException("leader disappeared after SetLeader");
            }
        }

        private static void AddCandidate(
            GridRecord record,
            DatumEnds datumEnd,
            View view,
            XYZ direction,
            XYZ transverse,
            List<BubbleCandidate> low,
            List<BubbleCandidate> high)
        {
            bool visible;
            try { visible = record.Grid.IsBubbleVisibleInView(datumEnd, view); }
            catch { return; }
            if (!visible) return;

            XYZ gridEnd = GetVisibleEndpointForDatumEnd(record.VisibleLine, record.ModelLine, datumEnd);
            Leader leader = null;
            try { leader = record.Grid.GetLeader(datumEnd, view); } catch { }
            XYZ anchor = leader != null ? leader.Anchor : gridEnd;

            XYZ midpoint = (record.VisibleLine.GetEndPoint(0) + record.VisibleLine.GetEndPoint(1)) * 0.5;
            double sideCoordinate = anchor.DotProduct(direction);
            double midCoordinate = midpoint.DotProduct(direction);
            double gridTransverse = midpoint.DotProduct(transverse);

            BubbleCandidate candidate = new BubbleCandidate(record.Grid, datumEnd, anchor, gridTransverse);
            if (sideCoordinate < midCoordinate) low.Add(candidate);
            else high.Add(candidate);
        }

        private static XYZ GetVisibleEndpointForDatumEnd(Line visible, Line model, DatumEnds datumEnd)
        {
            XYZ modelDirection = model.Direction.Normalize();
            XYZ v0 = visible.GetEndPoint(0);
            XYZ v1 = visible.GetEndPoint(1);
            double c0 = v0.DotProduct(modelDirection);
            double c1 = v1.DotProduct(modelDirection);

            if (datumEnd == DatumEnds.End0)
                return c0 <= c1 ? v0 : v1;

            return c0 >= c1 ? v0 : v1;
        }

        private static List<DirectionGroup> BuildDirectionGroups(List<GridRecord> records)
        {
            List<DirectionGroup> groups = new List<DirectionGroup>();

            foreach (GridRecord record in records)
            {
                DirectionGroup found = null;
                foreach (DirectionGroup group in groups)
                {
                    if (Math.Abs(group.Direction.DotProduct(record.Direction)) >= DirectionTolerance)
                    {
                        found = group;
                        break;
                    }
                }

                if (found == null)
                {
                    found = new DirectionGroup(record.Direction);
                    groups.Add(found);
                }

                found.Items.Add(record);
            }

            return groups;
        }

        private static XYZ ProjectToViewPlane(XYZ vector, XYZ viewDirection)
        {
            XYZ normal = viewDirection.Normalize();
            XYZ projected = vector - normal * vector.DotProduct(normal);
            if (projected.GetLength() < 1e-9) return null;
            return projected.Normalize();
        }

        private static XYZ CanonicalizeDirection(XYZ direction, View view)
        {
            double right = direction.DotProduct(view.RightDirection);
            double up = direction.DotProduct(view.UpDirection);

            if (right < -1e-6 || (Math.Abs(right) <= 1e-6 && up < 0))
                return direction.Negate();

            return direction;
        }

        private static XYZ FromViewCoordinates(
            XYZ direction,
            XYZ transverse,
            XYZ viewDirection,
            double along,
            double across,
            double depth)
        {
            return direction * along + transverse * across + viewDirection * depth;
        }

        private static double PaperMmToModelFeet(double paperMm, int scale)
        {
            double modelMm = paperMm * Math.Max(1, scale);
            return modelMm / 304.8;
        }

        private static double Median(double[] values)
        {
            if (values == null || values.Length == 0) return 0.0;
            double[] copy = (double[])values.Clone();
            Array.Sort(copy);
            int middle = copy.Length / 2;
            if (copy.Length % 2 == 1) return copy[middle];
            return (copy[middle - 1] + copy[middle]) * 0.5;
        }

        private static double[] SolveMinimumSpacing(double[] desired, double spacing)
        {
            int n = desired.Length;
            double[] transformed = new double[n];
            for (int i = 0; i < n; i++) transformed[i] = desired[i] - i * spacing;

            List<IsotonicBlock> blocks = new List<IsotonicBlock>();
            for (int i = 0; i < n; i++)
            {
                blocks.Add(new IsotonicBlock(i, i, transformed[i], 1));

                while (blocks.Count >= 2)
                {
                    IsotonicBlock a = blocks[blocks.Count - 2];
                    IsotonicBlock b = blocks[blocks.Count - 1];
                    if (a.Mean <= b.Mean + 1e-12) break;

                    int count = a.Count + b.Count;
                    double mean = (a.Mean * a.Count + b.Mean * b.Count) / count;
                    blocks.RemoveAt(blocks.Count - 1);
                    blocks[blocks.Count - 1] = new IsotonicBlock(a.Start, b.End, mean, count);
                }
            }

            double[] result = new double[n];
            foreach (IsotonicBlock block in blocks)
            {
                for (int i = block.Start; i <= block.End; i++)
                    result[i] = block.Mean + i * spacing;
            }
            return result;
        }

        private sealed class GridRecord
        {
            public Grid Grid;
            public Line VisibleLine;
            public Line ModelLine;
            public XYZ Direction;

            public GridRecord(Grid grid, Line visibleLine, Line modelLine, XYZ direction)
            {
                Grid = grid;
                VisibleLine = visibleLine;
                ModelLine = modelLine;
                Direction = direction;
            }
        }

        private sealed class DirectionGroup
        {
            public XYZ Direction;
            public List<GridRecord> Items = new List<GridRecord>();

            public DirectionGroup(XYZ direction)
            {
                Direction = direction;
            }
        }

        private sealed class BubbleCandidate
        {
            public Grid Grid;
            public DatumEnds DatumEnd;
            public XYZ CurrentAnchor;
            public double GridTransverse;

            public BubbleCandidate(Grid grid, DatumEnds datumEnd, XYZ currentAnchor, double gridTransverse)
            {
                Grid = grid;
                DatumEnd = datumEnd;
                CurrentAnchor = currentAnchor;
                GridTransverse = gridTransverse;
            }
        }

        private sealed class IsotonicBlock
        {
            public int Start;
            public int End;
            public double Mean;
            public int Count;

            public IsotonicBlock(int start, int end, double mean, int count)
            {
                Start = start;
                End = end;
                Mean = mean;
                Count = count;
            }
        }
    }

    internal static class GridToolHelpers
    {
        public static List<Grid> GetSelectedGrids(UIDocument uiDoc, Document doc, string prompt)
        {
            List<Grid> grids = uiDoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Grid>()
                .ToList();

            if (grids.Count >= 2) return grids;

            IList<Reference> picked = uiDoc.Selection.PickObjects(
                ObjectType.Element,
                new GridSelectionFilter(),
                prompt);

            return picked.Select(r => doc.GetElement(r))
                .OfType<Grid>()
                .GroupBy(g => g.Id.IntegerValue)
                .Select(g => g.First())
                .ToList();
        }

        public static Line GetVisibleLine(Grid grid, View view)
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
