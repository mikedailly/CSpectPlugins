using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;


/*
 * The used socket protocol is simple. It consists of header and payload.
 *
 * Message:
 * int Length: The length of the following bytes containing Command. Little endian. Size=4.
 * byte SeqNo:  Sequence number (must be returned in response)
 * byte Command: UART_DATA.
 * Payload bytes: The data
 *
 * A client may connect at anytime.
 * A connection is terminated only by the client.
 * If a connection has been terminated a new connection can be established.
 *
 */


namespace DeZogPlugin
{
    /**
     * Handles the socket commands, responses and notifications.
     */
    public class Commands
    {
        protected static byte[] DZRP_VERSION = { 2, 0, 0 };

        /**
         * The break reason.
         */
        protected enum BreakReason
        {
            NO_REASON = 0,
            MANUAL_BREAK = 1,
            BREAKPOINT_HIT = 2,
            WATCHPOINT_READ = 3,
            WATCHPOINT_WRITE = 4,
            OTHER = 255,
        }


        /**
         * The alternate command for CMD_CONTINUE.
         */
        protected enum AlternateCommand
        {
            CONTINUE = 0,   // I.e. no alternate command
            STEP_OVER = 1,
            STEP_OUT = 2
        }

        // Used for locking
        protected static object lockObj = new Object();

        // Index for setting a byte in the array.
        protected static int Index;

        // The data array for preparing data to send.
        protected static byte[] Data;

        // Temporary breakpoint (addresses) for Continue. -1 = unused.
        protected static int TmpBreakpoint1;
        protected static int TmpBreakpoint2;


        // The breakpoint map to keep the IDs and addresses.
        // If it is null then the connection is not active.
        protected static Dictionary<ushort,int> BreakpointMap = null;

        // The last breakpoint ID used.
        protected static ushort LastBreakpointId;

        // Action queue.
        //protected static List<Action> ActionQueue = new List<Action>();

        // Stores the previous debugger state.
        protected static bool CpuRunning = false;

        // Stores if a PAUSE command has been sent.
        protected static bool ManualBreak = false;


        /**
         * General initalization function.
         */
        public static void Init()
        {
            lock (lockObj)
            {
                var cspect = Main.CSpect;
                BreakpointMap = new Dictionary<ushort, int>();
                LastBreakpointId = 0;
                TmpBreakpoint1 = -1;
                TmpBreakpoint2 = -1;
                // Stop
                CpuRunning = false;
                cspect.Debugger(Plugin.eDebugCommand.Enter);
                // Clear any pending interrupt
                var regs = cspect.GetRegs();
                var prevRegs = regs;
                regs.IFF1 = false;
                regs.IFF2 = false;
                cspect.SetRegs(regs);   // This seems to clear a pending interrupt
                // Restore original value
                cspect.SetRegs(prevRegs);
            }
        }


        /**
         * Resets the breakpoint map.
         * Is called so that 'tick' does not process ticks anymore.
         */
        public static void Reset()
        {
            lock (lockObj)
            {
                BreakpointMap = null;
                LastBreakpointId = 0;
                TmpBreakpoint1 = -1;
                TmpBreakpoint2 = -1;
                CpuRunning = false;
                // Clear breakpoints
                ClearAllBreakAndWatchpoints();
            }
        }


        /**
         * Initializes the data buffer.
         */
        protected static void InitData(int size)
        {
            Index = 0;
            Data = new byte[size];
        }


        /**
         * Returns the DZRP version as a string.
         */
        public static string GetDzrpVersion()
        {
            string version = "" + DZRP_VERSION[0] + '.' + DZRP_VERSION[1] + '.' + DZRP_VERSION[2];
            return version;
        }


        /**
         * Debug function to print all Breakpoints and watchpoints.
         */
        protected static void PrintAllBpWp()
        {
            Log.WriteLine("PrintAllBpWp");
            var cspect = Main.CSpect;
            for (int addr = 0; addr < 0x10000; addr++)
            {
                var bp = cspect.Debugger(Plugin.eDebugCommand.GetBreakpoint, addr);
                var wpr = cspect.Debugger(Plugin.eDebugCommand.GetReadBreakpoint, addr);
                var wpw = cspect.Debugger(Plugin.eDebugCommand.GetWriteBreakpoint, addr);
                if ((bp | wpr | wpw) != 0)
                {
                    Log.Write("  Address 0x{0:X4}:", addr);
                    if (bp != 0)
                        Log.Write(" [Breakpoint]");
                    if (wpr != 0)
                        Log.Write(" [Watchpoint read]");
                    if (wpw != 0)
                        Log.Write(" [Watchpoint write]");
                    Log.WriteLine();
                }
            }
        }


