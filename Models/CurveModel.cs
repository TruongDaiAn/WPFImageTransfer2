using System.Collections.Generic;
using System.Windows;

namespace WPFImageTransfer.Models
{
    public sealed class CurveModel
    {
        public Dictionary<string, List<Point>> Channels { get; } = new();
        public string CurrentChannel { get; set; } = "RGB";

        public CurveModel()
        {
            foreach (string channel in new[] { "RGB", "Red", "Green", "Blue" })
                Channels[channel] = new List<Point>();
        }
    }
}