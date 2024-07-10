﻿// ********************************************************************************************************************************************
//      CSpect esxDOS extension, allowing access to the RST $08 function in the CSpect emulator
//      Written by:
//                  Mike Dailly
//      contributions by:
//                  Mike Flash Ware
//      Released under the GNU 3 license - please see license file for more details
//
//      This extension uses the EXE extension method and traps trying to execute an instruction at RST $08,
//      and the Read/Write on IO ports for file streaming
//
// ********************************************************************************************************************************************
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plugin;

namespace esxDOS
{

    // **********************************************************************
    /// <summary>
    ///     A simple, empty i2C device
    /// </summary>
    // **********************************************************************
    public class CRST8 : iPlugin
    {
        #region Plugin interface
        /// <summary>Special ID passed in to pass a pre-opened filehandle and return a NEXT file handle</summary>
        public const int ESXDOS_SETFILEHANDLE = unchecked((int)0xdeadc0de);

        public const int PC_Address = 0x08;
        public const int SDCard_Data_Port = 0xEb;
        public const int SDCard_Control_Port = 0xE7;

        public bool m_Internal = false;
        public byte port_e7 = 0xfe;

        public const int IM0 = 0;
        public const int IM1 = 1;
        public const int IM2 = 2;

        IFileIO FileSystem;
        bool Initialised = false;

        /// <summary>Z80 CPU flag bits</summary>
        [Flags]
        public enum eCPUFlags : UInt16
        {
            None = 0x00,
            C = 0x01,
            N = 0x02,
            PV = 0x04,
            b3 = 0x08,
            H = 0x10,
            b5 = 0x20,
            Z = 0x40,
            S = 0x80
        };

        /// <summary>SD card port writing</summary>
        enum eState
        {
            idle,
            waiting_for_cmd,
            ProcessingCMD
        }

        enum eFileAccessMode
        {
            /// <summary>F_READDIR returns short 8.3 names</summary>
            esx_mode_short_only = 0x00,
            /// <summary>F_READDIR returns LFNs only</summary>
            esx_mode_lfn_only = 0x10,
            /// <summary>F_READDIR returns LFN followed by 8.3 name(both null-terminated)</summary>
            esx_mode_lfn_and_short = 0x18,
            /// <summary>F_READDIR only returns entries matching wildcard</summary>
            esx_mode_use_wildcards = 0x20,
            /// <summary>F_READDIR additionally returns +3DOS header</summary>
            esx_mode_use_header = 0x40,
            /// <summary>enable sort/filter mode in C</summary>
            esx_mode_sf_enable = 0x80,
            /// <summary>F_READDIR doesn't return files</summary>
            esx_sf_exclude_files = 0x80,
            /// <summary>F_READDIR doesn't return directories</summary>
            esx_sf_exclude_dirs = 0x40,
            /// <summary>F_READDIR doesn't return . or .. directories</summary>
            esx_sf_exclude_dots = 0x20,
            /// <summary>F_READDIR doesn’t return system/hidden files</summary>
            esx_sf_exclude_sys = 0x10,
            /// <summary>entries will be sorted (unless memory exhausted/BREAK pressed)</summary>
            esx_sf_sort_enable = 0x08,
            /// <summary>sort by LFN</summary>
            esx_sf_sort_lfn = 0x00,
            /// <summary>sort by short name</summary>
            esx_sf_sort_short = 0x01,
            /// <summary>sort by date/time(LFN breaks ties)</summary>
            esx_sf_sort_date = 0x02,
            /// <summary>ort by file size(LFN breaks ties)</summary>
            esx_sf_sort_size = 0x03,
            /// <summary>sort order is reversed</summary>
            esx_sf_sort_reverse = 0x04
        };

        //****************************************************************************
        /// <summary>File info</summary>
        //****************************************************************************
        public struct sFileInfo
        {
            public byte Attrib;
            public UInt16 timestamp;
            public UInt16 datestamp;
            public UInt32 FileSize;
        }
        //****************************************************************************
        /// <summary>Directory structure for reading dirs</summary>
        //****************************************************************************
        public struct DirEntry
        {
            public string FileName;
            public string FileName_Short;
            public sFileInfo attrib;
            public DateTime Datestamp;
            public UInt32 FileSize;
        }
        //****************************************************************************
        /// <summary>Directory structure for reading dirs</summary>
        //****************************************************************************
        public struct FullDirectory
        {
            public List<DirEntry> Files;
            public int CurrentIndex;
            public int Handle;
        }

        //****************************************************************************
        /// <summary>MS DOS attribute bits</summary>
        //****************************************************************************
        enum eMSDOS_ATTRIB
        {
            ReadOnly = 1,
            Hidden = 2,
            System = 4,
            Directory = 16,
            Archive = 32
        };



        /// <summary>RST $08 disk commands</summary>
        enum RST08
        {
            FMODE_READ = 0x01,
            FMODE_WRITE = 0x02,
            FMODE_OPEN = 0x00,
            FMODE_OPEN_CREATE = 0x08,		// open if exists, else create
            FMODE_CREATE = 0x04,			// create if doesn't exist, else error
            FMODE_TRUNCATE = 0x0c,			// create and/or truncate

            /// <summary>Obtain a map of card addresses describing the space occupied by the file</summary>
            DISK_FILEMAP = 0x85,            // streaming get map start
            /// <summary>Start reading from the card in streaming mode.</summary>
            DISK_STRMSTART = 0x86,          // streaming start
            /// <summary>Stop current streaming operation.</summary>
            DISK_STRMEND = 0x87,            // streaming end

            /// <summary>Get API version/mode information.</summary>
            M_DOSVERSION = 0X88,
            M_GETSETDRV = 0x89,
            /// <summary> Get the current date/time.</summary>
            M_GETDATE = 0x8E,
            /// <summary>Execute a DOT command - not implemented</summary>
            M_EXECCMD= 0x8F,
            /// <summary>Set general capabilities - not implemented</summary>
            M_SETCAPS = 0x91,
            /// <summary>Access API for installable drivers - not implemented</summary>
            M_DRVAPI = 0x92,
            /// <summary>Get error - not implemented</summary>
            M_GETERR = 0x93,
            /// <summary>Open a file.</summary>
            F_OPEN = 0x9A,
            /// <summary>Close a file or directory.</summary>
            F_CLOSE = 0x9B,
            /// <summary>Sync file changes to disk.</summary>
            F_SYNC = 0x9C,
            /// <summary>Read bytes from file.</summary>
            F_READ = 0x9D,
            /// <summary>Write bytes to file.</summary>
            F_WRITE = 0x9E,
            /// <summary>Seek to position in file</summary>
            F_SEEK = 0x9F,

            /// <summary>Get file position - not implemented</summary>
            F_FGETPOS = 0xA0,
            /// <summary>Get file information/status</summary>
            F_FSTAT = 0xA1,
            /// <summary>Truncate a file - not implemented</summary>
            F_FTRUNCATE = 0xA2,

            F_OPENDIR = 0xA3,
            F_READ_DIR = 0xA4,
            F_TELLDIR = 0xA5,
            F_SEEKDIR = 0xA6,
            F_REWINDDIR = 0xA7,
            F_GETCWD = 0xA8,
            F_CHDIR = 0xA9,
            F_MKDIR = 0xAA,
            /// <summary>Remove directory - not implemented</summary>
            F_RMDIR = 0xAB,
            F_STAT = 0xAC,
            /// <summary>Delete file - not implemented</summary>
            F_UNLINK = 0xAD,
            F_RENAME = 0xB0,

            /// <summary>Extended flush all closed/cached files to disk (if dirty)</summary>
            F_FLUSH   = 0xE0
        };

        enum eCMD18
        {
            None,
            Waiting,
            Reading
        };

        /// <summary>The current registers at RST $08 call time - updated upon return</summary>
        Z80Regs regs;
        /// <summary>The current drive</summary>
        int CurrentDrive = 0;
        /// <summary>Simple buffer for loading in data before poking into memory - could go direct in many cases</summary>
        public byte[] filebuffer = new byte[65536];                    // buffer for reading in file
        /// <summary>The current MMC card path</summary>
        public string MMCPath = Environment.CurrentDirectory + "\\";
        /// <summary>Current Directory, relative to the MMC path</summary>
        public string CurrentDirectory = "";
        /// <summary>Last folder access for caching of upper/lower casing of filenames</summary>
        public string LastFolder = "";
        /// <summary>Cached directory list of filenames</summary>
        public string[] FindFiles = null;


        /// <summary>Open directories</summary>
        FullDirectory OpenedDirectory;
        
        // Streaming variables... this needs way more usage/testing

        /// <summary>Are we in streaming mode?</summary>
        public bool StreamEnabled = false;
        /// <summary>$FF simulation between blocks</summary>
        int StreamFFCount = 0;
        /// <summary>Current SD card block that's been read</summary>
        byte[] StreamBuffer = new byte[512];
        /// <summary>The byte counter, starts at 512 and counts down</summary>
        int StreamByteCount = -1;
        /// <summary>The current counter into the StreamBuffer while streaming</summary>
        int StreamByteIndex = 0;
        /// <summary>The start block in the SD card - emulation starts at "0" as we're not in a card, this is still technically valid</summary>
        int BlockStart = 0;
        /// <summary>Number of 512 byte blocks we're streaming</summary>
        int BlockCount = -1;
        /// <summary>Global streaming handle</summary>
        int g_StreamHandle = -1;

