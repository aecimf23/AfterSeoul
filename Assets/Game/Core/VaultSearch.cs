using System;

namespace AfterSeoul.Core
{
    public sealed class VaultSearch
    {
        public const int Size = 4, CellCount = 16, HazardCount = 3;
        private readonly bool[] _hazards = new bool[CellCount], _open = new bool[CellCount];
        private readonly Random _random;
        private bool _generated;
        public int Opened { get; private set; }
        public int Scans { get; private set; } = 2;
        public bool Resolved { get; private set; }
        public bool Collapsed { get; private set; }
        public double Score { get; private set; }
        public int Multiplier => Opened >= 10 ? 4 : Opened >= 6 ? 2 : 1;
        public VaultSearch(int seed) { _random = new Random(seed); }
        public bool IsOpen(int cell) => cell >= 0 && cell < CellCount && _open[cell];
        public bool IsHazard(int cell) => cell >= 0 && cell < CellCount && _hazards[cell];
        public int Nearby(int cell)
        {
            int count = 0;
            for (int other = 0; other < CellCount; other++)
                if (other != cell && Math.Abs(other / Size - cell / Size) <= 1 &&
                    Math.Abs(other % Size - cell % Size) <= 1 && _hazards[other]) count++;
            return count;
        }
        private void Generate(int first)
        {
            int placed = 0;
            while (placed < HazardCount)
            {
                int cell = _random.Next(CellCount);
                if (cell == first || _hazards[cell]) continue;
                _hazards[cell] = true; placed++;
            }
            _generated = true;
        }
        public bool Open(int cell)
        {
            if (Resolved || cell < 0 || cell >= CellCount || _open[cell]) return false;
            if (!_generated) Generate(cell); // The first touch is always safe.
            _open[cell] = true;
            if (_hazards[cell]) { Collapsed = true; Resolved = true; Score = 0; }
            else { Opened++; if (Opened >= 10) Bank(); }
            return true;
        }
        public bool Scan()
        {
            if (Resolved || Scans <= 0) return false;
            if (!_generated) Generate(_random.Next(CellCount));
            int start = _random.Next(CellCount);
            for (int i = 0; i < CellCount; i++)
            {
                int cell = (start + i) % CellCount;
                if (_hazards[cell] || _open[cell]) continue;
                Scans--; return Open(cell);
            }
            return false;
        }
        public void Bank()
        {
            if (Resolved || Opened < 3) return;
            Score = Opened >= 10 ? 1 : Opened >= 6 ? .7 : .4;
            Resolved = true;
        }
    }
}