        /**
         * Start/stop debugger.
         * @param start Starts the CPU if true (currently this is the only operation mode)
         */
        protected static void StartCpu(bool start)
        {

            //if (start)
            //    PrintAllBpWp();


            // The lock is required, otherwise CpuRunning can be set here and the Tick() jumps in
            // between "CpuRunning = true/false" and "eDebugCommand.Run/Enter"
            lock (lockObj)
            {
                // Is required. Otherwise a stop could be missed because the tick is called only
                // every 20ms. If start/stop happens within this timeframe it would not be recognized.
                //CpuRunning = start;

                // Get previous debug state
                var cspect = Main.CSpect;
                var prevDbgState = cspect.Debugger(Plugin.eDebugCommand.GetState);  // 0 = runnning
                bool prevRunning = (prevDbgState == 0);

               // Loop and wait until executed
                if (start != prevRunning)
                {
                    var dbgCommand = (start) ? Plugin.eDebugCommand.Run : Plugin.eDebugCommand.Enter;
                    while (true)
                    {
                        // Run or stop
                        cspect.Debugger(dbgCommand);
                        // Check if done
                        var debugState = cspect.Debugger(Plugin.eDebugCommand.GetState);
                        bool running = (debugState == 0);
                        if (start == running)
                            break;
                        // Else wait a little bit
                        Thread.Sleep(1);    // in ms
                    }
                    // If changed to 'stop' then check the breakpoints
                    if (!start)
                    {
                        CheckIfBreakpointHit();
                    }
                }

                // Start/stop
                CpuRunning = start;
            }
        }


        /**
         * Returns address as string.
         * If it is a long address than together with bank information.
         * E.g. "0x35F6" or 0x35F6 @bank9"
         */
        protected static string GetAddressString(int address)
        {
            int addr = address & 0xFFFF;
            string addrString = string.Format("0x{0:X4}", addr);
            byte bank = (byte)(address >> 16);
            if(bank>0)
                addrString += " @bank" + (bank - 1);
            return addrString;
        }


        /**
         * Checks if registers have changed.
         * Not all registers are tested.
         * Only R and PC.
         */
        protected static bool RegsChanged(Plugin.Z80Regs r1, Plugin.Z80Regs r2)
        {
            return (r1.PC != r2.PC) || (r1.R != r2.R);
        }


        /**
         * Used to execute an action but make sure that the CPU is stopped before.
         * If the CPU was running before it will be restarted.
         * If breakpoints are found on stop a notification will be sent.
         */
        public static void ExecuteStopped(Action action)
        {
            // The lock is required, otherwise CpuRunning can be set here and the Tick() jumps in
            // between "CpuRunning = true/false" and "eDebugCommand.Run/Enter"
            lock (lockObj)
            {
                // Is required. Otherwise a stop could be missed because the tick is called only
                // every 20ms. If start/stop happens within this timeframe it would not be recognized.
                //CpuRunning = start;

                // Get previous debug state
                var cspect = Main.CSpect;
                var prevDbgState = cspect.Debugger(Plugin.eDebugCommand.GetState);  // 0 = runnning
                bool prevRunning = (prevDbgState == 0);

                // Loop and wait until executed
                if (prevRunning)
                {
                    var dbgCommand = Plugin.eDebugCommand.Enter;
                    var prevRegs = cspect.GetRegs();
                    while (true)
                    {
                        // Run or stop
                        cspect.Debugger(dbgCommand);
                        // Wait a little bit
                        Thread.Sleep(1);    // in ms  // DOES NOT WORK: If I wait too long in this function CSpect behaves oddly. E.g. changes memory, restarts Z80, ...
                        // Checks registers
                        var curRegs = cspect.GetRegs();
                        if (!RegsChanged(prevRegs, curRegs)) {
                            // Check if done
                            var debugState = cspect.Debugger(Plugin.eDebugCommand.GetState);
                            bool running = (debugState == 0);
                            if (!running)
                                break;
                        }
                        prevRegs = curRegs;
                    }
                }

                // Execute the action
                action();

                // If previously running get back to the old state
                if (prevRunning)
                {
                    // If changed to 'stop' then check the breakpoints
                    CheckIfBreakpointHit(false);
                    // Restart
                    var dbgCommand = Plugin.eDebugCommand.Run;
                    while (true)
                    {
                        // Run or stop
                        cspect.Debugger(dbgCommand);
                        // Check if done
                        var debugState = cspect.Debugger(Plugin.eDebugCommand.GetState);
                        bool running = (debugState == 0);
                        if (running)
                            break;
                        // Else wait a little bit
                        Thread.Sleep(1);    // in ms
                    }
                }
            }
        }