        public const int SECTOR_BUFFER_CRC_SIZE = 2;
        public const int SECTOR_BUFFER_SPACE_SIZE = 4;
        public const int SECTOR_BUFFER_OFFEST = SECTOR_BUFFER_SPACE_SIZE + 1;
        public const int SECTOR_BUFFER_SIZE = SECTOR_BUFFER_OFFEST + 512+ SECTOR_BUFFER_CRC_SIZE + SECTOR_BUFFER_SPACE_SIZE;
        /// <summary>Used for reading sectors via the SD card interface</summary>
        byte[] SectorBuffer = new byte[SECTOR_BUFFER_SIZE];
        /// <summary>Index into Sector Buffer - used for CMD18 or CMD18</summary>
        int Sectorindex = 0;
        /// <summary>Current Sector Number used for CMD18 or CMD18</summary>
        int SectorNumber = -1;
        /// <summary>Current Sector Number used for CMD18 or CMD18</summary>
        int SectorNumber_Start = -1;
        /// <summary>Are we currently processing CMD18?</summary>
        eCMD18 ProcessingCMD18 = eCMD18.None;
        bool ProcessingCMD12 = false;

        /// <summary>Current SD port write state</summary>
        private eState State = eState.waiting_for_cmd;

        private CMD_Packet CMD = new CMD_Packet();

        /// <summary>Last streaming command written</summary>
        private int CMD_Read_Index;

        public iCSpect CSpect;


        // **********************************************************************
        /// <summary>
        ///     Get current directory
        /// </summary>
        // **********************************************************************
        public string CurrentPath
        {
            get
            {
                string s = Path.Combine(MMCPath, CurrentDirectory);
                // don't go "under" out path
                if (s.Length < MMCPath.Length) s = MMCPath;

                return s;
            }
        }

        // **********************************************************************
        /// <summary>
        ///     Init the RST 0x08 interface
        /// </summary>
        /// <returns>
        ///     List of addresses we're monitoring
        /// </returns>
        // **********************************************************************
        public List<sIO> Init(iCSpect _CSpect)
        {
            CSpect = _CSpect;
            bool active = (bool)CSpect.GetGlobal(eGlobal.esxDOS);
            if (!active) return null;

            // Install the disk interface - this lets us replace it with a behind the scenes WAD system if we want....
            FileSystem = new HDFileSystem();

            Debug.WriteLine("RST 0x08 interface added");


            // create a list of the ports we're interested in
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO(PC_Address, eAccess.Memory_EXE));                     // trap execution of RST $08
            for (int i = SDCard_Data_Port; i <= (0xff00+ SDCard_Data_Port); i += 0x100)
            {
                ports.Add(new sIO(i, eAccess.Port_Read));                // read a streaming file
            }

            for (int i = SDCard_Data_Port; i <= (0xff00 + SDCard_Data_Port); i += 0x100)
            {
                ports.Add(new sIO(i, eAccess.Port_Write));                // read a streaming file
            }

            for (int i = SDCard_Control_Port; i <=( 0xff00 + SDCard_Control_Port); i += 0x100)
            {
                ports.Add(new sIO(i, eAccess.Port_Write));                  // track SD card selection
            }
            //ports.Add(new sIO("<ctrl><alt>c", 0, eAccess.KeyPress));                   // Key press callback
            //ports.Add(new sIO(0xfe, eAccess.Port_Write));

            Initialised = true;
            return ports;
        }

        // **********************************************************************
        /// <summary>
        ///     Quit the device - free up anything we need to
        /// </summary>
        // **********************************************************************
        public void Quit()
        {
        }

        // **********************************************************************
        /// <summary>
        ///     Called when machine is reset
        /// </summary>
        // **********************************************************************
        public void Reset()
        {
            CloseAllHandles();
        }

        // **********************************************************************
        /// <summary>
        ///     Called once an emulation FRAME
        /// </summary>
        // **********************************************************************
        public void Tick()
        {
        }


        // ******************************************************************************************
        /// <summary>
        ///     Called once an OS emulator frame - do all UI rendering, opening windows etc here.
        /// </summary>
        // ******************************************************************************************
        public void OSTick()
        {
        }

        // **********************************************************************
        /// <summary>
        ///     Key press callback
        /// </summary>
        /// <param name="_id">The registered key ID</param>
        /// <returns>
        ///     True indicates the plugin handled the keey
        ///     False indicates someone else can handle it
        /// </returns>
        // **********************************************************************
        public bool KeyPressed(int _id)
        {
            return true;
        }
        // **********************************************************************
        /// <summary>
        ///     
        /// </summary>
        /// <param name="_port">Port/Address</param>
        /// <param name="_isvalid"></param>
        /// <returns></returns>
        // **********************************************************************
        public byte Read(eAccess _type, int _port, int _id, out bool _isvalid)
        {
            _isvalid = false;

            // esxDOS active? (when full NEXT ROM active, we're disabled)
            bool active = (bool)CSpect.GetGlobal(eGlobal.esxDOS);
            if (!active) return 0;

            if (_type == eAccess.Memory_EXE && _port == PC_Address)
            {
                int b = CSpect.GetNextRegister(0x50);
                if (b != 255) return 0;

                _isvalid = true;
                DoFileOps();
                return (byte)0;
            }
            else if (_type == eAccess.Port_Read && (_port & 0xff) == 0xEb)
            {
                if( ProcessingCMD12)
                {
                    _isvalid = true;
                    ProcessingCMD12 = false;
                    return 0;
                }
                if (ProcessingCMD18 == eCMD18.Reading)
                {
                    active = (bool)CSpect.GetGlobal(eGlobal.SDCardActive0);
                    if (!active)
                    {
                        _isvalid = true;
                        return StreamCMDData();
                    }
                }
                else if (port_e7 == 0xfe)
                {
                    active = (bool)CSpect.GetGlobal(eGlobal.SDCardActive0);
                    if (!active)
                    {
                        _isvalid = true;
                        return StreamByte();
                    }
                }
            }
            return 0;
        }
        #endregion




        #region SD Card access


        // **********************************************************************
        /// <summary>
        ///     Stream data from a fake SD card
        /// </summary>
        /// <returns>A new byte from the sector</returns>
        // **********************************************************************
        byte StreamCMDData()
        {
            if (ProcessingCMD18 == eCMD18.Reading)
            {
                byte b = SectorBuffer[Sectorindex];
                Sectorindex++;
                if (Sectorindex >= SectorBuffer.Length)
                {
                    SectorNumber++;
                    Sectorindex = 0;
                    try
                    {
                        FileSystem.Read(g_StreamHandle, SectorBuffer, SECTOR_BUFFER_OFFEST, 512);
                    }
                    catch {
                        //int o = 12;
                    }
                    ProcessingCMD18 = eCMD18.Reading;
                }
                return b;
            }
            /*else if (ProcessingCMD18 == eCMD18.Waiting)
            {
                return 255;
            }*/
            return 255;
        }

        // **********************************************************************
        /// <summary>
        ///     Stop transmission
        /// </summary>
        /// <param name="_cmd">Command packet</param>
        /// <returns></returns>
        // **********************************************************************
        byte StartCMD12(CMD_Packet _cmd)
        {
            ProcessingCMD18 =  eCMD18.None;
            ProcessingCMD12 = true;
            return 255;
        }

        // **********************************************************************
        /// <summary>
        ///     Read a single block/sector
        /// </summary>
        /// <param name="_cmd"></param>
        /// <returns></returns>
        // **********************************************************************
        byte StartCMD17(CMD_Packet _cmd)
        {
            return 255;
        }


        // **********************************************************************
        /// <summary>
        ///     Read a multiple sectors
        /// </summary>
        /// <param name="_cmd">Init command</param>
        /// <returns></returns>
        // **********************************************************************
        byte StartCMD18(CMD_Packet _cmd)
        {
            if (ProcessingCMD18 == eCMD18.None)
            {
                SectorNumber_Start = SectorNumber = (int)_cmd.arg32;
                try
                {
                    FileSystem.Seek(g_StreamHandle, ((long)SectorNumber) * 512, SeekOrigin.Begin);
                    FileSystem.Read(g_StreamHandle, SectorBuffer, SECTOR_BUFFER_OFFEST, 512);
                }
                catch { }
                SectorBuffer[SECTOR_BUFFER_OFFEST-1] = 0xfe;
                SectorBuffer[SectorBuffer.Length - 10] = 0xff;
                SectorBuffer[SectorBuffer.Length - 9] = 0xff;
                for (int i = 0; i < SECTOR_BUFFER_SPACE_SIZE; i++)
                {
                    SectorBuffer[i] = 0;
                }
                for (int i= (SectorBuffer.Length- SECTOR_BUFFER_SPACE_SIZE); i< SectorBuffer.Length; i++)
                {
                    SectorBuffer[i] = 0;
                }
                Sectorindex = 0;
                ProcessingCMD18 = eCMD18.Reading;   // ready to start reading
            }
            return 255;
        }

