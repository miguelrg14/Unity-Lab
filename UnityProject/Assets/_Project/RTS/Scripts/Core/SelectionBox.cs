using System;

namespace UnityLab.RTS.Core
{
    /// <summary>Screen-space rectangle dragged with the mouse to select several units.</summary>
    public readonly struct SelectionBox
    {
        public readonly float XMin, YMin, XMax, YMax;

        SelectionBox(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        /// <summary>Builds the box from where the drag started and where the cursor is, in any direction.</summary>
        public static SelectionBox FromCorners(float x1, float y1, float x2, float y2) =>
            new SelectionBox(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));

        public float Width => XMax - XMin;
        public float Height => YMax - YMin;

        /// <summary>A drag shorter than this many pixels is treated as a click.</summary>
        public bool IsClick(float threshold = 4f) => Width < threshold && Height < threshold;

        /// <param name="depth">Distance in front of the camera; points behind it are never inside.</param>
        public bool Contains(float x, float y, float depth = 1f) =>
            depth > 0f && x >= XMin && x <= XMax && y >= YMin && y <= YMax;
    }

    /// <summary>What the army's base can train, and how much each unit costs.</summary>
    public enum UnitKind
    {
        Melee = 0,
        Artillery = 1,
        Harvester = 2,
    }

    /// <summary>Resources of one army.</summary>
    public sealed class Treasury
    {
        public Treasury(int stone, int wood)
        {
            Stone = stone;
            Wood = wood;
        }

        public int Stone { get; private set; }
        public int Wood { get; private set; }

        public void Deposit(int stone, int wood)
        {
            if (stone < 0 || wood < 0)
                throw new ArgumentOutOfRangeException(stone < 0 ? nameof(stone) : nameof(wood));
            Stone += stone;
            Wood += wood;
        }

        public bool CanAfford(int stone, int wood) => Stone >= stone && Wood >= wood;

        /// <returns>False, and nothing is spent, when the army cannot afford it.</returns>
        public bool TrySpend(int stone, int wood)
        {
            if (!CanAfford(stone, wood))
                return false;
            Stone -= stone;
            Wood -= wood;
            return true;
        }
    }
}