        //static bool run = false;
        //static Plugin.Z80Regs prevRegs = Main.CSpect.GetRegs();
        //static int prevMem = 0;
        //static int counter = 0;
        //static System.Diagnostics.Stopwatch stopwatch;
        /**
         * Called on every tick.
         * Every 20ms.
         */
        public static void Tick()
        {
            /*
            var cspect = Main.CSpect;
            var curRegs = cspect.GetRegs();
            if(!run)
            {
                // Compare
                if (prevRegs.PC != curRegs.PC || prevRegs.R != curRegs.R )
                {
                    Log.WriteLine("prevPC={0}/0x{0:X4}, prevR={1}, curPC={2}/0x{2:X4}, curR={3}", prevRegs.PC, prevRegs.R, curRegs.PC, curRegs.R);
                    Log.WriteLine("prevSP={0}/0x{0:X4}, curSP={1}/0x{1:X4}", prevRegs.SP, curRegs.SP);
                    byte[] spCont = cspect.Peek((ushort)(curRegs.SP - 2), 2);
                    int mem = spCont[0] + 256 * spCont[1];
                    Log.WriteLine("prevMem={0}/0x{0:X4}, (curSP-2)={1}/0x{1:X4}", prevMem, prevMem, mem, mem);
                    Log.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                }
            }
            prevRegs = curRegs;
            byte[] prevCont = cspect.Peek(curRegs.SP, 2);
            prevMem = prevCont[0] + 256 * prevCont[1];

            run = !run;
            var dbgCommand = (run) ? Plugin.eDebugCommand.Run : Plugin.eDebugCommand.Enter;
            cspect.Debugger(dbgCommand);
            */

            /*
            counter++;
            if (counter == 1)
            {
                stopwatch = new System.Diagnostics.Stopwatch();
                stopwatch.Start();
            }
            if (counter == 1000)
            {
                stopwatch.Stop();              
                Console.WriteLine("RunTime: " + stopwatch.ElapsedMilliseconds);
                counter = 0;
            }
            */

            
            // Return if not initialized
            if (BreakpointMap == null)
                return;

            // Check if debugger state changed
            lock (lockObj)
            {
                var cspect = Main.CSpect;
                var debugState = cspect.Debugger(Plugin.eDebugCommand.GetState);
                bool running = (debugState == 0);
                if (CpuRunning != running)
                {
                    // State changed
                    if (Log.Enabled)
                        Log.WriteLine("Debugger state changed to {0}, 0=running", debugState);
                    CpuRunning = running;
                    if (!running)
                    {
                        CheckIfBreakpointHit();
                    }
                }
            }
        }


        /**
         * Called when the debugger stopped.
         * E.g. because a breakpoint was hit.
         * @param sendNtfIfNoReason If no reason is found a notification (with "manual break") is sent
         * if true.
         * The notification is not sent if false. In that case a notification is only sent if a reason
         * is found.
         */
        protected static void CheckIfBreakpointHit(bool sendNtfIfNoReason=true)
        {
            // Get PC
            var cspect = Main.CSpect;
            var regs = cspect.GetRegs();
            var pc = regs.PC;
            byte slot = (byte)(pc >> 13);
            int bank = cspect.GetNextRegister((byte)(0x50 + slot));
            //Log.WriteLine("Debugger stopped: bank {0}, slot {1}", bank, slot);
            int pcLong = ((bank+1)<<16) + pc;
            if (Log.Enabled)   
                Log.WriteLine("Debugger stopped at 0x{0:X4} (long address=0x{1:X6})", pc, pcLong);

            // Disable temporary breakpoints (64k addresses)
            if (TmpBreakpoint1 >= 0)
            {
                if(!BreakpointMap.ContainsValue((ushort)TmpBreakpoint1))
                   cspect.Debugger(Plugin.eDebugCommand.ClearBreakpoint, TmpBreakpoint1);
            }
            if (TmpBreakpoint2 >= 0)
            {
                if (!BreakpointMap.ContainsValue((ushort)TmpBreakpoint2))
                    cspect.Debugger(Plugin.eDebugCommand.ClearBreakpoint, TmpBreakpoint2);
            }

            // Guess break reason
            BreakReason reason = BreakReason.MANUAL_BREAK;
            string reasonString = "";
            int bpAddress = 0;
            //  Check for temporary breakpoints
            if (pc == TmpBreakpoint1 || pc == TmpBreakpoint2)
            {
                reason = BreakReason.NO_REASON;
                bpAddress = pcLong;
            }
            // Check for breakpoints
            else if (BreakpointMap.ContainsValue(pcLong) || BreakpointMap.ContainsValue(pc))
            {
                // Breakpoint hit
                reason = BreakReason.BREAKPOINT_HIT;
                bpAddress = pcLong;
            }

            // Note: Watchpoint reasons cannot be safely recognized.
            // Use a few heuristics to determine if a watchpoint is hit.
            if (reason == BreakReason.MANUAL_BREAK)
            {
                if (!ManualBreak)
                {
                    // No pause command has been sent.
                    // Check if there is any watchpoint set. If yes
                    // assume it was a watchpoint.
                    // Note: It could also be a user stopping the CSpect by F1.
                    for (int i = 0; i < 0x10000; i++)
                    {
                        if (cspect.Debugger(Plugin.eDebugCommand.GetReadBreakpoint, i) != 0
                            || cspect.Debugger(Plugin.eDebugCommand.GetWriteBreakpoint, i) != 0)
                        {
                            reason = BreakReason.OTHER;
                            reasonString = "Watchpoint hit or manual break.";
                            break;
                        }
                    }
                }
            }

            // Send break notification
            if(sendNtfIfNoReason || reason!=BreakReason.MANUAL_BREAK)
            {
                SendPauseNotification(reason, bpAddress, reasonString);
            }

            // "Undefine" temporary breakpoints
            TmpBreakpoint1 = -1;
            TmpBreakpoint2 = -1;
            // Reset Pause
            ManualBreak = false;
        }