        // #####################################################################################
        /// <summary>
        ///     Read a byte from current command stream
        /// </summary>
        /// <returns></returns>
        // #####################################################################################
        byte SendCommand(CMD_Packet _cmd)
        {
            if (g_StreamHandle <0) return 0xff;


            switch (CMD.cmd)
            {
                //case eSPI_CMD.CMD0: return ProcessCMD0(false);
                //case eSPI_CMD.CMD1: return ProcessCMD1(false);
                //case eSPI_CMD.CMD8: return ProcessCMD8(null, false);
                //case eSPI_CMD.CMD9: return ProcessCMD9(false);
                case eSPI_CMD.CMD12: return StartCMD12(_cmd);
                //case eSPI_CMD.CMD13: return ProcessCMD13(false);
                //case eSPI_CMD.CMD16: return ProcessCMD16(null, false);
                case eSPI_CMD.CMD17: return StartCMD17(_cmd);
                case eSPI_CMD.CMD18: return StartCMD18(_cmd);
                //case eSPI_CMD.CMD24: return ProcessCMD24(null, false, false, 0);    // write
                //case eSPI_CMD.CMD55: return ProcessCMD55(false);
                //case eSPI_CMD.CMD58: return ProcessCMD58(false);
                //case eSPI_CMD.ACMD41: return ProcessACMD41(false);
                default:
                    break;
            }
            return 0xff;
        }


        // ################################################################################
        /// <summary>
        ///     Read an SPI command
        /// </summary>
        /// <param name="_byte">byte being sent</param>
        // ################################################################################
        private void WriteCommand(byte _byte)
        {
            // ignore FFs being sent....
            if (CMD_Read_Index == 0)
            {
                /*if(ProcessingCMD18 == eCMD18.Waiting)
                {
                    if (_byte == 0xfe)
                    {
                        ProcessingCMD18 = eCMD18.Reading;
                        Sectorindex = 0;
                        return;
                    }
                }*/
                if (_byte == 0xff) return;
                if ((_byte & 0xc0) != 0x40) return;       // must be a bin of %01xxxxxx format
            }

            switch (CMD_Read_Index)
            {
                case 0:
                    {
                        CMD.cmd = (eSPI_CMD)_byte;
                        break;
                    }
                case 1: CMD.arg0 = _byte; break;
                case 2: CMD.arg1 = _byte; break;
                case 3: CMD.arg2 = _byte; break;
                case 4: CMD.arg3 = _byte; break;
                case 5: CMD.crc = _byte; break;
            }
            CMD_Read_Index++;
            if ((CMD_Read_Index == 5 && (_byte&0xc0)==0x40) ||  (CMD_Read_Index == 6) )
            {
                if (g_StreamHandle >0)
                {
                    SendCommand(CMD);
                    //State = eState.ProcessingCMD;
                }
                CMD_Read_Index = 0;
            }
        }
        void WriteByte(byte _b)
        {
        }


        // ################################################################################
        /// <summary>
        ///     Main command interface. Z80 writes a value via this.... 
        /// </summary>
        /// <param name="_byte">Byte being written</param>
        // ################################################################################
        public void WriteE3(byte _byte)
        {
            if (g_StreamHandle < 0) return;
            
            switch (State)
            {
                case eState.waiting_for_cmd:
                    {
                        WriteCommand(_byte);
                        break;
                    }
                case eState.ProcessingCMD:
                    {
                        // ignore while processing command
                        if (g_StreamHandle >=0 )
                        {
                            // writing sector? all data goes to card
                            if (CMD.cmd == eSPI_CMD.CMD24)
                            {
                                WriteByte(_byte);
                                return;
                            }
                        }
                        WriteCommand(_byte);
                        return;
                    }

            }
        }

        // **********************************************************************
        /// <summary>
        ///     Write a value to one of the registered ports
        /// </summary>
        /// <param name="_port">the port being written to</param>
        /// <param name="_value">the value to write</param>
        // **********************************************************************
        public bool Write(eAccess _type, int _port, int _id, byte _value)
        {
            if (_type == eAccess.Port_Write && (_port & 0xff) == 0xe7)
            {
                // remember the value written so we know if it's SD card 1 or 2. We're only interested in SD card 1.
                // $E7/231      xxxxxxxx11100111 xxxxxxxx11100111         -        SD control	
                // $fe=card 0, $fd=card 1	
                port_e7 = _value;
                return false;
            }
            else if (_type == eAccess.Port_Write && (_port & 0xff) == 0xeb)
            {
                // $fe=card 0, $fd=card 1	
                WriteE3(_value);
                port_e7 = _value;
                return true;
            }
            return false;
        }
        #endregion



        // ************************************************************************
        /// <summary>
        ///     Set flags
        /// </summary>
        /// <param name="_f">true/fals3</param>
        // ************************************************************************
        public void SetFlags(eCPUFlags _flags)
        {
            regs.AF &= (UInt16)~_flags;
            regs.AF |= (UInt16)_flags;
        }
        // ************************************************************************
        /// <summary>
        ///     Set flags
        /// </summary>
        /// <param name="_f">true/fals3</param>
        // ************************************************************************
        public void ClearFlags(eCPUFlags _flags)
        {
            regs.AF &= (UInt16)~_flags;
        }

        // ************************************************************************
        /// <summary>
        ///     Set/clear carry flag
        /// </summary>
        /// <param name="_set">true/false</param>
        // ************************************************************************
        public void setC(bool _set)
        {
            if(_set)
            {
                SetFlags(eCPUFlags.C);
            }
            else
            {
                ClearFlags(eCPUFlags.C);
            }
        }

        //****************************************************************************
        /// <summary>
        ///     Pop off the stack
        /// </summary>
        /// <returns>popped value</returns>
        //****************************************************************************
        int Pop()
        {
            int sp = regs.SP;
            int t = CSpect.Peek( (ushort) sp++ );
            t |= ((int)CSpect.Peek( (ushort) (sp & 0xffff))) <<8;
            regs.SP = (ushort)(++sp & 0xffff);
            return t;
        }

        //****************************************************************************
        /// <summary>
        ///     Search for the file regardless of case
        /// </summary>
        /// <param name="_filename">Filename we're looking for</param>
        /// <returns>
        /// </returns>
        //****************************************************************************
        public string FindFileName(string _filename)        // Need a WAD version of this
        {
            string p = Path.GetDirectoryName(_filename);
            if (LastFolder != p || FindFiles == null)
            {
                // cache this directory
                FindFiles = FileSystem.GetFiles(p);
                LastFolder = p;
            }
            string filename = Path.GetFileName(_filename).ToLower();
            foreach (string s in FindFiles)
            {
                if (Path.GetFileName(s).ToLower() == filename)
                {
                    return s;
                }
            }
            return null;
        }


        //****************************************************************************
        /// <summary>
        ///     Get/Set the drive
        /// </summary>
        //****************************************************************************
        void DoGetSetDrive()
        {
            if (regs.A == '*' || regs.A == '$') CurrentDrive = regs.A;
            regs.A = (int)CurrentDrive;
        }


