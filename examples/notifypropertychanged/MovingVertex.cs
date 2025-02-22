﻿namespace Metalama.Samples.NotifyPropertyChanged;

[NotifyPropertyChanged]
internal partial class MovingVertex
{
    public double X { get; set; }

    public double Y { get; set; }

    public double DX { get; set; }

    public double DY { get; set; }

    public void ApplyTime( double time )
    {
        this.X += this.DX * time;
        this.Y += this.DY * time;
    }
}