        /**
         * Sets a byte in the Data array.
         */
        protected static void SetByte(int value)
        {
            Data[Index++] = (byte) value;
        }


        /**
         * Sets a dword in the Data array.
         */
        protected static void SetWord(int value)
        {
            Data[Index++] = (byte)(value & 0xFF);
            Data[Index++] = (byte)(value>>8);
        }


        /**
         * Copies a buffer to the Data array.
         */
        protected static void SetBuffer(byte[] buffer)
        {
            foreach (var value in buffer)
                Data[Index++] = value;
        }

        /**
         * Copies a string (including terminating 0) into the Data array.
         */
        protected static void SetString(string text)
        {
            foreach (var value in text)
                Data[Index++] = (byte)value;
        }


        /**
        * Clears all break- and watchpoints.
        */
        protected static void ClearAllBreakAndWatchpoints()
        {
            // Clear all breakpoints etc.
            var cspect = Main.CSpect;
            cspect.Debugger(Plugin.eDebugCommand.ClearAllBreakpoints);
            for (int addr = 0; addr < 0x10000; addr++)
            {
                //cspect.Debugger(Plugin.eDebugCommand.ClearBreakpoint, addr);
                cspect.Debugger(Plugin.eDebugCommand.ClearReadBreakpoint, addr);
                cspect.Debugger(Plugin.eDebugCommand.ClearWriteBreakpoint, addr);
            }
        }


        /**
         * Returns the configuration.
         */
        public static void CmdInit()
        {
            // Clear breakpoints
            ClearAllBreakAndWatchpoints();
            // Return values
            int length = 1 + 3 + Main.ProgramName.Length + 1;
            InitData(length);
            SetByte(0);   // No error
            SetBuffer(DZRP_VERSION);
            SetByte((byte)DzrpMachineType.ZXNEXT);  // machine type = ZXNext
            SetString(Main.ProgramName);
            CSpectSocket.SendResponse(Data);
        }



        /**
         * Just sends a response.
         */
        public static void CmdClose()
        {
            CSpectSocket.SendResponse();
        }


        /**
         * Returns the registers.
         */
        public static void GetRegisters()
        {
            // Get registers
            var cspect = Main.CSpect;
            var regs = cspect.GetRegs();
            InitData(29+8);
            // Return registers
            SetWord(regs.PC);
            SetWord(regs.SP);
            SetWord(regs.AF);
            SetWord(regs.BC);
            SetWord(regs.DE);
            SetWord(regs.HL);
            SetWord(regs.IX);
            SetWord(regs.IY);
            SetWord(regs._AF);
            SetWord(regs._BC);
            SetWord(regs._DE);
            SetWord(regs._HL);
            SetByte(regs.R);
            SetByte(regs.I);
            SetByte(regs.IM);
            SetByte(0);     // reserved
            // Return the slots/banks
            SetByte(8);
            for (int i = 0; i < 8; i++)
            {
                byte bank = cspect.GetNextRegister((byte)(0x50 + i));
                SetByte(bank);
            }
            // Return
            CSpectSocket.SendResponse(Data);
            //Log.WriteLine("GetRegs: I={0}, R={1}", regs.I, regs.R);
        }


        /**
         * Sets one double or single register.
         */
        public static void SetRegister()
        {
            // Get register number
            byte regNumber = CSpectSocket.GetDataByte();
            // Get new value
            ushort value = CSpectSocket.GetDataWord();
            ushort valueByte = (ushort)(value & 0xFF);
            // Get registers
            var cspect = Main.CSpect;
            var regs = cspect.GetRegs();
            // Set a specific register
            switch (regNumber)
            {
                case 0: regs.PC = value; break;
                case 1: regs.SP = value; break;
                case 2: regs.AF = value; break;
                case 3: regs.BC = value; break;
                case 4: regs.DE = value; break;
                case 5: regs.HL = value; break;
                case 6: regs.IX = value; break;
                case 7: regs.IY = value; break;
                case 8: regs._AF = value; break;
                case 9: regs._BC = value; break;
                case 10: regs._DE = value; break;
                case 11: regs._HL = value; break;

                case 13: regs.IM = (byte)value; break;

                case 14: regs.AF = (ushort)((regs.AF & 0xFF00) + valueByte); break;  // F
                case 15: regs.AF = (ushort)((regs.AF & 0xFF) + 256* valueByte); break;  // A
                case 16: regs.BC = (ushort)((regs.BC & 0xFF00) + valueByte); break;  // C
                case 17: regs.BC = (ushort)((regs.BC & 0xFF) + 256 * valueByte); break;  // B
                case 18: regs.DE = (ushort)((regs.DE & 0xFF00) + valueByte); break;  // E
                case 19: regs.DE = (ushort)((regs.DE & 0xFF) + 256 * valueByte); break;  // D
                case 20: regs.HL = (ushort)((regs.HL & 0xFF00) + valueByte); break;  // L
                case 21: regs.HL = (ushort)((regs.HL & 0xFF) + 256 * valueByte); break;  // H
                case 22: regs.IX = (ushort)((regs.IX & 0xFF00) + valueByte); break;  // IXL
                case 23: regs.IX = (ushort)((regs.IX & 0xFF) + 256 * valueByte); break;  // IXH
                case 24: regs.IY = (ushort)((regs.IY & 0xFF00) + valueByte); break;  // IYL
                case 25: regs.IY = (ushort)((regs.IY & 0xFF) + 256 * valueByte); break;  // IYH
                case 26: regs._AF = (ushort)((regs._AF & 0xFF00) + valueByte); break;  // F'
                case 27: regs._AF = (ushort)((regs._AF & 0xFF) + 256 * valueByte); break;  // A'
                case 28: regs._BC = (ushort)((regs._BC & 0xFF00) + valueByte); break;  // C'
                case 29: regs._BC = (ushort)((regs._BC & 0xFF) + 256 * valueByte); break;  // B'
                case 30: regs._DE = (ushort)((regs._DE & 0xFF00) + valueByte); break;  // E'
                case 31: regs._DE = (ushort)((regs._DE & 0xFF) + 256 * valueByte); break;  // D'
                case 32: regs._HL = (ushort)((regs._HL & 0xFF00) + valueByte); break;  // L'
                case 33: regs._HL = (ushort)((regs._HL & 0xFF) + 256 * valueByte); break;  // H'

                case 34: regs.R = (byte)value; break;
                case 35: regs.I = (byte)value; break;

                default:
                    // Error
                    Log.WriteLine("Error: Wrong register number {0} to set.", regNumber);
                    break;
            }

            // Set register(s)
            cspect.SetRegs(regs);
            //Log.WriteLine("SetRegs: I={0}, R={1}", regs.I, regs.R);

            // Respond
            CSpectSocket.SendResponse();
        }


