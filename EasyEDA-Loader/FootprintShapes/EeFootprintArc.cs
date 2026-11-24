using PCB;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace EasyEDA_Loader
{
    public class EeFootprintArc : EeFootprintShape
    {
        public class ArcPath
        {
            public static ArcPath FromString(string path)
            {
                var figures = PathFigureCollection.Parse(path);
                if (figures.Count != 1 || figures[0].IsClosed || figures[0].Segments.Count != 1 ||
                    !(figures[0].Segments[0] is ArcSegment arc))
                    throw new FormatException("An EasyEDA footprint arc must contain one open arc segment.");

                var start = figures[0].StartPoint;
                var result = new ArcPath
                {
                    StartPoint = new Point(ConvertToMM(start.X), ConvertToMM(start.Y)),
                    EndPoint = new Point(ConvertToMM(arc.Point.X), ConvertToMM(arc.Point.Y)),
                    Size = new Size(ConvertToMM(arc.Size.Width), ConvertToMM(arc.Size.Height)),
                    RotationAngle = arc.RotationAngle,
                    IsLargeArc = arc.IsLargeArc,
                    Sweep = arc.SweepDirection
                };

                if (result.IsNativeArc)
                {
                    (result.X, result.Y, result.Radius, result.StartAngle, result.EndAngle) =
                        SvgArcUtils.ComputeArc(result.StartPoint.X, result.StartPoint.Y, result.Size.Width,
                            result.Size.Height, result.RotationAngle, result.IsLargeArc,
                            result.Sweep == SweepDirection.Clockwise, result.EndPoint.X, result.EndPoint.Y);
                }
                return result;
            }

            public Point StartPoint { get; private set; }
            public Point EndPoint { get; private set; }
            public Size Size { get; private set; }
            public double RotationAngle { get; private set; }
            public bool IsLargeArc { get; private set; }
            internal bool IsNativeArc => Size.Width > 0 && Size.Width == Size.Height && StartPoint != EndPoint;

            public double X { get; set; }
            public double Y { get; set; }
            public double Radius { get; set; }
            public double StartAngle { get; set; }
            public double EndAngle { get; set; }
            public SweepDirection Sweep { get; set; }

            internal PathGeometry CreateGeometry(double offsetX, double offsetY)
            {
                var start = new Point(StartPoint.X - offsetX, StartPoint.Y - offsetY);
                var end = new Point(EndPoint.X - offsetX, EndPoint.Y - offsetY);
                var figure = new PathFigure { StartPoint = start, IsFilled = false };
                if (start != end)
                {
                    PathSegment segment = Size.Width == 0 || Size.Height == 0
                        ? (PathSegment)new LineSegment(end, true)
                        : new ArcSegment(end, Size, RotationAngle, IsLargeArc, Sweep, true);
                    figure.Segments.Add(segment);
                }
                return new PathGeometry(new[] { figure });
            }
        }

        public static EeFootprintArc FromString(string data)
        {
            var parts = data.Split(new[] { "~" }, StringSplitOptions.None);
            return new EeFootprintArc
            {
                StrokeWidth = ConvertToMM(double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)),
                LayerId = parts[2],
                Net = parts[3],
                Path = ArcPath.FromString(parts[4]),
                HelperDots = parts[5],
                Id = parts[6],
                IsLocked = ParseBoolean(parts[7]),
            };
        }

        public override List<UIElement> AddToCanvas(Canvas c, EeFootprintContext ctx)
        {
            return new List<UIElement>
            {
                new Path
                {
                    Stroke = new SolidColorBrush(ColorHelper.FromHex(ctx.Layers.GetLayerColor(LayerId))),
                    StrokeThickness = StrokeWidth,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = Path.CreateGeometry(ctx.Box.X, ctx.Box.Y)
                }
            };
        }

        public override bool AddToComponent(IPCB_LibComponent c, EeFootprintContext ctx)
        {
            TLayerConstant targetLayer;
            var layer = ctx.Layers.GetLayer(LayerId);
            if (layer != null)
            {
                try
                {
                    targetLayer = EEPCB.EELayerToAltium(layer.Name);

                    if (Path.IsNativeArc)
                    {
                        // Altium stores counterclockwise angles in a Y-up coordinate system.
                        bool counterclockwise = Path.Sweep == SweepDirection.Counterclockwise;
                        double startAngle = ConvertAngle(counterclockwise ? Path.StartAngle : Path.EndAngle, ctx);
                        double endAngle = ConvertAngle(counterclockwise ? Path.EndAngle : Path.StartAngle, ctx);
                        var arc = EEPCB.CreateArc(c, targetLayer, ConvertX(Path.X, ctx), ConvertY(Path.Y, ctx),
                            Path.Radius, StrokeWidth, startAngle, endAngle);
                        if (arc != null) EEPCB.AddToPCB(c, arc);
                    }
                    else if (Path.StartPoint != Path.EndPoint)
                    {
                        AddTracks(c, ctx, targetLayer);
                    }
                }
                catch (Exception ex)
                {
                    if (ctx.Exception != null && !ctx.Exception(ex))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private void AddTracks(IPCB_LibComponent component, EeFootprintContext context, TLayerConstant layer)
        {
            // PCB arc primitives are circular. Approximate an elliptical arc within 5 μm.
            const double toleranceMm = 0.005;
            var geometry = Path.CreateGeometry(0, 0).GetFlattenedPathGeometry(toleranceMm, ToleranceType.Absolute);
            foreach (var figure in geometry.Figures)
            {
                Point start = figure.StartPoint;
                foreach (var segment in figure.Segments)
                {
                    if (segment is LineSegment line)
                    {
                        AddTrack(start, line.Point);
                        start = line.Point;
                    }
                    else if (segment is PolyLineSegment polyline)
                    {
                        foreach (Point end in polyline.Points)
                        {
                            AddTrack(start, end);
                            start = end;
                        }
                    }
                }
            }

            void AddTrack(Point start, Point end)
            {
                if (start == end) return;
                var track = EEPCB.CreateLine(component, layer,
                    ConvertX(start.X, context), ConvertY(start.Y, context),
                    ConvertX(end.X, context), ConvertY(end.Y, context), StrokeWidth);
                if (track != null) EEPCB.AddToPCB(component, track);
            }
        }

        public double StrokeWidth { get; set; }
        public string LayerId { get; set; }
        public string Net { get; set; }
        public ArcPath Path { get; set; }
        public string HelperDots { get; set; }
        public string Id { get; set; }
        public bool IsLocked { get; set; }
    }

}