        //****************************************************************************
        /// <summary>
        ///     Open a file
        ///     In:   _IX = pointer to filename,0
        ///     Out:  _A  = file handle, or 0
        /// </summary>
        //****************************************************************************
        public bool OpenRST8File()
        {
            if (StreamEnabled)
            {
                regs.A = -1;
                return true;
            }
            if ((regs.A != CurrentDrive) && (regs.A != '*') && (regs.A != '$'))
            {
                regs.A = 11;
                return true;
            }
            string OpenFileBuffer = ""; // CurrentPath;    
            int len = OpenFileBuffer.Length;
            int len2 = len;

            int add = regs.IX;
            while (OpenFileBuffer.Length < 1023)
            {
                byte c = CSpect.Peek((UInt16)add++);
                if (c == 0x00) break;
                OpenFileBuffer += (char)c;
            }

            OpenFileBuffer = Path.Combine(CurrentPath, OpenFileBuffer);

            // make sure it's 8.3 format name - at most....
            bool verify = (bool) CSpect.GetGlobal(eGlobal.verify_EightDotThree);
            if(verify)
            {
                int cnt1 = 0;
                int cnt2 = 0;
                bool dot = false;
                for (int ii = len2; ii < len; ii++)
                {
                    if (OpenFileBuffer[ii] == '.')
                    {
                        dot = true;
                    }
                    else
                    {
                        if (OpenFileBuffer[ii] == '/')
                        {
                            if (cnt1 > 8)
                            {
                                regs.A = 7;     // invalid filename
                                return true;
                            }
                            cnt1 = 0;
                        }
                        else
                        {
                            if (OpenFileBuffer.Length == ii) break;
                            if (!dot) cnt1++; else cnt2++;
                        }
                    }
                }
                if (cnt1 > 8 || cnt2 > 3)
                {
                    regs.A = 7;     // invalid filename
                    return true;
                }
            }


            //#define FMODE_READ	0x01
            //#define FMODE_WRITE	0x02
            //#define FMODE_OPEN	0x00
            //#define FMODE_OPEN_CREATE	0x08		// open if exists, else create
            //#define FMODE_CREATE	0x04			// create if doesn't exist, else error
            //#define FMODE_TRUNCATE	0x0c		// create and/or truncate


            //
            // Now open file, and if in READ mode...read it all in...
            //
            try
            {
                int handle = -1;

                if ((regs.B & 0xc) == (int)RST08.FMODE_OPEN)
                {
                    OpenFileBuffer = FindFileName(OpenFileBuffer);
                    if (FileSystem.Exists(OpenFileBuffer))
                    {
                        handle = FileSystem.Open(OpenFileBuffer, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    }
                }
                else if ((regs.B & 0xc) == (int)RST08.FMODE_CREATE)
                {
                    string currfile = FindFileName(OpenFileBuffer);
                    if (!FileSystem.Exists(currfile))
                    {
                        // Can only create new if file doesn't exist
                        handle = FileSystem.Open(OpenFileBuffer, FileMode.CreateNew);
                    }
                }
                else if ((regs.B & 0xc) == (int)RST08.FMODE_TRUNCATE)
                {

                    string CurrFile = FindFileName(OpenFileBuffer);
                    if (CurrFile == null) CurrFile = OpenFileBuffer;
                    if (FileSystem.Exists(CurrFile))
                    {
                        OpenFileBuffer = CurrFile;
                        handle = FileSystem.Open(OpenFileBuffer, FileMode.Truncate);
                    }
                    else
                    {
                        handle = FileSystem.Open(OpenFileBuffer, FileMode.CreateNew);     // create original name
                    }
                    FindFiles = null;
                }
                if (handle < 0)
                {
                    regs.A = 5;             // No such file or directory
                    return true;
                }
                regs.A = handle&0xff;
                return false;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("is denied"))
                {
                    regs.A = 8;             // access is denied
                }
                else
                {
                    regs.A = 5;             // error/exception opening file
                }
                return true;
            }
            //regs.A = 12;            // too many files open error
            //return true;
        }

        //****************************************************************************
        /// <summary>
        ///     Read bytes from an open file
        ///	    	regs.A = handle
        ///			regs.BC = size
        ///			regs.IX = location        
        /// </summary>
        /// <returns>true for error, false for okay</returns>
        //****************************************************************************
        bool ReadRST8File()
        {
            if (StreamEnabled)
            {
                regs.A = -1;
                return true;
            }

            if (regs.A == 0) return true;           // file handle 0?
            int handle = regs.A;
            if (!FileSystem.IsOpen(handle)) return true;

            int size = regs.BC;
            int address = regs.IX;

            // read data
            int val = 0;
            try
            {
                val = FileSystem.Read(regs.A,filebuffer, 0, size);  //_read(handle, &(filebuffer[0]), size);
                size = val;
            }
            catch
            {
                regs.DE = regs.BC = 0;
                return true;
            }
            regs.B = (val >> 8) & 0xff;
            regs.C = (val & 0xff);
            regs.DE = regs.BC;

            // write to memory through whatever banks are set - this could be better in the extension....
            for (int i = 0; i < size; i++)
            {
                byte b = filebuffer[i];
                CSpect.Poke((UInt16) address++, b);
            }
            regs.HL = (UInt16) (address & 0xffff);
            return false;
        }

        //****************************************************************************
        /// <summary>
        ///     Write bytes to an open file
        ///			Regs.A  = handle
        ///			Regs.BC = size
        ///			Regs.IX = location
        /// </summary>
        /// <returns>true for error, false for okay</returns>
        //****************************************************************************
        bool WriteRST8File()
        {
            if (StreamEnabled)
            {
                regs.A = -1;
                return true;
            }


            if (regs.A == 0) return true;
            if (!FileSystem.IsOpen(regs.A)) return true;

            int handle = regs.A;
            int size = regs.BC;    
            int address = regs.IX;

            // ref memory through whatever banks are set
            byte[] data = CSpect.Peek((UInt16)address, size);
            address += size;

            try
            {
                // write data
                FileSystem.Write(handle, data, 0, size);
            }
            catch
            {
                regs.BC = 0;
                return true;
            }
            regs.BC = (ushort)size;
            //regs.IX += (UInt16)size; // does not change
            return false;
        }


        //****************************************************************************
        /// <summary>
        ///     Close the RST $08 file
        ///     Regs.A = handle
        /// </summary>
        /// <returns></returns>
        //****************************************************************************
        bool CloseRST8File()
        {
            if (StreamEnabled)
            {
                regs.A = -1;
                return true;
            }


            if (regs.A == 0) return true;
            bool res = FileSystem.Close(regs.A);
            if (res) return false;
            return true;
        }

        //****************************************************************************
        /// <summary>
        ///     Seek to position in file.
        /// </summary>
        /// <remarks>
        ///     Entry:
        ///         A=file handle
        ///         BCDE=bytes to seek
        ///         IXL[L from dot command]=seek mode:
        ///             esx_seek_set $00 set the fileposition to BCDE
        ///             esx_seek_fwd $01 add BCDE to the fileposition
        ///             esx_seek_bwd $02 subtract BCDE from the fileposition
        ///         Exit(success) :
        ///             Fc=0
        ///             BCDE=current position
        ///         Exit(failure) :
        ///             Fc=1
        ///             A=error code
        ///     NOTES:
        ///         Attempts to seek past beginning/end of file leave BCDE=position=0/filesize
        ///         respectively, with no error.
        ///</remarks>
        //****************************************************************************
        bool SeekRST8File()
        {
            if (StreamEnabled)
            {
                regs.A = -1;
                return true;
            }


            if (regs.A == 0) return true;
            if( !FileSystem.IsOpen(regs.A)) return false;
            int handle = regs.A;

            long offset = (int)regs.DE | ((int)regs.BC << 16);
            int seek_kind = regs.IX & 0xff;
            switch (seek_kind)
            {
                case 0: offset = (int)FileSystem.Seek(handle, offset, SeekOrigin.Begin); break;      // from start
                case 1: offset = (int)FileSystem.Seek(handle, offset, SeekOrigin.Current); break;      // from current +pos (rel)
                case 2: offset = (int)FileSystem.Seek(handle, -offset, SeekOrigin.Current); break;     // negative current pos (rel)
                default:
                    return false;
            }

            regs.DE = (UInt16) (offset & 0xffff);
            regs.BC = (UInt16) (offset >> 16);
            return false;
        }


        //****************************************************************************
        /// <summary>
        ///     F_FGETPOS - get current file position
        /// </summary>
        /// <remarks>
        ///     Entry:
        ///         A=file handle
        ///         Exit(success) :
        ///             Fc=0
        ///             BCDE=current position
        ///         Exit(failure) :
        ///             Fc=1
        ///             A=error code
        ///     NOTES:
        ///         Attempts to seek past beginning/end of file leave BCDE=position=0/filesize
        ///         respectively, with no error.
        ///</remarks>
        //****************************************************************************
        bool GetPosRST8File()
        {
            if (StreamEnabled)
            {
                regs.A = -1;
                return true;
            }


            if (regs.A == 0) return true;
            if(!FileSystem.IsOpen(regs.A)) return false;
            int handle = regs.A;

            long offset = FileSystem.GetPosition(handle);

            regs.DE = (UInt16)(offset & 0xffff);
            regs.BC = (UInt16)(offset >> 16);
            return false;
        }


        //****************************************************************************
        /// <summary>
        ///     get the info of a file
        /// </summary>
        /// <param name="_name"></param>
        /// <returns></returns>
        //****************************************************************************
        sFileInfo GetFileInfo(string _name)
        {
            sFileInfo info;
            FileAttributes att = File.GetAttributes(_name);
            info.Attrib = 0;
            info.datestamp = 0;
            info.FileSize = 0;
            info.timestamp = 0;

            if (((int)att & (int)FileAttributes.ReadOnly) != 0) info.Attrib |= (int)eMSDOS_ATTRIB.ReadOnly;
            if (((int)att & (int)FileAttributes.Hidden) != 0) info.Attrib |= (int)eMSDOS_ATTRIB.Hidden;
            if (((int)att & (int)FileAttributes.System) != 0) info.Attrib |= (int)eMSDOS_ATTRIB.System;
            if (((int)att & (int)FileAttributes.Archive) != 0) info.Attrib |= (int)eMSDOS_ATTRIB.Archive;
            if (((int)att & (int)FileAttributes.Directory) != 0) info.Attrib |= (int)eMSDOS_ATTRIB.Directory;


            DateTime t = File.GetLastWriteTime(_name);

            // MSDOS timestamp = 0-4 second, 5-10 minute, 11-15 hour (0-23 not 0-12 + AM/PM)
            int msdos_t = t.Second / 2;
            msdos_t |= t.Minute << 5;
            msdos_t |= t.Hour << 11;

            // MSDOS datestamp = 0-4 Day, 5-8 Month, 9-15 years since 1980 
            int msdos_date = t.Day;
            msdos_date |= t.Month << 5;
            msdos_date |= (t.Year - 1980) << 9;

            info.timestamp = (UInt16)msdos_t;
            info.datestamp = (UInt16)msdos_date;

            if(((int)att & (int)FileAttributes.Directory)==0)
            {
                FileInfo fi = new FileInfo(_name);
                info.FileSize = (UInt32)(fi.Length & 0xffffffff);
            }else{
                info.FileSize = 0;
            }
            return info;
        }