        /**
         * Writes one memory bank.
         */
        public static void WriteBank()
        {
            // Get size
            int bankSize = CSpectSocket.GetRemainingDataCount()-1;
            if(bankSize!=0x2000)
            {
                // Only supported is 0x2000
                string errorString = "Bank size incorrect!";
                int length = 1 + errorString.Length + 1;
                InitData(length);
                SetByte(1);   // Error
                SetString(errorString);
                CSpectSocket.SendResponse(Data);
            }

            // Get bank number
            byte bankNumber = CSpectSocket.GetDataByte();
            // Calculate physical address
            // Example: phys. address $1f021 = ($1f021&$1fff) for offset and ($1f021>>13) for bank.
            Int32 physAddress = bankNumber * bankSize;
            // Write memory
            var cspect = Main.CSpect;
            if (Log.Enabled)
                Log.WriteLine("WriteBank: bank={0}", bankNumber);
            for (int i = bankSize; i > 0; i--)
            {
                byte value = CSpectSocket.GetDataByte();
                cspect.PokePhysical(physAddress++, new byte[] {value});
            }

            // Respond
            InitData(2);
            SetByte(0);   // No error
            SetByte(0);
            CSpectSocket.SendResponse(Data);
        }



        /**
         * Sets a breakpoint and returns an ID (!=0).
         * The address is the physical address.
         */
        protected static ushort SetBreakpoint(int address)
        {
            if (Log.Enabled)
                Log.WriteLine("  SetBreakpoint: 0x{0:X6}", address);
            // Set in CSpect
            byte bank = (byte)(address >> 16);
            if(bank>0)
            {
                // Adjust physical address
                int physAddress = (address & 0x1FFF) + ((bank - 1) << 13);
                Main.CSpect.Debugger(Plugin.eDebugCommand.SetPhysicalBreakpoint, physAddress);
                if(Log.Enabled)
                    Log.WriteLine("  Phys. breakpoint 0x{0:X6}", physAddress);
            }
            else
            {
                // Use 64k address
                Main.CSpect.Debugger(Plugin.eDebugCommand.SetBreakpoint, address);
                if (Log.Enabled)
                    Log.WriteLine("  Normal breakpoint 0x{0:X4}", address);
            }
            // Add to array (ID = element position + 1)
            BreakpointMap.Add(++LastBreakpointId, address);
            return LastBreakpointId;
        }


        /**
         * Removes a breakpoint.
         */
        protected static void DeleteBreakpoint(ushort bpId)
        {
            // Remove
            int address;
            if (BreakpointMap.TryGetValue(bpId, out address))
            {
                BreakpointMap.Remove(bpId);
                // Clear in CSpect (only if last breakpoint with that address)
                if (!BreakpointMap.ContainsValue(address))
                {
                    byte bank = (byte)(address >> 16);
                    if (bank > 0)
                    {
                        // Adjust physical address
                        address = (address & 0x1FFF) + ((bank - 1) << 13);
                        Main.CSpect.Debugger(Plugin.eDebugCommand.ClearPhysicalBreakpoint, address);
                    }
                    else
                    {
                        // Use 64k address
                        Main.CSpect.Debugger(Plugin.eDebugCommand.ClearBreakpoint, address);

                    }
                }
            }
        }


