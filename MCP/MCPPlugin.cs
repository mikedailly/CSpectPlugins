// ********************************************************************************************************************************************
//      CSpect MCP server plugin - lets AI assistants (Claude Code etc.) drive CSpect through the Model Context Protocol
//      Written by:
//                  Mike Dailly
//
//      Released under the GNU 3 license - please see license file for more details
//
//      Listens on http://127.0.0.1:8765/mcp (set CSPECT_MCP_PORT to change the port), speaking MCP's Streamable
//      HTTP transport. Claude Code:  claude mcp add --transport http cspect http://127.0.0.1:8765/mcp
//
//      Threading: requests arrive on socket threads, but CSpect's CPU/memory must only be touched on the emulator
//      thread. So every request is queued as a job and run in Tick() - which CSpect calls every frame, running or
//      stopped in the debugger - and the socket thread waits for the answer.
// ********************************************************************************************************************************************
using Plugin;
using System;
using System.Collections.Generic;
using System.Threading;

namespace MCP
{
    // *********************************************************************************************************
    /// <summary>
    ///     The plugin
    /// </summary>
    // *********************************************************************************************************
    public class MCPPlugin : iPlugin
    {
        public const string Version = "1.0.0";
        public const int DEFAULT_PORT = 8765;

        internal iCSpect CSpect;
        HttpServer Server;
        McpServer Mcp;

        // ---- jobs for the emulator thread ----
        class Job
        {
            public Func<object> Work;
            public object Result;
            public Exception Error;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public bool Abandoned;
        }
        readonly object Gate = new object();
        readonly Queue<Job> Jobs = new Queue<Job>();
        /// <summary>Set by Tick, so a request can tell when CSpect has stopped ticking us (closing down)</summary>
        volatile bool Quitting;

        // ---- stop tracking (emulator thread writes, any thread waits) ----
        readonly object StopLock = new object();
        int StopCount;
        bool WasStopped = true;
        volatile bool StoppedNow = true;
        /// <summary>"Run to" breakpoints to remove when we next stop: address, physical?</summary>
        readonly List<KeyValuePair<int, bool>> TempBreakpoints = new List<KeyValuePair<int, bool>>();

        // ---- key player (emulator thread) ----
        internal class KeyStep { public int[] Rows; public int Frames; }
        readonly Queue<KeyStep> KeySteps = new Queue<KeyStep>();
        KeyStep CurrentKey;
        int KeyFramesLeft;
        bool KeysHeld;
        /// <summary>Bumped when the key queue empties</summary>
        int KeysDoneCount;
        readonly object KeyLock = new object();
        internal bool KeysSupported = true;

        /// <summary>Physical breakpoints set through MCP (listing them all means scanning 4MB)</summary>
        internal readonly HashSet<int> PhysicalBreakpointsSet = new HashSet<int>();

        // *********************************************************************************************************
        /// <summary>
        ///     Init the plugin: start the server
        /// </summary>
        // *********************************************************************************************************
        public List<sIO> Init(iCSpect _CSpect)
        {
            CSpect = _CSpect;
            int port = DEFAULT_PORT;
            string env = Environment.GetEnvironmentVariable("CSPECT_MCP_PORT");
            if (!string.IsNullOrEmpty(env))
            {
                if (env.Trim() == "0" || env.Trim().ToLowerInvariant() == "off")
                {
                    Console.WriteLine(" MCP server disabled (CSPECT_MCP_PORT=" + env + ")");
                    return new List<sIO>();
                }
                int.TryParse(env, out port);
                if (port <= 0 || port > 65535) port = DEFAULT_PORT;
            }

            // can this CSpect hold keys for us? (releasing them all is harmless)
            ReleaseKeys();

            Mcp = new McpServer(this);
            Server = new HttpServer(Mcp.Handle);
            try
            {
                Server.Start(port);
                Console.WriteLine(" MCP server on http://127.0.0.1:" + port + "/mcp");
            }
            catch (Exception ex)
            {
                Console.WriteLine(" MCP server couldn't listen on port " + port + ": " + ex.Message);
                Server = null;
            }
            return new List<sIO>();
        }

        public bool KeyPressed(int _id) { return false; }
        public byte Read(eAccess _type, int _address, int _id, out bool _isvalid) { _isvalid = false; return 0; }
        public bool Write(eAccess _type, int _port, int _id, byte _value) { return false; }
        public void OSTick() { }

        public void Reset()
        {
            // the machine was reset: let go of any keys we're holding
            lock (KeyLock) { KeySteps.Clear(); CurrentKey = null; }
        }