        //****************************************************************************
        /// <summary>
        ///     Get file info 
        ///         A = Handle
        ///         IX = buffer
        /// </summary>
        /// <returns></returns>
        //****************************************************************************
        public bool DoGetFileInfoHandle()
        {
            if (StreamEnabled)
            {
                regs.A = -1;
                return true;
            }
            if (regs.A == 0) return true;


            string name = FileSystem.GetName(regs.A);

            int address = regs.IX;
            CSpect.Poke((UInt16)address++, 0);        // drive
            CSpect.Poke((UInt16)address++, 0);        // device
            CSpect.Poke((UInt16)address++, 0);
            CSpect.Poke((UInt16)address++, 0);
            CSpect.Poke((UInt16)address++, 0);
            CSpect.Poke((UInt16)address++, 0);
            CSpect.Poke((UInt16)address++, 0);

            CSpect.Poke((UInt16)address++, 0);
            CSpect.Poke((UInt16)address++, 0);
            CSpect.Poke((UInt16)address++, 0);
            CSpect.Poke((UInt16)address++, 0);
            if (string.IsNullOrEmpty(name)) return false;

            sFileInfo info = GetFileInfo(name);
            address = regs.IX;
            CSpect.Poke((UInt16)address++, 0);        // drive
            CSpect.Poke((UInt16)address++, 0);        // device
            CSpect.Poke((UInt16)address++, (byte)info.Attrib);
            CSpect.Poke((UInt16)address++, (byte)(info.timestamp & 0xff));
            CSpect.Poke((UInt16)address++, (byte)((info.timestamp >> 8) & 0xff));
            CSpect.Poke((UInt16)address++, (byte)(info.datestamp & 0xff));
            CSpect.Poke((UInt16)address++, (byte)((info.datestamp >> 8) & 0xff));

            CSpect.Poke((UInt16)address++, (byte)(info.FileSize & 0xff));
            CSpect.Poke((UInt16)address++, (byte)((info.FileSize >> 8) & 0xff));
            CSpect.Poke((UInt16)address++, (byte)((info.FileSize >> 16) & 0xff));
            CSpect.Poke((UInt16)address++, (byte)((info.FileSize >> 24) & 0xff));
            return false;
        }

        //****************************************************************************
        // Get file info
        // _A = drive spec
        // _IX = filename/filespec
        // _DE = 11 byte buffer address
        //****************************************************************************
        public bool DoGetFileInfoString()
        {
            if (StreamEnabled)
            {
                regs.A = -1;
                return true;
            }

            if ((regs.A != CurrentDrive) && (regs.A != '*') && (regs.A != '$'))
            {
                regs.A = 11;
                return true;
            }



            int add = regs.IX;
            int len = 0;
            string name = CurrentPath;
            if (!CurrentPath.EndsWith("\\")) name += "\\";
            while (len < 1023)
            {
                byte b = CSpect.Peek((UInt16)add++);
                if (b == 0x00) break;
                name += (char)b;
                len++;
            }

            name = FindFileName(name);
            if (!File.Exists(name))
            {
                regs.A = 7;     // invalid filename
                return true;
            }

            sFileInfo info = GetFileInfo(name);
            int address = regs.DE;
            CSpect.Poke((UInt16)address++, 0);        // drive
            CSpect.Poke((UInt16)address++, 0);        // device
            CSpect.Poke((UInt16)address++, (byte)info.Attrib);
            CSpect.Poke((UInt16)address++, (byte)(info.timestamp & 0xff));
            CSpect.Poke((UInt16)address++, (byte)((info.timestamp >> 8) & 0xff));
            CSpect.Poke((UInt16)address++, (byte)(info.datestamp & 0xff) );
            CSpect.Poke((UInt16)address++, (byte)((info.datestamp >> 8) & 0xff));
            CSpect.Poke((UInt16)address++, (byte)(info.FileSize & 0xff));
            CSpect.Poke((UInt16)address++, (byte)((info.FileSize >> 8) & 0xff));
            CSpect.Poke((UInt16)address++, (byte)((info.FileSize >> 16) & 0xff));
            CSpect.Poke((UInt16)address++, (byte)((info.FileSize >> 24) & 0xff));
            return false;
        }


        //****************************************************************************
        //	Rename a file via RST $08 file
        //	In:	_A=drive
        //      A = Drive
        //      IX = Full Filename
        //      DE = NEW Full Filename
        //****************************************************************************
        bool RST08_Rename()
        {
            if (StreamEnabled)
            {
                regs.A = -1;
                return true;
            }


            if ((regs.A != CurrentDrive) && (regs.A != '*') && (regs.A != '$'))
            {
                regs.A = 11;
                return true;
            }


            // get src name
            string SrcFile = CurrentPath;
            int len = SrcFile.Length;
            int len2 = len;
            int add = regs.IX;
            while (SrcFile.Length < 1023)
            {
                byte[] m = CSpect.Peek((UInt16)add++,1);
                if (m[0] == (char)0x00) break;
                SrcFile += (char)m[0];
            }

            // get dest name
            string DestFile = CurrentPath;
            len = DestFile.Length;
            len2 = len;
            add = regs.DE;
            while (DestFile.Length < 1023)
            {
                byte[] m = CSpect.Peek((UInt16)add++, 1);
                if (m[0] == 0x00) break;
                DestFile += (char)m[0];
            }



            // now rename it
            try
            {
                string CurrFile = FindFileName(SrcFile);
                if (File.Exists(CurrFile))
                {
                    System.IO.File.Move(CurrFile, DestFile);
                }
            }
            catch
            {
                regs.A = 6;     //IO error (or something)
                return true;    // carry set
            }
            return false;       // carry clear
        }


        // ****************************************************************************************************
        /// <summary>
        ///     M_GETDATE($8e) - Get the current date/time.  (RTC simulation)
        ///       Fc=0 if RTC present and providing valid date/time, and: 
        ///               BC=date, in MS-DOS format
        ///               DE=time, in MS-DOS format
        ///       Fc=1 if no RTC, or invalid date/time, and: 
        ///               BC=0 
        ///               DE=0
        ///      0-4 	Day of the month(1-31)
        ///      5-8 	Month(1 = January, 2 = February, and so on)
        ///      9-15 	Year offset from 1980 (add 1980 to get actual year)
        ///
        ///      0-4 	Second divided by 2
        ///      5-10 	Minute(0-59)
        ///      11-15   Hour(0-23 on a 24-hour clock)        
        /// </summary>
        /// <returns>FALSE = RTC present</returns>
        // ****************************************************************************************************
        bool GetDateTime()
        {
            DateTime t = DateTime.Now;

            // MSDOS timestamp = 0-4 second/2, 5-10 minute, 11-15 hour (0-23 not 0-12 + AM/PM)
            int msdos_t = t.Second / 2;
            msdos_t |= t.Minute << 5;
            msdos_t |= t.Hour << 11;

            // MSDOS datestamp = 0-4 Day, 5-8 Month, 9-15 years since 1980 
            int msdos_date = t.Day;
            msdos_date |= t.Month << 5;
            msdos_date |= (t.Year - 1980) << 9;

            regs.BC = (UInt16)msdos_date;
            regs.DE = (UInt16)msdos_t;

            regs.HL = (UInt16) ( ((t.Second & 0xff) << 8) | (regs.HL & 0xff) );
            return false;           // false = RTC present
        }


