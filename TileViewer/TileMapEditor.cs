using System;
using System.Collections.Generic;
using Plugin;

namespace TileViewer
{
    internal sealed class TileMapEdit
    {
        internal byte MapBase, Control, DefaultAttribute, Attribute;
        internal int Cell, Tile;
        internal bool Clear, Undo;

        internal static TileMapEdit Paint(TileSnapshot frame, int cell, int tile, byte attribute, bool clear)
        {
            return new TileMapEdit { MapBase = frame.MapBase, Control = frame.Control,
                DefaultAttribute = frame.DefaultAttribute, Cell = cell, Tile = tile, Attribute = attribute, Clear = clear };
        }
    }

    // UI commands are serialized with undo history. Tick applies running-machine edits;
    // OSTick may apply them while the emulator is paused in its debugger.
    internal sealed class TileMapEditor
    {
        private readonly object gate = new object();
        private readonly Queue<TileMapEdit> pending = new Queue<TileMapEdit>();
        private readonly Stack<Change> history = new Stack<Change>();
        private string status;
        private const int LayoutMask = 0x6a; // columns, attributes, text mode, 512 mode

        internal bool CanUndo { get { lock (gate) return history.Count != 0; } }
        internal bool HasPending { get { lock (gate) return pending.Count != 0; } }
        internal void Enqueue(TileMapEdit edit) { lock (gate) pending.Enqueue(edit); }
        internal string TakeStatus() { lock (gate) { string text = status; status = null; return text; } }
        internal void Reset() { lock (gate) { pending.Clear(); history.Clear(); status = null; } }

        internal bool ApplyPending(iCSpect cspect)
        {
            bool changed = false;
            lock (gate)
            {
                while (pending.Count != 0)
                {
                    TileMapEdit edit = pending.Dequeue();
                    changed |= edit.Undo ? Undo(cspect) : Apply(cspect, edit);
                }
            }
            return changed;
        }

        private bool Matches(iCSpect cspect, TileMapEdit edit)
        {
            return cspect.GetNextRegister(0x6e) == edit.MapBase &&
                (cspect.GetNextRegister(0x6b) & LayoutMask) == (edit.Control & LayoutMask) &&
                ((edit.Control & 0x20) == 0 || cspect.GetNextRegister(0x6c) == edit.DefaultAttribute);
        }

        private bool Apply(iCSpect cspect, TileMapEdit edit)
        {
            if (!Matches(cspect, edit)) { status = "Map layout changed; edit skipped. Pick a cell again."; return false; }
            bool attributes = (edit.Control & 0x20) == 0;
            bool extended = (edit.Control & 2) != 0;
            int columns = (edit.Control & 0x40) == 0 ? 40 : 80;
            if (edit.Cell < 0 || edit.Cell >= columns * 32 || edit.Tile < 0 || edit.Tile >= (extended ? 512 : 256))
            { status = "Tile or cell is outside the active map mode."; return false; }
            if (!attributes && extended && (edit.Tile >> 8) != (edit.DefaultAttribute & 1))
            { status = "This attribute-free map uses the tile range selected by NextReg $6C."; return false; }

            int length = attributes ? 2 : 1;
            int address = Address(edit.MapBase, edit.Cell * length);
            byte[] before = cspect.PeekPhysicalULA(address, length);
            byte attribute = edit.Clear && attributes ? before[1] : edit.Attribute;
            if (extended) attribute = (byte)((attribute & 0xfe) | (edit.Tile >> 8));
            byte[] after = attributes ? new[] { (byte)edit.Tile, attribute } : new[] { (byte)edit.Tile };
            if (Equal(before, after)) return false;
            cspect.PokePhysicalULA(address, after);
            history.Push(new Change { Context = edit, Address = address, Before = before, After = after });
            status = string.Format("{0} cell {1},{2}. Undo cell restores its previous value.", edit.Clear ? "Cleared" : "Painted", edit.Cell % columns, edit.Cell / columns);
            return true;
        }

        private bool Undo(iCSpect cspect)
        {
            if (history.Count == 0) return false;
            Change change = history.Pop();
            if (!Matches(cspect, change.Context)) { status = "Map layout changed; undo skipped."; return false; }
            if (!Equal(cspect.PeekPhysicalULA(change.Address, change.After.Length), change.After))
            { status = "The program changed this cell; undo skipped."; return false; }
            cspect.PokePhysicalULA(change.Address, change.Before);
            status = "Restored the previous cell value.";
            return true;
        }

        internal static int Address(byte mapBase, int offset)
        {
            bool bank7 = (mapBase & 0x80) != 0;
            return (bank7 ? 7 : 5) * 16384 + ((((mapBase & 0x3f) << 8) + offset) & (bank7 ? 0x1fff : 0x3fff));
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private sealed class Change
        {
            internal TileMapEdit Context;
            internal int Address;
            internal byte[] Before, After;
        }
    }
}
