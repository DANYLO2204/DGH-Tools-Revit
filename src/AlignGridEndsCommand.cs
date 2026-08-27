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

    [Transaction(TransactionMode.Manual)]
    public class AddGridElbowsCommand : IExternalCommand
    {
        private const double MinimumPaperSpacingMm = 10.0;
        private const double LeaderStemPaperMm = 5.0;
        private const double DirectionTolerance = 0.9995;

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
                    TaskDialog.Show("Add Grid Elbows", "Select at least two straight grids.");
                    return Result.Cancelled;
                }

                List<GridRecord> records = new List<GridRecord>();
                List<string> skipped = new List<string>();

                foreach (Grid grid in grids)
                {
                    Line line = GetVisibleLine(grid, view);
                    Line modelLine = grid.Curve as Line;

                    if (line == null || modelLine == null)
                    {
                        skipped.Add(grid.Name + " (only straight visible grids are supported)");
                        continue;
                    }

                    XYZ direction = ProjectToViewPlane(line.Direction.Normalize(), view.ViewDirection);
                    if (direction == null)
                    {
                        skipped.Add(grid.Name + " (grid direction is not valid in this view)");
                        continue;
                    }

                    direction = CanonicalizeDirection(direction, view);
                    records.Add(new GridRecord(grid, line, modelLine, direction));
                }

                if (records.Count < 2)
                {
                    TaskDialog.Show("Add Grid Elbows", "Not enough supported straight grids were found.");
                    return Result.Cancelled;
                }

                List<DirectionGroup> groups = BuildDirectionGroups(records);
                double minimumSpacing = PaperMmToModelFeet(MinimumPaperSpacingMm, view.Scale);
                double leaderStem = PaperMmToModelFeet(LeaderStemPaperMm, view.Scale);
                double moveTolerance = PaperMmToModelFeet(0.20, view.Scale);

                int changed = 0;
                List<string> failed = new List<string>();

                using (Transaction tx = new Transaction(doc, "Add Grid Elbows"))
                {
                    tx.Start();

                    foreach (DirectionGroup group in groups)
                    {
                        if (group.Items.Count < 2)
                            continue;

                        XYZ direction = group.Direction;
                        XYZ transverse = view.ViewDirection.CrossProduct(direction);

                        if (transverse.GetLength() < 1e-9)
                            continue;

                        transverse = transverse.Normalize();

                        List<BubbleCandidate> lowSide = new List<BubbleCandidate>();
                        List<BubbleCandidate> highSide = new List<BubbleCandidate>();

                        foreach (GridRecord record in group.Items)
                        {
                            AddVisibleBubbleCandidate(record, DatumEnds.End0, view, direction, transverse, lowSide, highSide);
                            AddVisibleBubbleCandidate(record, DatumEnds.End1, view, direction, transverse, lowSide, highSide);
                        }

                        changed += ProcessSide(lowSide, -1, view, direction, transverse, minimumSpacing, leaderStem, moveTolerance, failed);
                        changed += ProcessSide(highSide, 1, view, direction, transverse, minimumSpacing, leaderStem, moveTolerance, failed);
                    }

                    tx.Commit();
                }

                if (skipped.Count > 0)
                    failed.AddRange(skipped);

                if (failed.Count > 0)
                {
                    string warning = "Some grids could not be processed.\n\nChanged leaders: " + changed + "\n\n" +
                                     string.Join("\n", failed.Take(8).ToArray());

                    if (failed.Count > 8)
                        warning += "\n...and " + (failed.Count - 8) + " more.";

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
            int sideSign,
            View view,
            XYZ direction,
            XYZ transverse,
            double minimumSpacing,
            double leaderStem,
            double moveTolerance,
            List<string> failed)
        {
            if (candidates.Count < 2)
                return 0;

            candidates = candidates
                .OrderBy(c => c.GridTransverse)
                .ToList();

            double[] desired = candidates.Select(c => c.GridTransverse).ToArray();
            double[] targets = SolveMinimumSpacing(desired, minimumSpacing);

            double[] currentLevels = candidates
                .Select(c => c.CurrentBubblePoint.DotProduct(direction))
                .OrderBy(v => v)
                .ToArray();

            double commonLevel = Median(currentLevels);
            int changed = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                BubbleCandidate candidate = candidates[i];
                double horizontalMove = targets[i] - candidate.GridTransverse;

                if (Math.Abs(horizontalMove) <= moveTolerance)
                    continue;

                SubTransaction sub = new SubTransaction(candidate.Grid.Document);
                sub.Start();

                try
                {
                    Leader leader = candidate.Grid.GetLeader(candidate.DatumEnd, view);
                    if (leader == null)
                        leader = candidate.Grid.AddLeader(candidate.DatumEnd, view);

                    double stem = Math.Min(leaderStem, candidate.VisibleLine.Length * 0.25);
                    if (stem < 1e-5)
                        throw new InvalidOperationException("grid extent is too short for a leader");

                    XYZ bubbleAnchor = WithViewCoordinates(
                        candidate.GridEndPoint,
                        direction,
                        transverse,
                        commonLevel,
                        targets[i]);

                    XYZ elbow = WithViewCoordinates(
                        candidate.GridEndPoint,
                        direction,
                        transverse,
                        commonLevel,
                        candidate.GridTransverse);

                    double inwardSign = sideSign < 0 ? 1.0 : -1.0;
                    XYZ leaderEnd = candidate.GridEndPoint + direction * (stem * inwardSign);

                    leader.Anchor = bubbleAnchor;
                    leader.Elbow = elbow;
                    leader.End = leaderEnd;

                    if (!candidate.Grid.IsLeaderValid(candidate.DatumEnd, view, leader))
                        throw new InvalidOperationException("calculated leader geometry is not valid in this view");

                    candidate.Grid.SetLeader(candidate.DatumEnd, view, leader);
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

        private static double[] SolveMinimumSpacing(double[] desired, double spacing)
        {
            int n = desired.Length;
            double[] transformed = new double[n];

            for (int i = 0; i < n; i++)
                transformed[i] = desired[i] - i * spacing;

            List<IsotonicBlock> blocks = new List<IsotonicBlock>();

            for (int i = 0; i < n; i++)
            {
                blocks.Add(new IsotonicBlock(i, i, transformed[i], 1));

                while (blocks.Count >= 2)
                {
                    IsotonicBlock a = blocks[blocks.Count - 2];
                    IsotonicBlock b = blocks[blocks.Count - 1];

                    if (a.Mean <= b.Mean + 1e-12)
                        break;

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

        private static void AddVisibleBubbleCandidate(
            GridRecord record,
            DatumEnds datumEnd,
            View view,
            XYZ direction,
            XYZ transverse,
            List<BubbleCandidate> lowSide,
            List<BubbleCandidate> highSide)
        {
            bool visible;

            try
            {
                visible = record.Grid.IsBubbleVisibleInView(datumEnd, view);
            }
            catch
            {
                return;
            }

            if (!visible)
                return;

            XYZ point = GetVisiblePointForDatumEnd(record, datumEnd);
            if (point == null)
                return;

            double midpoint = 0.5 * (
                record.VisibleLine.GetEndPoint(0).DotProduct(direction) +
                record.VisibleLine.GetEndPoint(1).DotProduct(direction));

            double pointLevel = point.DotProduct(direction);
            int side = pointLevel < midpoint ? -1 : 1;
            double gridTransverse = point.DotProduct(transverse);

            XYZ currentBubblePoint = point;

            try
            {
                Leader existing = record.Grid.GetLeader(datumEnd, view);
                if (existing != null)
                    currentBubblePoint = existing.Anchor;
            }
            catch { }

            BubbleCandidate candidate = new BubbleCandidate(
                record.Grid,
                record.VisibleLine,
                datumEnd,
                point,
                currentBubblePoint,
                gridTransverse);

            if (side < 0)
                lowSide.Add(candidate);
            else
                highSide.Add(candidate);
        }

        private static XYZ GetVisiblePointForDatumEnd(GridRecord record, DatumEnds datumEnd)
        {
            XYZ ownDirection = record.ModelLine.Direction.Normalize();
            XYZ p0 = record.VisibleLine.GetEndPoint(0);
            XYZ p1 = record.VisibleLine.GetEndPoint(1);

            double c0 = p0.DotProduct(ownDirection);
            double c1 = p1.DotProduct(ownDirection);

            if (datumEnd == DatumEnds.End0)
                return c0 <= c1 ? p0 : p1;

            return c0 >= c1 ? p0 : p1;
        }

        private static List<DirectionGroup> BuildDirectionGroups(List<GridRecord> records)
        {
            List<DirectionGroup> groups = new List<DirectionGroup>();

            foreach (GridRecord record in records)
            {
                DirectionGroup match = groups.FirstOrDefault(
                    g => Math.Abs(g.Direction.DotProduct(record.Direction)) >= DirectionTolerance);

                if (match == null)
                {
                    match = new DirectionGroup(record.Direction);
                    groups.Add(match);
                }

                match.Items.Add(record);
            }

            return groups;
        }

        private static XYZ ProjectToViewPlane(XYZ vector, XYZ viewNormal)
        {
            XYZ projected = vector - viewNormal * vector.DotProduct(viewNormal);
            if (projected.GetLength() < 1e-9)
                return null;
            return projected.Normalize();
        }

        private static XYZ CanonicalizeDirection(XYZ direction, View view)
        {
            double right = direction.DotProduct(view.RightDirection);
            double up = direction.DotProduct(view.UpDirection);

            if (right < -1e-9 || (Math.Abs(right) <= 1e-9 && up < 0))
                return direction.Negate();

            return direction;
        }

        private static XYZ WithViewCoordinates(
            XYZ referencePoint,
            XYZ direction,
            XYZ transverse,
            double directionCoordinate,
            double transverseCoordinate)
        {
            double currentD = referencePoint.DotProduct(direction);
            double currentT = referencePoint.DotProduct(transverse);

            return referencePoint +
                   direction * (directionCoordinate - currentD) +
                   transverse * (transverseCoordinate - currentT);
        }

        private static double PaperMmToModelFeet(double paperMm, int viewScale)
        {
            int scale = Math.Max(1, viewScale);
            return (paperMm / 304.8) * scale;
        }

        private static double Median(double[] sortedValues)
        {
            if (sortedValues == null || sortedValues.Length == 0)
                return 0.0;

            int n = sortedValues.Length;
            if (n % 2 == 1)
                return sortedValues[n / 2];

            return 0.5 * (sortedValues[n / 2 - 1] + sortedValues[n / 2]);
        }

        private static List<Grid> GetSelectedGrids(UIDocument uiDoc, Document doc)
        {
            List<Grid> grids = uiDoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Grid>()
                .ToList();

            if (grids.Count >= 2)
                return grids;

            IList<Reference> picked = uiDoc.Selection.PickObjects(
                ObjectType.Element,
                new GridElbowSelectionFilter(),
                "Select grids and click Finish");

            return picked
                .Select(r => doc.GetElement(r))
                .OfType<Grid>()
                .GroupBy(g => g.Id.IntegerValue)
                .Select(g => g.First())
                .ToList();
        }

        private static Line GetVisibleLine(Grid grid, View view)
        {
            try
            {
                Line line = grid.GetCurvesInView(DatumExtentType.ViewSpecific, view)
                    .OfType<Line>()
                    .FirstOrDefault();

                if (line != null)
                    return line;
            }
            catch { }

            try
            {
                return grid.GetCurvesInView(DatumExtentType.Model, view)
                    .OfType<Line>()
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private sealed class GridElbowSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) { return elem is Grid; }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        private sealed class GridRecord
        {
            public Grid Grid { get; private set; }
            public Line VisibleLine { get; private set; }
            public Line ModelLine { get; private set; }
            public XYZ Direction { get; private set; }

            public GridRecord(Grid grid, Line visibleLine, Line modelLine, XYZ direction)
            {
                Grid = grid;
                VisibleLine = visibleLine;
                ModelLine = modelLine;
                Direction = direction;
            }
        }

        private sealed class BubbleCandidate
        {
            public Grid Grid { get; private set; }
            public Line VisibleLine { get; private set; }
            public DatumEnds DatumEnd { get; private set; }
            public XYZ GridEndPoint { get; private set; }
            public XYZ CurrentBubblePoint { get; private set; }
            public double GridTransverse { get; private set; }

            public BubbleCandidate(
                Grid grid,
                Line visibleLine,
                DatumEnds datumEnd,
                XYZ gridEndPoint,
                XYZ currentBubblePoint,
                double gridTransverse)
            {
                Grid = grid;
                VisibleLine = visibleLine;
                DatumEnd = datumEnd;
                GridEndPoint = gridEndPoint;
                CurrentBubblePoint = currentBubblePoint;
                GridTransverse = gridTransverse;
            }
        }

        private sealed class DirectionGroup
        {
            public XYZ Direction { get; private set; }
            public List<GridRecord> Items { get; private set; }

            public DirectionGroup(XYZ direction)
            {
                Direction = direction;
                Items = new List<GridRecord>();
            }
        }

        private sealed class IsotonicBlock
        {
            public int Start { get; private set; }
            public int End { get; private set; }
            public double Mean { get; private set; }
            public int Count { get; private set; }

            public IsotonicBlock(int start, int end, double mean, int count)
            {
                Start = start;
                End = end;
                Mean = mean;
                Count = count;
            }
        }
    }
}