        // *************************************************************************** 
        // * DISK_FILEMAP($85)                                                      * 
        // *************************************************************************** 
        // Obtain a map of card addresses describing the space occupied by the file. 
        // Can be called multiple times if buffer is filled, continuing from previous. 
        // Entry: 
        //       A=file handle(just opened, or following previous DISK_FILEMAP calls)
        //       IX=buffer (32bit LBA, and 16bit block out *pairs*)
        //       DE=max entries(each 6 bytes: 4 byte address, 2 byte sector count)
        // Exit(success): 
        //       Fc=0 
        //       DE=max entries-number of entries returned
        //       HL=address in buffer after last entry
        //       A=card flags: bit 0=card id(0 or 1)
        //                     bit 1=0 for byte addressing, 1 for block addressing
        // Exit(failure): 
        //       Fc=1 
        //       A=error
        // NOTES: 
        //       Each entry may describe an area of the file between 2K and just under 32MB
        //       in size, depending upon the fragmentation and disk format. 
        //       Please see example application code, stream.asm, for full usage information 
        //       (available separately or at the end of this document)
        //
        // *************************************************************************** 
        public bool StreamFileMap()
        {
            if (regs.A < 0)
            {
                regs.A = -1;
                return true;
            }
            if(!FileSystem.IsOpen(regs.A)) return true;
            g_StreamHandle = regs.A;

            int tlen = (int)FileSystem.GetLength(g_StreamHandle);
            int len = (int)(tlen + 511) >> 9;
            int count = (tlen + (32 * 1024 * 1024)-1) / (32 * 1024 * 1024);
            int block = 0;
            int offset = 0;
            while (count > 0)
            {
                // set a block address - start at 0
                CSpect.Poke((UInt16)(regs.IX + offset), (byte) (block&0xff));
                CSpect.Poke((UInt16)(regs.IX + offset + 1), (byte) ((block >> 8) & 0xff));
                CSpect.Poke((UInt16)(regs.IX + offset + 2), (byte) ((block >> 16) & 0xff));
                CSpect.Poke((UInt16)(regs.IX + offset + 3), (byte) ((block >> 24) & 0xff));

                if (count != 1)
                {
                    CSpect.Poke((UInt16)(regs.IX + offset + 4), (byte)255);
                    CSpect.Poke((UInt16)(regs.IX + offset + 5), (byte)255);
                }
                else
                {
                    tlen = (tlen + 511) >> 9;
                    CSpect.Poke((UInt16)(regs.IX + offset + 4), (byte)(tlen & 0xff));
                    CSpect.Poke((UInt16)(regs.IX + offset + 5), (byte)((tlen >> 8) & 0xff));
                }

                tlen -= (65535 * 512);
                regs.DE--;
                count--;
                block += 65535;
                offset += 6;
            }
            regs.HL = (UInt16) (regs.IX + offset +6);
            regs.A = 2;         // card ID 0, abd 1 for BLOCK addressing
            return false;
        }


        // *************************************************************************** 
        // * DISK_STRMSTART ($86)                                                    * 
        // *************************************************************************** 
        // Start reading from the card in streaming mode. 
        // Entry: IXDE=card address 
        //        BC=number of 512-byte blocks to stream 
        //        A=card flags ; Exit (success): Fc=0 
        //                 B=0 for SD/MMC protocol, 1 for IDE protocol 
        //                 C=8-bit data port ; Exit (failure): Fc=1, A=esx_edevicebusy 
        // 
        // NOTES: 
        // On the Next, this call always returns with B=0 (SD/MMC protocol) and C=$EB 
        // When streaming using the SD/MMC protocol, after every 512 bytes you must read 
        // a 2-byte CRC value (which can be discarded) and then wait for a $FE value 
        // indicating that the next block is ready to be read. 
        // Please see example application code, stream.asm, for full usage information 
        // (available separately or at the end of this document).
        public bool StartStream()
        {
            StreamEnabled = false;
            if (g_StreamHandle <0 ) return true;

            bool DontWait = (regs.A & 0x80) == 0x80;
            BlockStart = ((regs.IX & 0xffff) << 16) + (regs.DE & 0xffff);
            BlockCount = regs.BC;
            StreamByteCount = -1;

            try
            {
                FileSystem.Seek(g_StreamHandle,BlockStart * 512, SeekOrigin.Begin);           // move to start of the file
            }
            catch { }

            regs.B = 0;
            regs.C = 0xeb;
            StreamEnabled = true;

            FillStreamBuffer();           // fill initial buffer
            StreamFFCount = -1;
            if (DontWait)
            {
                StreamFFCount = 0x30;
            }
            return false;
        }


        // *********************************************************************************
        /// <summary>
        ///     Fill the streaming buffer
        /// </summary>
        // *********************************************************************************
        private void FillStreamBuffer()
        {
            for (int i = 0; i < 512; i++) { StreamBuffer[i] = 0; }        // clear buffer
            try
            {
                int val = FileSystem.Read(g_StreamHandle, StreamBuffer, 0, 512);
            }
            catch { }
            StreamByteCount = 512;
            StreamByteIndex = 0;
            BlockCount--;
            StreamFFCount = 10;           // 2 bytes CRC, and some blank between blocks
        }

        // *********************************************************************************
        /// <summary>
        ///     Read a byte from the stream
        /// </summary>
        /// <returns>
        ///     Return a byte from the stream
        /// </returns>
        // *********************************************************************************
        public byte StreamByte()
        {
            if (!StreamEnabled) return 0xff;


            if (StreamFFCount > 0)
            {
                StreamFFCount--;
                return 0xff;
            }
            if (StreamFFCount == 0)
            {
                StreamFFCount--;
                return 0xfe;            // data ready...
            }




            // fill buffer?
            if (BlockCount < 0)
            {
                for (int i = 0; i < 512; i++) { StreamBuffer[i] = 0; }
                StreamByteCount = 512;
                BlockCount = -1;
                StreamFFCount = 10;
                StreamByteIndex = 0;
            }
            else
            {
                if (StreamByteCount <= 0)
                {
                    FillStreamBuffer();
                    return 0xff;            // inbetween buffer FFs
                }
            }

            StreamByteCount--;
            return StreamBuffer[StreamByteIndex++];
        }



        // *************************************************************************** 
        // * DISK_STRMEND($87)                                                       * 
        // *************************************************************************** 
        // Stop current streaming operation. 
        // Entry: A=card flags; Exit(success): Fc=0 
        // Exit(failure): Fc=1, A=esx_edevicebusy 
        // 
        // NOTES: 
        // This call must be made to terminate a streaming operation. 
        // Please see example application code, stream.asm, for full usage information 
        // (available separately or at the end of this document).
        public bool EndStream()
        {
            StreamEnabled = false;
            StreamFFCount = -1;
            StreamByteCount = -1;
            StreamByteIndex = 0;
            BlockStart = 0;
            BlockCount = -1;
            return false;
        }

        //****************************************************************************
        /// <summary>
        ///     Cloase all files, and end streaming if opened. Usually called on reset.
        /// </summary>
        //****************************************************************************
        private void CloseAllHandles()
        {
            if (FileSystem != null)
            {
                FileSystem.CloseAll();
            }
            EndStream();
        }

        //****************************************************************************
        /// <summary>
        ///     Get current working directory
        /// </summary>
        /// <remarks>
        ///  Entry:
        ///     A=drive, to obtain current working directory for that drive 
        ///     or: A=$ff, to obtain working directory for a supplied filespec in DE
        ///     DE=filespec(only if A=$ff)
        ///     IX[HL from dot command]=buffer for null-terminated path
        ///  Exit(success):
        ///     Fc=0
        ///  Exit(failure):
        ///     Fc=1
        ///     A=error code
        /// </remarks>
        //****************************************************************************
        public bool GetCurrentDirectory()
        {
            int address = regs.IX;
            for (int i=0;i<CurrentDirectory.Length;i++)
            {
                byte c = (byte)CurrentDirectory[i];
                CSpect.Poke((UInt16)address++, c);
            }
            CSpect.Poke((UInt16)address++, 0x00);
            return false;       // carry clear
        }



