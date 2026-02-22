using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Dali.UI.ViewModels;

namespace Dali.UI.Controls
{
    /// <summary>
    /// Code-behind for ControllerVisualizationControl.
    /// Responsible for:
    ///  - Registering port and line-card elements as they load.
    ///  - Drawing throttled bezier connector lines between each output port
    ///    (right-center) and its corresponding line card (left-center).
    /// No Revit API usage. UI-only, read-only visualization.
    /// </summary>
    public partial class ControllerVisualizationControl : UserControl
    {
        #region Fields

        // Registered elements keyed by OutputNumber (ports) and LineName (line cards).
        private readonly Dictionary<int, FrameworkElement> _portElements
            = new Dictionary<int, FrameworkElement>();
        private readonly Dictionary<string, FrameworkElement> _lineCardElements
            = new Dictionary<string, FrameworkElement>();

        // Throttle: request a redraw at most once per timer interval.
        private readonly DispatcherTimer _redrawTimer;
        private bool _redrawPending;

        #endregion

        #region Constructor

        public ControllerVisualizationControl()
        {
            InitializeComponent();

            // Connector redraw throttle: fire every 75 ms if a redraw is pending.
            _redrawTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(75)
            };
            _redrawTimer.Tick += OnRedrawTimerTick;
            _redrawTimer.Start();

            // Redraw when the control itself is first laid out.
            Loaded += OnControlLoaded;
            // Redraw when the DataContext (ControllerVizVm) changes.
            DataContextChanged += OnDataContextChanged;
            // Redraw on resize.
            SizeChanged += (_, __) => RequestRedraw();
        }

        #endregion

        #region Public helper for DataContext null visibility

        // Used by the main window binding to show/hide the "select a controller" hint.
        // Not needed in XAML here because we simplified that out.

        #endregion

        #region Element registration

        /// <summary>
        /// Called from the DataTemplate when an output port border is loaded.
        /// Tag = OutputNumber (int).
        /// </summary>
        private void OutputPort_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is int outputNum)
            {
                _portElements[outputNum] = fe;
                RequestRedraw();
            }
        }

        /// <summary>
        /// Called from the DataTemplate when a line card border is loaded.
        /// Tag = LineName (string).
        /// </summary>
        private void LineCard_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string lineName
                && !string.IsNullOrEmpty(lineName))
            {
                _lineCardElements[lineName] = fe;
                RequestRedraw();
            }
        }

        #endregion

        #region Layout / resize events

        private void OnControlLoaded(object sender, RoutedEventArgs e)
        {
            RequestRedraw();
        }

        private void ContentRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RequestRedraw();
        }

        #endregion

        #region DataContext change

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Clear stale registrations when the controller selection changes.
            _portElements.Clear();
            _lineCardElements.Clear();
            ConnectorCanvas.Children.Clear();
            RequestRedraw();
        }

        #endregion

        #region Redraw throttle

        private void RequestRedraw()
        {
            _redrawPending = true;
        }

        private void OnRedrawTimerTick(object sender, EventArgs e)
        {
            if (!_redrawPending) return;
            _redrawPending = false;
            DrawConnectors();
        }

        #endregion

        #region Connector drawing

        /// <summary>
        /// Clears and redraws all bezier connector paths.
        /// Connects each registered port's right-center to each matching line card's left-center.
        /// The VizVm provides the mapping: Output i has Lines[j] whose LineName is the key.
        /// </summary>
        private void DrawConnectors()
        {
            ConnectorCanvas.Children.Clear();

            var vizVm = DataContext as ControllerVizVm;
            if (vizVm == null) return;

            foreach (var outputVm in vizVm.Outputs)
            {
                if (!_portElements.TryGetValue(outputVm.OutputNumber, out var portElem)) continue;

                foreach (var lineVm in outputVm.Lines)
                {
                    if (!_lineCardElements.TryGetValue(lineVm.LineName ?? string.Empty, out var cardElem)) continue;

                    // Compute anchor points relative to ConnectorCanvas.
                    Point? portAnchor = GetRightCenter(portElem, ConnectorCanvas);
                    Point? cardAnchor = GetLeftCenter(cardElem, ConnectorCanvas);

                    if (portAnchor == null || cardAnchor == null) continue;

                    var path = BuildBezierPath(portAnchor.Value, cardAnchor.Value);
                    ConnectorCanvas.Children.Add(path);
                }
            }
        }

        /// <summary>Returns the right-center of an element in connector-canvas coordinates.</summary>
        private static Point? GetRightCenter(FrameworkElement elem, Canvas canvas)
        {
            try
            {
                if (!elem.IsLoaded || elem.ActualWidth == 0) return null;
                var transform = elem.TransformToAncestor(canvas);
                var topLeft = transform.Transform(new Point(0, 0));
                return new Point(topLeft.X + elem.ActualWidth, topLeft.Y + elem.ActualHeight / 2.0);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Returns the left-center of an element in connector-canvas coordinates.</summary>
        private static Point? GetLeftCenter(FrameworkElement elem, Canvas canvas)
        {
            try
            {
                if (!elem.IsLoaded || elem.ActualHeight == 0) return null;
                var transform = elem.TransformToAncestor(canvas);
                var topLeft = transform.Transform(new Point(0, 0));
                return new Point(topLeft.X, topLeft.Y + elem.ActualHeight / 2.0);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Builds a cubic bezier Path from start (port) to end (line card).
        /// Control points are offset horizontally to give a smooth S-curve.
        /// </summary>
        private static Path BuildBezierPath(Point start, Point end)
        {
            double dx = Math.Abs(end.X - start.X);
            double offset = Math.Max(dx * 0.5, 20.0);

            var segment = new BezierSegment(
                new Point(start.X + offset, start.Y),
                new Point(end.X - offset, end.Y),
                end,
                isStroked: true);

            var figure = new PathFigure
            {
                StartPoint = start,
                IsClosed = false
            };
            figure.Segments.Add(segment);

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return new Path
            {
                Data = geometry,
                Stroke = new SolidColorBrush(Color.FromArgb(180, 112, 186, 188)), // #70babc with alpha
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                SnapsToDevicePixels = true
            };
        }

        #endregion
    }
}
