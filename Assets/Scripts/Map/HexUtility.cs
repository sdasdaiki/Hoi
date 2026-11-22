using UnityEngine;

namespace EdoStrategy.Map
{
    public readonly struct HexCoordinates
    {
        public readonly int q;
        public readonly int r;

        public HexCoordinates(int q, int r)
        {
            this.q = q;
            this.r = r;
        }

        public static HexCoordinates operator +(HexCoordinates a, HexCoordinates b)
        {
            return new HexCoordinates(a.q + b.q, a.r + b.r);
        }
    }

    public static class HexUtility
    {
        public static readonly HexCoordinates[] AxialDirections =
        {
            new HexCoordinates(1, 0),
            new HexCoordinates(0, 1),
            new HexCoordinates(-1, 1),
            new HexCoordinates(-1, 0),
            new HexCoordinates(0, -1),
            new HexCoordinates(1, -1)
        };

        public static HexCoordinates OffsetToAxial(int x, int y)
        {
            int q = x - (y - (y & 1)) / 2;
            return new HexCoordinates(q, y);
        }

        public static Vector2Int AxialToOffset(int q, int r)
        {
            int x = q + (r - (r & 1)) / 2;
            return new Vector2Int(x, r);
        }

        public static int Distance(HexCoordinates a, HexCoordinates b)
        {
            int deltaQ = a.q - b.q;
            int deltaR = a.r - b.r;
            return (Mathf.Abs(deltaQ) + Mathf.Abs(deltaQ + deltaR) + Mathf.Abs(deltaR)) / 2;
        }

        public static int DistanceOffset(int x1, int y1, int x2, int y2)
        {
            return Distance(OffsetToAxial(x1, y1), OffsetToAxial(x2, y2));
        }

        public static Vector2Int GetNeighborOffset(int x, int y, int directionIndex)
        {
            HexCoordinates axial = OffsetToAxial(x, y);
            int index = ((directionIndex % 6) + 6) % 6;
            HexCoordinates neighbor = axial + AxialDirections[index];
            return AxialToOffset(neighbor.q, neighbor.r);
        }
    }
}