        //****************************************************************************
        /// <summary>
        ///     Open Directory
        /// </summary>
        /// <remarks>
        ///  Entry:
        ///      A=drive specifier(overridden if filespec includes a drive)
        ///      IX[HL from dot command]=directory, null-terminated
        ///      
        ///      B=access mode - add together any or all of:
        ///      $00 esx_mode_short_only F_READDIR returns short 8.3 names
        ///      $10 esx_mode_lfn_only F_READDIR returns LFNs only
        ///      $18 esx_mode_lfn_and_short F_READDIR returns LFN followed by 8.3 name
        ///      (both null-terminated)
        ///      $20 esx_mode_use_wildcards F_READDIR only returns entries matching
        ///      wildcard
        ///      $40 esx_mode_use_header F_READDIR additionally returns +3DOS header
        ///      $80 esx_mode_sf_enable enable sort/filter mode in C
        ///      
        ///      C=sort/filter mode(if enabled in access mode) – add together:
        ///      $80 esx_sf_exclude_files F_READDIR doesn't return files
        ///      $40 esx_sf_exclude_dirs F_READDIR doesn't return directories
        ///      $20 esx_sf_exclude_dots F_READDIR doesn't return . or .. directories
        ///      $10 esx_sf_exclude_sys F_READDIR doesn’t return system/hidden files
        ///      $08 esx_sf_sort_enable entries will be sorted
        ///      (unless memory exhausted/BREAK pressed)
        ///      $00 esx_sf_sort_lfn sort by LFN
        ///      $01 esx_sf_sort_short sort by short name
        ///      $02 esx_sf_sort_date sort by date/time(LFN breaks ties)
        ///      $03 esx_sf_sort_size sort by file size(LFN breaks ties)
        ///      $04 esx_sf_sort_reverse sort order is reversed
        ///      
        ///      DE=null-terminated wildcard string, if esx_mode_use_wildcards
        ///      The same string must also be passed when calling F_READDIR, in case
        ///      sorting is not possible and a fall back to unsorted mode is made.
        ///  Exit(success):
        ///      A=dir handle
        ///      C=0 if sort operation not completed(out of memory/user pressed BREAK)
        ///      C<>0 if sorting completed
        ///      Fc=0
        ///  Exit(failure):
        ///      Fc=1
        ///      A=error code
        /// </remarks>
        /// <returns>true for error, false for okay</returns>
        //****************************************************************************
        public bool OpenDirectory()
        {
            string wildcard = "";
            bool exclude_dirs = false;
            bool exclude_files = false;
            bool sort_enabled = false;
            bool sort_sfn = false;			// false = lfn
            bool sort_reversed = false;

            if ((regs.B & 0x80) != 0)
            {
                if ((regs.BC & 0x80) != 0) exclude_files = true;
                if ((regs.BC & 0x40) != 0) exclude_dirs = true;
            }

            if ((regs.C & 0x08) != 0)
            {
				sort_enabled = true;

				if ((regs.C & 0x01) != 0) sort_sfn = true;
				if ((regs.C & 0x04) != 0) sort_reversed = true;
            }

            if ((regs.BC & 0x2000) != 0)
            {
                int max_len = 260;
                int wadd = regs.DE;
                while (max_len > 0)
                {
                    byte c = CSpect.Peek((ushort)wadd++);
				if (c == 0) break;
                    wildcard += (char)c;
                    max_len--;
                }
                if (wildcard == "") wildcard = "*";
            }
            else
            {
                wildcard = "*";
            }

            string[] dirs = Directory.GetDirectories(CurrentPath);
            string[] files = Directory.GetFiles(CurrentPath, wildcard);


            List<DirEntry> entries = new List<DirEntry>();
            if (!exclude_dirs)
            {
                // get parent folder date/time etc so we can use in "." and ".."
                sFileInfo fo = GetFileInfo(Path.Combine(CurrentPath, ".."));

                // Add "." and ".."
                DirEntry e = new DirEntry();
                e.FileName_Short = e.FileName = ".";
                e.attrib = fo; 
                entries.Add(e);

                e = new DirEntry();
                e.FileName_Short = e.FileName = "..";
                e.attrib = fo;
                entries.Add(e);

                foreach (string dir in dirs)
                {
                    string d = Path.GetFullPath(dir);
                    d = Path.GetFileName(d);
                    e = new DirEntry();
                    e.FileName = d;
                    e.FileName_Short = d;
					e.Datestamp = new FileInfo(Path.Combine(CurrentPath, d)).CreationTime;
					e.FileSize = 0;
                    e.attrib = GetFileInfo(Path.Combine(CurrentPath, d));

                    entries.Add(e);
                }
            }
            if (!exclude_files)
            {
                foreach (string file in files)
                {
                    string f = Path.GetFullPath(file);
                    f = Path.GetFileName(f);
                    DirEntry e = new DirEntry();
                    e.FileName = f;
                    e.FileName_Short = f;
					e.Datestamp = new FileInfo(Path.Combine(CurrentPath, f)).CreationTime;
					e.FileSize = (uint) new FileInfo(Path.Combine(CurrentPath, f)).Length;
                    e.attrib = GetFileInfo(Path.Combine(CurrentPath, f));

                    entries.Add(e);
                }
            }

//			entries.Sort((x, y) => x.FileSize.CompareTo(y.FileSize));
//			entries.Sort((x, y) => x.Datestamp.CompareTo(y.Datestamp));

			if (sort_enabled)
			{
				if (!sort_sfn)
				{
					if (!sort_reversed)
					{ 
						entries.Sort((x, y) => x.FileName.CompareTo(y.FileName));
					}
					else
					{
						entries.Sort((x, y) => y.FileName.CompareTo(x.FileName));
					}
				}
				else
				{
					if (!sort_reversed)
					{
						entries.Sort((x, y) => x.FileName.CompareTo(y.FileName_Short));
					}
					else
					{
						entries.Sort((x, y) => y.FileName.CompareTo(x.FileName_Short));
					}
				}
			}

            OpenedDirectory = new FullDirectory();
            OpenedDirectory.CurrentIndex = 0;
            OpenedDirectory.Handle = 234;
            OpenedDirectory.Files = entries;

            regs.A = OpenedDirectory.Handle;
            regs.C = 0;
            return false;
        }

        //****************************************************************************
        /// <summary>
        ///     Read the next directory entry on an already opened directory
        /// </summary>
        /// <remarks>
        ///   Entry:
        ///     A=handle
        ///     IX[HL from dot command]=buffer
        ///     Additionally, if directory was opened with esx_mode_use_wildcards:
        ///     DE=wildcard string (null-terminated)
        ///  Exit(success):
        ///     A=number of entries returned(0 or 1)
        ///     If 0, there are no more entries
        ///     Fc=0
        ///   Exit(failure):
        ///     Fc=1
        ///     A=error code
        ///  
        ///   Buffer format:
        ///   1 byte file attributes(MSDOS format)
        ///   ? bytes file/directory name(s), null-terminated
        ///   2 bytes timestamp(MSDOS format)
        ///   2 bytes datestamp(MSDOS format)
        ///   4 bytes file size
        /// </remarks>
        /// <returns>true for error, false for okay</returns>
        //****************************************************************************
        public bool ReadDirectoryEntry()
        {
            if (OpenedDirectory.Handle != regs.A)
            {
                regs.A = 255;
                return true;
            }
            int curr = OpenedDirectory.CurrentIndex++;
            if (curr >= OpenedDirectory.Files.Count)
            {
                regs.A = 0; // done
                return false;
            }

            DirEntry d = OpenedDirectory.Files[curr];
            string s = d.FileName;
            if (s.Length > 260) s = s.Substring(0, 260);
            int add = regs.IX;
            CSpect.Poke((ushort)add++, (byte)d.attrib.Attrib);
            for (int i = 0; i < s.Length; i++)
            {
                CSpect.Poke((ushort)add++, (byte)s[i]);
            }
            CSpect.Poke((ushort)add++, (byte)0);
            CSpect.Poke((ushort)add++, (byte)(d.attrib.timestamp & 0xff));
            CSpect.Poke((ushort)add++, (byte)((d.attrib.timestamp >> 8) & 0xff));
            CSpect.Poke((ushort)add++, (byte)(d.attrib.datestamp & 0xff));
            CSpect.Poke((ushort)add++, (byte)((d.attrib.datestamp >> 8) & 0xff));
            CSpect.Poke((ushort)add++, (byte)(d.attrib.FileSize & 0xff));
            CSpect.Poke((ushort)add++, (byte)((d.attrib.FileSize >> 8) & 0xff));
            CSpect.Poke((ushort)add++, (byte)((d.attrib.FileSize >> 16) & 0xff));
            CSpect.Poke((ushort)add++, (byte)((d.attrib.FileSize >> 24) & 0xff));

            return false;
        }

        //****************************************************************************
        /// <summary>
        ///     Get the current index into a directory
        /// </summary>
        /// <remarks>
        ///  Entry:
        ///      A=handle
        ///  Exit(success):
        ///      BCDE=current offset in directory
        ///      Fc=0
        ///  Exit(failure):
        ///      Fc=1
        ///      A=error code
        /// </remarks>
        /// <returns>true for error, false for okay</returns>
        //****************************************************************************
        public bool GetDirectoryPosition()
        {
            if (OpenedDirectory.Handle != regs.A)
            {
                regs.A = 255;
                return true;
            }

            regs.DE = (ushort) (OpenedDirectory.CurrentIndex & 0xffff);
            regs.BC = (ushort) ((OpenedDirectory.CurrentIndex>>16) & 0xffff);
            regs.A = 0;
            return false;
        }


        //****************************************************************************
        /// <summary>
        ///     Get the current index into a directory
        /// </summary>
        /// <remarks>
        ///  Entry:
        ///      A=handle
        ///      BCDE=current offset in directory  (as returned by F_TELLDIR)
        ///  Exit(success):
        ///      Fc=0
        ///  Exit(failure):
        ///      Fc=1
        ///      A=error code
        /// </remarks>
        /// <returns>true for error, false for okay</returns>
        //****************************************************************************
        public bool SetDirectoryPosition()
        {
            if (OpenedDirectory.Handle != regs.A)
            {
                regs.A = 255;
                return true;
            }
            OpenedDirectory.CurrentIndex = regs.DE | (((int)regs.BC) << 16);
            if(OpenedDirectory.CurrentIndex>=OpenedDirectory.Files.Count)
            {
                OpenedDirectory.CurrentIndex = OpenedDirectory.Files.Count-1;
            }
            return false;
        }

        //****************************************************************************
        /// <summary>
        ///     Reset the current dir index to 0
        /// </summary>
        /// <remarks>
        ///  Entry:
        ///      A=handle
        ///  Exit(success):
        ///      Fc=0
        ///  Exit(failure):
        ///      Fc=1
        ///      A=error code
        /// </remarks>
        /// <returns>true for error, false for okay</returns>
        //****************************************************************************
        public bool ResetDirectoryPosition()
        {
            if (OpenedDirectory.Handle != regs.A)
            {
                regs.A = 255;
                return true;
            }
            OpenedDirectory.CurrentIndex = 0;
            return false;
        }


