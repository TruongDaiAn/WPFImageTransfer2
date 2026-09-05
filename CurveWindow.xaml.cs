using Emgu.CV;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WPFImageTransfer.Helpers;
using WPFImageTransfer.Controllers;
using WPFImageTransfer.Models;
using WPFImageTransfer.Services.Processing;

namespace WPFImageTransfer
{
    public partial class CurveWindow : Window
    {
        public Mat? ResultLUTMat { get; private set; }
        private readonly BitmapSource _originalPreview;
        private readonly Mat _cachedOriginalMat;

        private readonly Dictionary<string, List<Point>> _channels = new Dictionary<string, List<Point>>();
        private string _currentChannel = "RGB";
        private Point? _draggingPoint = null;
        private const double POINT_RADIUS = 5.0;
        private readonly CurveModel _curveModel = new CurveModel();
        private readonly CurveController _curveController = new CurveController();

        public CurveWindow(BitmapSource originalSource)
        {
            InitializeComponent();
            _originalPreview = originalSource;
            _cachedOriginalMat = ImageConversionHelper.BitmapSourceToMat(originalSource);
            ResultLUTMat = null;

            this.Closed += (s, e) =>
            {
                _cachedOriginalMat?.Dispose();
            };

            _channels["RGB"] = _curveModel.Channels["RGB"];
            _channels["Red"] = _curveModel.Channels["Red"];
            _channels["Green"] = _curveModel.Channels["Green"];
            _channels["Blue"] = _curveModel.Channels["Blue"];

            ResetPoints();
            UpdatePreview();
        }

        private void ResetPoints()
        {
            foreach (var key in _channels.Keys)
            {
                _channels[key].Clear();
                _channels[key].Add(new Point(0, 255));
                _channels[key].Add(new Point(255, 0));
            }
            UpdateCurve();
        }

