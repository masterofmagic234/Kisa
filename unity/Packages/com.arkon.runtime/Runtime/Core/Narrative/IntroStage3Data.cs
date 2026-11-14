using System.Collections.Generic;

namespace Arkon.Core
{
    /// <summary>Typed schema3 data in the existing Godot 768x432 design frame.</summary>
    public sealed class IntroStage3Data
    {
        public Dictionary<string, IntroArtRegion> Art = new Dictionary<string, IntroArtRegion>();
        public Dictionary<string, string[]> Poses = new Dictionary<string, string[]>();
        public Dictionary<string, IntroWheelData[]> Wheels = new Dictionary<string, IntroWheelData[]>();
        public Dictionary<string, IntroFleetData> Formations = new Dictionary<string, IntroFleetData>();
        public IntroPanelShotData[] Shots;
        public double Duration;
    }

    public sealed class IntroArtRegion
    {
        public string Id, Asset;
        public IntroRect Source;
    }

    public sealed class IntroPanelShotData
    {
        public string Id, Label;
        public int Block;
        public double Start, End;
        public IntroPanelData[] Panels;
    }

    public sealed class IntroPanelData
    {
        public IntroRect Rect = new IntroRect(0, 0, 768, 432);
        public IntroRect Crop;
        public string Art;
        public double Reveal;
        public bool Mirror;
        public double[] Tone = { 1, 1, 1 };
        public double[][] Camera = System.Array.Empty<double[]>();
        public IntroSpriteLayer[] Behind = System.Array.Empty<IntroSpriteLayer>();
        public IntroSpriteLayer[] Layers = System.Array.Empty<IntroSpriteLayer>();
        public IntroTrafficData Traffic;
    }

    public sealed class IntroSpriteLayer
    {
        public string Art, Pose;
        public double X, Y;
        public double Scale = 1;
        public bool Ease = true;
        public double[][] Motion = System.Array.Empty<double[]>();
        public double[][] Phase = System.Array.Empty<double[]>();
        public double[][] Wipe = System.Array.Empty<double[]>();
    }

    public sealed class IntroTrafficData
    {
        public string Fleet;
        public double Rate, Offset;
    }

    public sealed class IntroFleetData
    {
        public string Projection;
        public IntroVehicleData[] Vehicles;
    }

    public sealed class IntroVehicleData
    {
        public string Art;
        public double X, DepthOrY, NearWidth, AnchorX, AnchorY;
    }

    public sealed class IntroWheelData
    {
        public string Pose;
        public double X, Y;
    }
}