        public void Quit()
        {
            Quitting = true;
            if (Server != null) { Server.Stop(); Server = null; }
            lock (Gate)
            {
                foreach (Job j in Jobs) { j.Error = new InvalidOperationException("CSpect is closing down"); j.Done.Set(); }
                Jobs.Clear();
            }
            ReleaseKeys();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Run something on the emulator thread and wait for the answer (any thread)
        /// </summary>
        // ******************************************************************************************
        internal T Emu<T>(Func<T> _work, int _timeoutMs = 5000)
        {
            if (Quitting) throw new InvalidOperationException("CSpect is closing down");
            Job j = new Job { Work = () => _work() };
            lock (Gate) Jobs.Enqueue(j);
            if (!j.Done.Wait(_timeoutMs))
            {
                lock (Gate)
                {
                    if (!j.Done.IsSet)
                    {
                        j.Abandoned = true;
                        throw new TimeoutException("CSpect didn't answer in time - is the emulator paused, or busy loading?");
                    }
                }
            }
            if (j.Error != null) throw j.Error;
            return (T)j.Result;
        }

        internal void Emu(Action _work, int _timeoutMs = 5000) { Emu<object>(() => { _work(); return null; }, _timeoutMs); }

        // ******************************************************************************************
        /// <summary>
        ///     Emulator frame (running, or stopped in the debugger)
        /// </summary>
        // ******************************************************************************************
        public void Tick()
        {
            Job[] todo = null;
            lock (Gate)
            {
                if (Jobs.Count > 0) { todo = Jobs.ToArray(); Jobs.Clear(); }
            }
            if (todo != null)
            {
                foreach (Job j in todo)
                {
                    lock (Gate) { if (j.Abandoned) continue; }
                    try { j.Result = j.Work(); }
                    catch (Exception ex) { j.Error = ex; }
                    lock (Gate) j.Done.Set();
                }
            }

            bool stopped = CSpect.Debugger(eDebugCommand.GetState) != 0;
            StoppedNow = stopped;
            if (stopped && !WasStopped)
            {
                // just stopped: remove "run to" breakpoints
                foreach (KeyValuePair<int, bool> t in TempBreakpoints)
                {
                    if (t.Value) { CSpect.Debugger(eDebugCommand.ClearPhysicalBreakpoint, t.Key); PhysicalBreakpointsSet.Remove(t.Key); }
                    else CSpect.Debugger(eDebugCommand.ClearBreakpoint, t.Key);
                }
                TempBreakpoints.Clear();
                lock (StopLock) { StopCount++; Monitor.PulseAll(StopLock); }
            }
            WasStopped = stopped;

            if (!stopped) PlayKeys();
        }

        // ******************************************************************************************
        //  Stopping (emulator thread unless noted)
        // ******************************************************************************************
        /// <summary>How many times the CPU has stopped (emulator thread)</summary>
        internal int StopCounter { get { lock (StopLock) return StopCount; } }

        internal bool IsStoppedNow { get { return StoppedNow; } }

        /// <summary>Wait until the CPU stops after _since (any thread except the emulator's)</summary>
        internal bool WaitForStop(int _since, int _timeoutMs)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(_timeoutMs);
            lock (StopLock)
            {
                while (StopCount == _since)
                {
                    int left = (int)(end - DateTime.UtcNow).TotalMilliseconds;
                    if (left <= 0) return false;
                    Monitor.Wait(StopLock, left);
                }
                return true;
            }
        }

        /// <summary>A breakpoint to remove when the CPU next stops (emulator thread)</summary>
        internal void AddTempBreakpoint(int _address, bool _physical)
        {
            TempBreakpoints.Add(new KeyValuePair<int, bool>(_address, _physical));
        }

        // ******************************************************************************************
        //  Keys
        // ******************************************************************************************
        /// <summary>Queue key presses (any thread). Returns the "done" count to wait for.</summary>
        internal int QueueKeys(List<KeyStep> _steps)
        {
            lock (KeyLock)
            {
                foreach (KeyStep s in _steps) KeySteps.Enqueue(s);
                return KeysDoneCount;
            }
        }

        /// <summary>Wait until the key queue has played out (any thread)</summary>
        internal bool WaitForKeys(int _since, int _timeoutMs)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(_timeoutMs);
            lock (KeyLock)
            {
                while (KeysDoneCount == _since)
                {
                    int left = (int)(end - DateTime.UtcNow).TotalMilliseconds;
                    if (left <= 0) return false;
                    Monitor.Wait(KeyLock, left);
                }
                return true;
            }
        }

        internal void CancelKeys()
        {
            lock (KeyLock) { KeySteps.Clear(); CurrentKey = null; KeysDoneCount++; Monitor.PulseAll(KeyLock); }
        }

        void PlayKeys()
        {
            lock (KeyLock)
            {
                if (CurrentKey == null || KeyFramesLeft <= 0)
                {
                    if (KeySteps.Count == 0)
                    {
                        if (CurrentKey != null || KeysHeld)
                        {
                            CurrentKey = null;
                            ReleaseKeys();
                            KeysDoneCount++;
                            Monitor.PulseAll(KeyLock);
                        }
                        return;
                    }
                    CurrentKey = KeySteps.Dequeue();
                    KeyFramesLeft = Math.Max(1, CurrentKey.Frames);
                    SetRows(CurrentKey.Rows);
                }
                KeyFramesLeft--;
            }
        }

        void SetRows(int[] _rows)
        {
            if (!KeysSupported) return;
            try
            {
                SetRowsNow(_rows);
            }
            catch (MissingMethodException)
            {
                // an older CSpect without iCSpect.SetKeyboardRow
                KeysSupported = false;
            }
        }

        /// <summary>(Its own method: calling a missing interface method throws when this is JIT compiled)</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        void SetRowsNow(int[] _rows)
        {
            bool any = false;
            for (int r = 0; r < 8; r++)
            {
                CSpect.SetKeyboardRow(r, _rows[r]);
                if ((_rows[r] & 0x1f) != 0x1f) any = true;
            }
            KeysHeld = any;
        }

        void ReleaseKeys()
        {
            int[] none = new int[8];
            for (int r = 0; r < 8; r++) none[r] = 0xff;
            if (CSpect != null) SetRows(none);
            KeysHeld = false;
        }
    }
}