        private void CmbChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbChannel != null && cmbChannel.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                _currentChannel = item.Tag.ToString() ?? "RGB";
                _curveModel.CurrentChannel = _currentChannel;
                if (CurveCanvas != null)
                {
                    UpdateCurve();
                }
            }
        }

        private void DrawCurve()
        {
            List<Ellipse> elist = CurveCanvas.Children.OfType<Ellipse>().ToList();
            foreach (var el in elist) CurveCanvas.Children.Remove(el);
            List<Polyline> llist = CurveCanvas.Children.OfType<Polyline>().ToList();
            foreach (var l in llist) CurveCanvas.Children.Remove(l);

            var activePts = _channels[_currentChannel];
            activePts = activePts.OrderBy(p => p.X).ToList();
            activePts[0] = new Point(0, activePts[0].Y);
            activePts[activePts.Count - 1] = new Point(255, activePts[activePts.Count - 1].Y);
            _channels[_currentChannel] = activePts;

            string[] renderOrder = new string[] { "RGB", "Red", "Green", "Blue" };
            foreach (var ch in renderOrder)
            {
                if (!_channels.ContainsKey(ch)) continue;
                var pts = _channels[ch];
                if (pts.Count < 2) continue;

                List<Point> smoothCurve = CurveLutService.GenerateSmoothCurve(pts);

                Polyline pline = new Polyline
                {
                    Points = new System.Windows.Media.PointCollection(smoothCurve),
                    StrokeThickness = (ch == _currentChannel) ? 2 : 1
                };

                if (ch == "Red") pline.Stroke = (ch == _currentChannel) ? Brushes.Red : new SolidColorBrush(Color.FromArgb(120, 255, 0, 0));
                else if (ch == "Green") pline.Stroke = (ch == _currentChannel) ? Brushes.Lime : new SolidColorBrush(Color.FromArgb(120, 0, 255, 0));
                else if (ch == "Blue") pline.Stroke = (ch == _currentChannel) ? Brushes.DeepSkyBlue : new SolidColorBrush(Color.FromArgb(120, 0, 191, 255));
                else pline.Stroke = (ch == _currentChannel) ? Brushes.White : new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));

                if (ch != _currentChannel) Canvas.SetZIndex(pline, 0);
                else Canvas.SetZIndex(pline, 10);

                CurveCanvas.Children.Add(pline);

                if (ch == _currentChannel)
                {
                    foreach (var p in pts)
                    {
                        Ellipse ellipse = new Ellipse
                        {
                            Width = POINT_RADIUS * 2,
                            Height = POINT_RADIUS * 2,
                            Fill = Brushes.Cyan,
                            Stroke = Brushes.White,
                            StrokeThickness = 1,
                            Tag = p
                        };
                        Canvas.SetLeft(ellipse, p.X - POINT_RADIUS);
                        Canvas.SetTop(ellipse, p.Y - POINT_RADIUS);
                        Canvas.SetZIndex(ellipse, 20);
                        CurveCanvas.Children.Add(ellipse);
                    }
                }
            }
        }

        private void UpdateCurve()
        {
            DrawCurve();
            UpdatePreview();
        }

        private async void UpdatePreview()
        {
            var resultSource = await _curveController.PreviewAsync(_cachedOriginalMat, _curveModel);
            if (resultSource != null)
                ImgPreview.Source = resultSource;
        }

        private Point? GetPointNear(Point clickPoint)
        {
            foreach (var p in _channels[_currentChannel])
            {
                if (Math.Abs(p.X - clickPoint.X) <= POINT_RADIUS * 2 &&
                    Math.Abs(p.Y - clickPoint.Y) <= POINT_RADIUS * 2)
                    return p;
            }
            return null;
        }

        private void CurveCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(CurveCanvas);
            Point? match = GetPointNear(p);
            if (match.HasValue)
            {
                _draggingPoint = match;
            }
            else
            {
                if (_channels[_currentChannel].Count < 15)
                {
                    Point newP = new Point(Math.Max(0, Math.Min(255, p.X)), Math.Max(0, Math.Min(255, p.Y)));
                    _channels[_currentChannel].Add(newP);
                    _draggingPoint = newP;
                    UpdateCurve();
                }
            }
            CurveCanvas.CaptureMouse();
        }

        private void CurveCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingPoint.HasValue && e.LeftButton == MouseButtonState.Pressed)
            {
                Point p = e.GetPosition(CurveCanvas);
                p.X = Math.Max(0, Math.Min(255, p.X));
                p.Y = Math.Max(0, Math.Min(255, p.Y));

                var pts = _channels[_currentChannel];
                var idx = pts.IndexOf(_draggingPoint.Value);
                if (idx == -1) idx = pts.FindIndex(c => c.X == _draggingPoint.Value.X && c.Y == _draggingPoint.Value.Y);
                if (idx == -1) return;

                if (idx == 0) p.X = 0;
                if (idx == pts.Count - 1) p.X = 255;

                if (idx > 0 && p.X <= pts[idx - 1].X)
                {
                    p.X = pts[idx - 1].X + 2;
                }
                if (idx < pts.Count - 1 && p.X >= pts[idx + 1].X)
                {
                    p.X = pts[idx + 1].X - 2;
                }

                pts[idx] = p;
                _channels[_currentChannel] = pts;
                _draggingPoint = p;
                UpdateCurve();
            }
        }

        private void CurveCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggingPoint = null;
            CurveCanvas.ReleaseMouseCapture();
        }

        private void CurveCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(CurveCanvas);
            Point? match = GetPointNear(p);
            if (match.HasValue)
            {
                var pts = _channels[_currentChannel];
                int idx = pts.IndexOf(match.Value);
                if (idx != 0 && idx != pts.Count - 1)
                {
                    pts.RemoveAt(idx);
                    _channels[_currentChannel] = pts;
                    UpdateCurve();
                }
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ResetPoints();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ResultLUTMat = _curveController.CreateLut(_curveModel);
            this.DialogResult = true;
            this.Close();
        }
    }
}
