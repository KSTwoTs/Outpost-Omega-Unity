using System;

namespace Outpost.Content
{
    /// <summary>One exported battlefield (StreamingAssets/Outpost/maps/ID*.{json,png,bin}).</summary>
    public class MapData
    {
        public string id, name, theme;
        public int tile, w, h, px_w, px_h, height_step, height_w, height_h, max_slots;
        public string[] tiles;
        public SiteData[] sites;
        public float[][] relays;
        public float[][] crates;

        [NonSerialized] public float[] heights;

        public class SiteData
        {
            public int id;
            public float x, y;
            public string main;
            public bool wild;
            public float[][] slots;
            public float[][] sockets;
        }
    }
}