        //****************************************************************************
        /// <summary>
        ///     Set the current directory
        /// </summary>
        /// <remarks>
        /// Entry:
        ///     A=drive specifier(overridden if filespec includes a drive)
        ///     IX[HL from dot command]=path, null-terminated
        /// Exit(success):
        ///     Fc=0
        /// Exit(failure):
        ///     Fc=1
        ///     A=error code
        /// </remarks>
        /// <returns>true for error, false for okay</returns>
        //****************************************************************************
        public bool SetCurrentDirectory()
        {
            if ((regs.A != CurrentDrive) && (regs.A != '*') && (regs.A != '$'))
            {
                regs.A = 11;
                return true;
            }

            string dir = "";
            int add = regs.IX;
            int max_len = 260;
            while (max_len>0)
            {
                byte b = CSpect.Peek((ushort)add++);
                if (b == 0) break;
                dir = dir + (char)b;

                max_len--;
            }
            if(dir == ".")
            {
                regs.A = -1;
                return true;
            }

            // Make sure we can't go "below" the root folder specified in MMC= command line
            string p = CurrentDirectory;
            CurrentDirectory = Path.Combine(CurrentDirectory, dir);
            string s = Path.GetFullPath(CurrentPath);
            if( !s.ToLower().Contains(MMCPath.ToLower()) )
            {
                CurrentDirectory = p;
                regs.A = -1;
                return true;
            }else
            {
                CurrentDirectory = s.Substring(MMCPath.Length);
                if(CurrentDirectory.StartsWith("\\") || CurrentDirectory.StartsWith("//"))
                {
                    CurrentDirectory = CurrentDirectory.Substring(1);
                }
            }
            return false;
        }


        //****************************************************************************
        /// <summary>
        ///     Create directory (untested)
        /// </summary>
        /// <remarks>
        /// Entry:
        ///     A=drive specifier(overridden if filespec includes a drive)
        ///     IX[HL from dot command]=path, null-terminated
        /// Exit(success):
        ///     Fc=0
        /// Exit(failure):
        ///     Fc=1
        ///     A=error code
        /// </remarks>
        /// <returns></returns>
        //****************************************************************************
        public bool MakeDirectory()
        {
            string new_folder = "";
            int max_len = 260;
            int add = regs.IX;
            while (max_len > 0)
            {
                byte c = CSpect.Peek((ushort)add++);
                if (c == 0) break;
                new_folder += (char)c;
                max_len--;
            }

            try
            {
                Directory.CreateDirectory(new_folder);
            }catch
            {
                regs.A = -2;
                return true;
            }
            return false;
        }


        //********************************************************************************************************************************************************
        /// <summary>
        ///     A .NEX file can request the file remain open. This file handle needs to be passed into the RST8 file system for management
        /// </summary>
        /// <returns>
        ///     False for okay
        ///     True for error
        /// </returns>
        //********************************************************************************************************************************************************
        public bool SetFileHandle()
        {
            FileStream filestream= (FileStream)CSpect.GetGlobal(eGlobal.file_handle);
            string filename = (string)CSpect.GetGlobal(eGlobal.file_name);

            int handle = FileSystem.SetFileHandle(filestream, filename);
            if (handle < 0) return true;
            CSpect.SetGlobal(eGlobal.next_file_handle, handle);
            return false;
        }


        // ***************************************************************************
        // * M_DOSVERSION($88) *
        // ***************************************************************************
        // Get API version/mode information.
        // Entry:
        // -
        // Exit:
        //      For esxDOS <= 0.8.6
        //      Fc=1, error
        //      A=14 ("no such device")
        //
        // For NextZXOS:
        //      Fc=0, success
        //      B='N',C='X' (NextZXOS signature)
        //      DE=NextZXOS version in BCD format: D=major, E=minor version
        //          eg for NextZXOS v1.94, DE =$0194
        //      HL=language code:
        //          English: L='e',H='n'
        //          Spanish: L='e',H='s'
        //      Further languages may be available in the future
        //      A=0 if running in NextZXOS mode(and zero flag is set)
        //      A<>0 if running in 48K mode(and zero flag is reset)
        //
        //****************************************************************************
        public void GetDosVersion()
        {
            regs.A = 0;
            regs.DE = 0x0202;
            regs.H = 'n';
            regs.L = 'e';
            regs.B = 'N';
            regs.C = 'X';
        }
        //****************************************************************************
        /// <summary>
        ///     Do open/read/write/close ops - pretend to be an MMC card 
        /// </summary>
        //****************************************************************************
        public void DoFileOps()
        {
            regs = CSpect.GetRegs();

            // return from RST $08 call and "skip" the following byte that holds the RST command
            regs.PC = (UInt16) Pop();
            int FileOp = CSpect.Peek(regs.PC);
            regs.PC++;

            // get current MMC path
            MMCPath = (string) CSpect.GetGlobal(eGlobal.MMCPath);
            if (MMCPath.EndsWith("\\") || MMCPath.EndsWith("/"))
            {
                MMCPath = MMCPath.Substring(0, MMCPath.Length - 1);
            }
            MMCPath = Path.GetFullPath(MMCPath);

            // Now do the actual commands
            switch ((RST08)FileOp)
            {
                case RST08.DISK_FILEMAP:    setC( StreamFileMap() ); break;         // streaming get map start
                case RST08.DISK_STRMSTART:  setC(StartStream()); break;             // streaming start
                case RST08.DISK_STRMEND:    setC(EndStream()); break;               // streaming end

                case RST08.M_DOSVERSION:    GetDosVersion(); setC(false); break;    // set default drive (random number really)
                case RST08.M_GETSETDRV:     DoGetSetDrive(); setC(false); break;    // set default drive (random number really)
                case RST08.F_OPEN:          setC(OpenRST8File()); break;
                case RST08.F_READ:          setC(ReadRST8File()); break;
                case RST08.F_WRITE:         setC(WriteRST8File()); break;
                case RST08.F_SEEK:          setC(SeekRST8File()); break;
                case RST08.F_FGETPOS:       setC(GetPosRST8File()); break;
                case RST08.F_CLOSE:         setC(CloseRST8File()); break;
                case RST08.F_FSTAT:         setC(DoGetFileInfoHandle()); break;
                case RST08.F_STAT:          setC(DoGetFileInfoString()); break;
                case RST08.F_RENAME:        setC(RST08_Rename()); break;
                case RST08.M_GETDATE:       setC(GetDateTime()); break;

                case RST08.F_OPENDIR:       setC(OpenDirectory()); break;               // 0xA3
                case RST08.F_READ_DIR:      setC(ReadDirectoryEntry()); break;          // 0xA4
                case RST08.F_TELLDIR:       setC(GetDirectoryPosition()); break;        // 0xA5
                case RST08.F_SEEKDIR:       setC(SetDirectoryPosition()); break;        // 0xA6
                case RST08.F_REWINDDIR:     setC(ResetDirectoryPosition()); break;      // 0xA7
                case RST08.F_GETCWD:        setC(GetCurrentDirectory()); break;         // 0xA8
                case RST08.F_CHDIR:         setC(SetCurrentDirectory()); break;         // 0xA9
                case RST08.F_MKDIR:         setC(MakeDirectory()); break;               // 0xAA
                case RST08.F_RMDIR:         break;

                case RST08.F_FLUSH:         Flush(); break;

            }
            // send the registers back...
            CSpect.SetRegs(regs);
        }

        //****************************************************************************
        /// <summary>
        ///     Flush all files, close them and write wad to disk if there are changes
        /// </summary>
        //****************************************************************************
        [Function("flush", "none", "Flush any cached files to disk")]
        public bool Flush()
        {
            FileSystem.FlushToDisk();
            return false;
        }

        //****************************************************************************
        /// <summary>
        ///     Setup the WAD system instead of single write file to disk mode
        /// </summary>
        /// <param name="_filesystem">The file object</param>
        /// <returns>
        ///     true for okay
        ///     false for error
        /// </returns>
        //****************************************************************************
        [Function("attach_filesystem", "none", "Attach a new filesystem - replacing the current/default HD one")]
        public bool AttachFilesystem(IFileIO _filesystem)
        {
            CloseAllHandles();
            Flush();
            FileSystem = _filesystem;
            return true;
        }


        //****************************************************************************
        /// <summary>
        ///     Allow us to add in an already open file to our internal systems
        /// </summary>
        /// <param name="_handle">The FileStream handle</param>
        /// <param name="_filename">The name of the file we're adding</param>
        /// <returns>
        ///     A new "handle" that Z80 can then reference.
        /// </returns>
        //****************************************************************************
        [Function("set_file_handle", "FileStream,string", "Add an already opened file handle to our internal systems")]
        public int SetFileHandle(FileStream _handle, string _filename)
        {
            int handle = FileSystem.SetFileHandle(_handle, _filename);
            return handle;
        }

    }
}
