using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ClearDesk
{
    public static class ZoneLayout
    {
        public const double CollapsedHeight = 52;
        public static void PrepareForStartup(Settings state, double left, double top, double width, double height)
        {
            if (state.StartCollapsed) foreach (Zone zone in state.Zones) zone.Collapsed = true;
            if (state.LayoutRevision < 3 || state.AutoArrangeZones) Arrange(Catalog.VisibleZones(state), left, top, width, height);
            state.LayoutRevision = 3;
        }
        public static void Arrange(IEnumerable<Zone> models, double left, double top, double width, double height)
        {
            var all = models.ToList();
            var zones = all.Where(z => !z.Locked).ToList(); if (zones.Count == 0) return;
            var occupied = all.Where(z => z.Locked).Select(z => new Rect(z.X, z.Y, z.Width, z.Collapsed ? CollapsedHeight : z.Height)).ToList();
            const double margin = 16, gap = 12;
            double availableWidth = Math.Max(240, width - margin * 2), availableHeight = Math.Max(160, height - margin * 2);
            bool collapsed = zones.All(z => z.Collapsed);
            int maxColumns = Math.Max(1, (int)((availableWidth + gap) / (300 + gap)));
            int desiredRows = Math.Max(1, (int)((availableHeight + gap) / ((collapsed ? CollapsedHeight : 240) + gap)));
            int columns = Math.Min(maxColumns, Math.Max(1, (int)Math.Ceiling((double)zones.Count / desiredRows)));
            int rows = (int)Math.Ceiling((double)zones.Count / columns);
            double cellWidth = Math.Max(240, Math.Min(310, (availableWidth - gap * (columns - 1)) / columns));
            double cellHeight = Math.Max(CollapsedHeight, (availableHeight - gap * (rows - 1)) / rows);
            // When more zones than the screen can fit are present, overlap compact rows inside
            // the work area, keeping every title accessible instead of placing them off-screen.
            double rowStep = collapsed ? Math.Min(CollapsedHeight + gap, Math.Max(4, (availableHeight - CollapsedHeight) / Math.Max(1, rows - 1))) : cellHeight + gap;
            for (int i = 0; i < zones.Count; i++)
            {
                Zone zone = zones[i]; int row = i % rows, column = i / rows;
                double targetHeight = zone.Collapsed ? CollapsedHeight : Math.Max(160, Math.Min(zone.Height, cellHeight));
                var candidate = new Rect(left + width - margin - cellWidth - column * (cellWidth + gap), top + margin + row * rowStep, cellWidth, targetHeight);
                if (occupied.Any(r => r.IntersectsWith(candidate)))
                {
                    bool found = false;
                    for (double x = left + width - margin - cellWidth; x >= left + margin && !found; x -= 24)
                        for (double y = top + margin; y + targetHeight <= top + height - margin; y += 16)
                        {
                            var alternative = new Rect(x, y, cellWidth, targetHeight);
                            if (occupied.Any(r => r.IntersectsWith(alternative))) continue;
                            candidate = alternative; found = true; break;
                        }
                    if (!found) { occupied.Add(new Rect(zone.X, zone.Y, zone.Width, zone.Collapsed ? CollapsedHeight : zone.Height)); continue; }
                }
                zone.Width = cellWidth;
                if (!zone.Collapsed) zone.Height = targetHeight;
                zone.X = candidate.X; zone.Y = candidate.Y;
                if (all.Any(z => z.Locked)) occupied.Add(candidate);
            }
        }
    }
}