        /**
         * Continues execution.
         */
        public static void Continue()
        {
            var cspect = Main.CSpect;
            // Breakpoint 1
            bool bp1Enable = (CSpectSocket.GetDataByte() != 0);
            ushort bp1Address = CSpectSocket.GetDataWord();
            // Breakpoint 2
            bool bp2Enable = (CSpectSocket.GetDataByte() != 0);
            ushort bp2Address = CSpectSocket.GetDataWord();

            // Alternate command?
            //AlternateCommand alternateCmd = (AlternateCommand)CSpectSocket.GetDataByte();
            AlternateCommand alternateCmd = AlternateCommand.CONTINUE;
            switch (alternateCmd)
            {
                /* Note: Cspect cannot support this because of the strange step-over behavior:
                 * CSpect step-over does not step-over a conditional jump backwards, e.g. "JP cc, -5"
                case AlternateCommand.STEP_OVER: // Step over
                    ushort address = CSpectSocket.GetDataWord();
                    ushort endAddress = CSpectSocket.GetDataWord();
                    // Respond
                    CSpectSocket.SendResponse();
                    //ManualBreak = false;
                    //CpuRunning = true; Need to be locked
                    //cspect.Debugger(Plugin.eDebugCommand.StepOver);
                    break;
                    
                case AlternateCommand.STEP_OUT: // Step out
                    // Respond
                    CSpectSocket.SendResponse();
                    break;
                    */

                case AlternateCommand.CONTINUE: // Continue
                default:
                    // Set temporary breakpoints
                    TmpBreakpoint1 = -1;
                    if (bp1Enable)
                    {
                        TmpBreakpoint1 = bp1Address;
                        var result = cspect.Debugger(Plugin.eDebugCommand.SetBreakpoint, TmpBreakpoint1);
                        if (Log.Enabled)
                            Log.WriteLine("  Set tmp breakpoint 1 at 0x{0:X4}, result={1}", TmpBreakpoint1, result);
                    }
                    TmpBreakpoint2 = -1;
                    if (bp2Enable)
                    {
                        TmpBreakpoint2 = bp2Address;
                        var result = cspect.Debugger(Plugin.eDebugCommand.SetBreakpoint, TmpBreakpoint2);
                        if (Log.Enabled)
                            Log.WriteLine("  Set tmp breakpoint 2 at 0x{0:X4}, result={1}", TmpBreakpoint2, result);
                    }

                    // Log
                    if (Log.Enabled)
                    {
                        var regs = cspect.GetRegs();
                        Log.WriteLine("Continue: Run debugger. pc=0x{0:X4}/{0}, bp1=0x{1:X4}/{1}, bp2=0x{2:X4}/{2}", regs.PC, TmpBreakpoint1, TmpBreakpoint2);
                    }

                    // Respond
                    CSpectSocket.SendResponse();

                    // Run
                    ManualBreak = false;
                    StartCpu(true);
                    break;
            }
        }


        /**
         * Does a Step-Over.
         * Step-over in a loop until PC is out of the given range.
         * The idea is to step over e.g. a macro which consists of several instructions.
         */
        public static void StepOver(ushort address, ushort endAddress)
        {
            var cspect = Main.CSpect;
            // Step over
            while (true)
            {
                cspect.Debugger(Plugin.eDebugCommand.StepOver);
                var regs = cspect.GetRegs();
                if (regs.PC < address || regs.PC >= endAddress)
                    break;
            }
        }


        /**
         * Pauses execution.
         */
        public static void Pause()
        {
            // Pause
            if (Log.Enabled)
                Log.WriteLine("Pause: Stop debugger.");
            ManualBreak = true;
            Main.CSpect.Debugger(Plugin.eDebugCommand.Enter);
            // Respond
            CSpectSocket.SendResponse();
        }


        /**
         * Adds a breakpoint.
         */
        public static void AddBreakpoint()
        {
            // Get breakpoint address
            int bpAddr = CSpectSocket.GetLongAddress();

            // Set CSpect breakpoint
            ushort bpId = SetBreakpoint(bpAddr);

            // Respond
            InitData(2);
            SetWord(bpId);
            CSpectSocket.SendResponse(Data);
        }


        /**
         * Removes a breakpoint.
         */
        public static void RemoveBreakpoint()
        {
            // Get breakpoint ID
            ushort bpId = CSpectSocket.GetDataWord();
            // Remove breakpoint
            DeleteBreakpoint(bpId);
            // Respond
            CSpectSocket.SendResponse();

        }


        /**
         * Adds a watchpoint area.
         */
        public static void AddWatchpoint()
        {
            // Get data
            ushort start = CSpectSocket.GetDataWord();
            ushort size = CSpectSocket.GetDataWord();
            ushort end = (ushort)(start + size);
            byte access = CSpectSocket.GetDataByte();
            if (Log.Enabled)
                Log.WriteLine("AddWatchpoint: address={0:X4}, size={1}", start, size);
            // condition is not used
            var cspect = Main.CSpect;
            // Read
            if ((access & 0x01) != 0)
            {
                for (ushort i = start; i != end; i++)
                {
                    cspect.Debugger(Plugin.eDebugCommand.SetReadBreakpoint, i);
                    //Log.WriteLine("Read Watchpoint {0}", i);
                }
            }
            // Write
            if ((access & 0x02) != 0)
            {
                for (ushort i = start; i != end; i++)
                {
                    cspect.Debugger(Plugin.eDebugCommand.SetWriteBreakpoint, i);
                    //Log.WriteLine("Write Watchpoint {0}", i);
                }
            }
            // Respond
            CSpectSocket.SendResponse();
        }


