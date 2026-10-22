// ********************************************************************************************************************************************
//      CSpect Monitor extension - a debugger front end: registers, disassembly, stack and memory in one window
//      Released under the GNU 3 license - please see license file for more details
// ********************************************************************************************************************************************
using Plugin;

namespace Monitor
{
    // *********************************************************************************************************
    /// <summary>
    ///     One disassembled instruction
    /// </summary>
    // *********************************************************************************************************
    public sealed class DisasmLine
    {
        /// <summary>64k address</summary>
        public int Address;
        /// <summary>Instruction length in bytes</summary>
        public int Length;
        /// <summary>The instruction bytes</summary>
        public byte[] Bytes;
        /// <summary>Instruction text (labels already substituted by CSpect)</summary>
        public string Text;
        /// <summary>Label at this address, or null</summary>
        public string Label;
        /// <summary>Is there a breakpoint here?</summary>
        public bool Breakpoint;
        /// <summary>T-States (and the "branch taken" count, or 0)</summary>
        public int TStates1, TStates2;
    }

    // *********************************************************************************************************
    /// <summary>
    ///     Everything the window shows, captured on the emulator thread (MonitorPlugin.Tick).
    ///     Never modified after it's published, so the UI can read it without locking.
    /// </summary>
    // *********************************************************************************************************
    public sealed class MonitorSnapshot
    {
        /// <summary>Stopped in the debugger?</summary>
        public bool Stopped;
        /// <summary>Bumped every time the CPU is stepped or stops, so the UI can highlight what changed</summary>
        public int StopCount;
        public Z80Regs Regs;
        /// <summary>The CPU's 64k view of memory</summary>
        public byte[] Memory;
        /// <summary>MMU slots 0-7 (NextReg $50-$57)</summary>
        public byte[] Mmu;
        /// <summary>Disassembly for the lines the UI asked for</summary>
        public DisasmLine[] Disasm;
        /// <summary>Labels for the words at SP-8 .. SP+(2*StackWords) (index 0 = SP-8)</summary>
        public string[] StackLabels;
        public const int StackBelow = 4, StackWords = 48;
        /// <summary>CSpect's own debugger screen hidden ("remote" mode)?</summary>
        public bool Remote;
    }
}