        /**
         * Removes a watchpoint area.
         */
        public static void RemoveWatchpoint()
        {
            // Get data
            ushort start = CSpectSocket.GetDataWord();
            ushort size = CSpectSocket.GetDataWord();
            ushort end = (ushort)(start + size);
            var cspect = Main.CSpect;
            // Remove both read and write
            for (ushort i = start; i != end; i++)
            {
                cspect.Debugger(Plugin.eDebugCommand.ClearReadBreakpoint, i);
                cspect.Debugger(Plugin.eDebugCommand.ClearWriteBreakpoint, i);
            }
            // Respond
            CSpectSocket.SendResponse();
        }


        /**
         * Reads a memory area.
         */
        public static void ReadMem()
        {
            // Skip reserved
            CSpectSocket.GetDataByte();
            // Start of memory
            ushort address = CSpectSocket.GetDataWord();
            // Get size
            ushort size = CSpectSocket.GetDataWord();
            if (Log.Enabled)
                Log.WriteLine("ReadMem at address={0}, size={1}", address, size);

            // Respond
            InitData(size);
            var cspect = Main.CSpect;
            byte[] values = cspect.Peek(address, size);
            foreach(byte value in values)
                SetByte(value);
            CSpectSocket.SendResponse(Data);
        }


        /**
         * Writes a memory area.
         */
        public static void WriteMem()
        {
            // Skip reserved
            CSpectSocket.GetDataByte();
            // Start of memory
            ushort address = CSpectSocket.GetDataWord();
            // Get size
            var data = CSpectSocket.GetRemainingData();
            ushort size = (ushort)data.Count;

            // Write memory
            var cspect = Main.CSpect;
            byte[] values = data.ToArray();
            cspect.Poke(address, values);
            
            // Respond
            CSpectSocket.SendResponse();
        }


        /**
         * Associate a slot with a bank.
         */
        public static void SetSlot()
        {
            // Get slot
            byte slot = CSpectSocket.GetDataByte();
            // Get bank
            byte bank = CSpectSocket.GetDataByte();
            // Special handling for 0xFE
            if (bank == 0xFE)
                bank = 0xFF;

            // Set slot
            var cspect = Main.CSpect;
            cspect.SetNextRegister((byte)(0x50 + slot), bank);
            //Console.WriteLine("slot {0} = {1}", slot, bank);

            // No error
            InitData(1);
            SetByte(0);
            // Respond
            CSpectSocket.SendResponse(Data);
        }


        /**
         * Returns the state.
         */
        public static void ReadState()
        {
            // Not implemented: No CSpect interface yet.

            // Respond
            CSpectSocket.SendResponse();
        }

        /**
         * Writes the state.
         */
        public static void WriteState()
        {
            // Not implemented: No CSpect interface yet.

            // Respond
            CSpectSocket.SendResponse();
        }


        /**
         * Returns the value of one TBBlue register.
         */
        public static void GetTbblueReg()
        {
            // Get register
            byte reg = CSpectSocket.GetDataByte();
            // Get register value
            var cspect = Main.CSpect;
            cspect.GetNextRegister(reg);
            // Write register value
            InitData(1);
            byte value = cspect.GetNextRegister(reg);
            SetByte(value);
            // Log
            if (Log.Enabled)
                Log.WriteLine("GetNextRegister({0:X2}): {1}", reg, value);
            // Respond
            CSpectSocket.SendResponse(Data);
        }


        /**
         * Returns the first or second sprites palette.
         */
        public static void GetSpritesPalette()
        {
            // Which palette
            int paletteIndex = CSpectSocket.GetDataByte() & 0x01;;

            // Prepare data
            InitData(2 * 256);

            // Store current values
            var cspect = Main.CSpect;
            byte eUlaCtrlReg = cspect.GetNextRegister(0x43);
            byte indexReg = cspect.GetNextRegister(0x40);
            byte colorReg = cspect.GetNextRegister(0x41);
            // Bit 7: 0=first (8bit color), 1=second (9th bit color)
            byte machineReg = cspect.GetNextRegister(0x03);
            // Select sprites
            byte selSprites = (byte)((eUlaCtrlReg & 0x0F) | 32 | (paletteIndex << 6));
            cspect.SetNextRegister(0x43, selSprites); // Resets also 0x44
             // Read palette
            for (int i = 0; i < 256; i++)
            {
                // Set index
                cspect.SetNextRegister(0x40, (byte)i);
                // Read color
                byte colorMain = cspect.GetNextRegister(0x41);
                SetByte(colorMain);
                byte color9th = cspect.GetNextRegister(0x44);
                SetByte(color9th);
                //Log.WriteLine("Palette index={0}: 8bit={1}, 9th bit={2}", i, colorMain, color9th);
            }
            // Restore values
            cspect.SetNextRegister(0x43, eUlaCtrlReg);
            cspect.SetNextRegister(0x40, indexReg);
            if ((machineReg & 0x80) != 0)
            {
                // Bit 7 set, increase 0x44 index.
                // Write it to increase the index
                cspect.SetNextRegister(0x44, colorReg);
            }

            // Respond
            CSpectSocket.SendResponse(Data);
        }


        /**
         * Returns the attributes of some sprites.
         */
        public static void GetSprites()
        {
            // Get index
            int index = CSpectSocket.GetDataByte();
            // Get count
            int count = CSpectSocket.GetDataByte();
            // Get sprite data
            InitData(5*count);
            var cspect = Main.CSpect;
            for (int i = 0; i < count; i++)
            {
                var sprite = cspect.GetSprite(index + i);
                SetByte(sprite.x);
                SetByte(sprite.y);
                SetByte(sprite.paloff_mirror_flip_rotate_xmsb);
                SetByte(sprite.visible_name);
                SetByte(sprite.H_N6_0_XX_YY_Y8);
            }
            // Respond
            CSpectSocket.SendResponse(Data);
        }


        /**
         * Returns some sprite patterns.
         */
        public static void GetSpritePatterns()
        {
            var cspect = Main.CSpect;
            var prevRegs = cspect.GetRegs();

            // Start of memory
            ushort index = CSpectSocket.GetDataByte();
            // Get size
            ushort count = CSpectSocket.GetDataByte();
            if (Log.Enabled)
                Log.WriteLine("Sprite pattern index={0}, count={1}", index, count);

            // Respond
            int address = index * 256;
            int size = count * 256;
            InitData(size);
            byte[] values = cspect.PeekSprite(address, size);
            foreach (byte value in values)
                SetByte(value);
            CSpectSocket.SendResponse(Data);

            var afterRegs = cspect.GetRegs();
            //Log.WriteLine("prevPC={0}/(0x{0:X4}), afterPC={1}/(0x{1:X4})", prevRegs.PC, afterRegs.PC);
            if (prevRegs.PC != afterRegs.PC)
            {
                Log.WriteLine("prevPC={0}/(0x{0:X4}), afterPC={1}/(0x{1:X4})", prevRegs.PC, afterRegs.PC);
            }
        }


        /**
         * Returns the sprite clipping window.
         */
        public static void GetSpritesClipWindow()
        {
            // Get index (for restoration)
            var cspect = Main.CSpect;
            int prevIndex = cspect.GetNextRegister(0x1C);
            prevIndex = (prevIndex >> 2) & 0x03;
            // Get clip window
            byte[] clip = new byte[5];
            for (int i = 0; i < 4; i++)
            {
                byte value = cspect.GetNextRegister(0x19); // xl
                clip[(prevIndex + i) % 4] = value;
                cspect.SetNextRegister(0x19, value);  // Increment index
            }
            clip[4] = cspect.GetNextRegister(0x15); // sprite control register
            if (Log.Enabled)
                Log.WriteLine("Clip: xl={0}, xr={1}, yt={2}, yb={3}, control={4:X2}", clip[0], clip[1], clip[2], clip[3], clip[4]);
            // Respond
            CSpectSocket.SendResponse(clip);
        }



        /**
         * Sets the border color.
         */
        public static void SetBorder()
        {
            // Get border color
            byte color = CSpectSocket.GetDataByte();
            if (Log.Enabled)
                Log.WriteLine("Bordercolor={0}", color);
            // Set border
            Main.CSpect.OutPort(0xFE, color);
            // Respond
            CSpectSocket.SendResponse();
        }


        /**
         * Sends the pause notification.
         * bpAddress is a long address.
         */
        protected static void SendPauseNotification(BreakReason reason, int bpAddress, string reasonString)
        {
            //Log.WriteLine("SendPauseNotification: reason={0}, bpAddress=0x{1:X6}, reasonString='{2}'", reason, bpAddress, reasonString);
            // Convert string to byte array
            System.Text.ASCIIEncoding enc = new System.Text.ASCIIEncoding();
            byte[] reasonBytes = enc.GetBytes(reasonString+"\0");
            int stringLen = reasonBytes.Length;

            // Prepare data
            int length = 6+stringLen;
            byte[] dataWoString =
            {
                // Length
                (byte)(length & 0xFF),
                (byte)((length >> 8) & 0xFF),
                (byte)((length >> 16) & 0xFF),
                (byte)(length >> 24),
                // SeqNo = 0
                0,
                // PAUSE
                (byte)DZRP_NTF.NTF_PAUSE,
                // Reason
                (byte)reason,
                // Breakpoint address (long address)
                (byte)(bpAddress & 0xFF),
                (byte)((bpAddress >> 8) & 0xFF),
                (byte)((bpAddress >> 16) & 0xFF),
            };
            int firstLen = dataWoString.Length;
            byte[] data = new byte[firstLen + stringLen];
            dataWoString.CopyTo(data, 0);
            reasonBytes.CopyTo(data, firstLen);

            // Respond
            CSpectSocket.Send(data);
        }
    }
}
