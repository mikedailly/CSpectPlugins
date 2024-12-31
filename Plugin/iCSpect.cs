using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin
{

    // ********************************************************************************************************************************
    /// <summary>
    ///     Debugger command
    /// </summary>
    // ********************************************************************************************************************************
    public enum eDebugCommand
    {
        none = 0,
        /// <summary>Set remote mode - enables/disables debugger screen (0 for enable, 1 for disable)</summary>
        SetRemote,
        /// <summary>Get the debug state (in debugger or not - returns 0 or 1)</summary>
        GetState,
        /// <summary>Enter debugger</summary>
        Enter,
        /// <summary>Exit debugger and run (only in debug mode)</summary>
        Run,
        /// <summary>Single step (only in debug mode)</summary>
        Step,
        /// <summary>Step over (only in debug mode)</summary>
        StepOver,
        /// <summary>Rewind the CPU one instruction</summary>
        UnStep,
        /// <summary>Set a breakpoint [0 to 65535]</summary>
        SetBreakpoint,
        /// <summary>Clear a breakpoint [0 to 65535]</summary>
        ClearBreakpoint,
        /// <summary>Clears ALL logical, physical and read/write breakpoints</summary>
        ClearAllBreakpoints,
        /// <summary>Get a breakpoint [0 to 65535], returns 0 or 1 for being set</summary>
        GetBreakpoint,
        /// <summary>Set a breakpoint in physical memory[0 to 0x1FFFFF]</summary>
        SetPhysicalBreakpoint,
        /// <summary>Clear a breakpoint in physical memory[0 to 0x1FFFFF]</summary>
        ClearPhysicalBreakpoint,
        /// <summary>Get a breakpoint in physical memory[0 to 0x1FFFFF], returns 0 or 1 for being set</summary>
        GetPhysicalBreakpoint,
        /// <summary>Set a breakpoint on CPU READING of a memory location[0 to 65535]</summary>
        SetReadBreakpoint,
        /// <summary>Clear a READ breakpoint [0 to 65535]</summary>
        ClearReadBreakpoint,
        /// <summary>Get a READ breakpoint [0 to 65535], returns 0 or 1 for being set</summary>
        GetReadBreakpoint,
        /// <summary>Set a breakpoint on CPU WRITING of a memory location[0 to 65535]</summary>
        SetWriteBreakpoint,
        /// <summary>Clear a WRITE breakpoint [0 to 65535]</summary>
        ClearWriteBreakpoint,
        /// <summary>Get a WRITE breakpoint [0 to 65535], returns 0 or 1 for being set</summary>
        GetWriteBreakpoint,
    };

    // ********************************************************************************************************************************
    /// <summary>
    ///     Global variable access
    /// </summary>
    // ********************************************************************************************************************************
    public enum eGlobal
    {
        /// <summary>Is SD card 0 active?  returns bool (can't set)</summary>
        SDCardActive0,
        /// <summary>Is SD card 1 active?  returns bool (can't set)/summary>
        SDCardActive1,
        /// <summary>Get the command line MMC path.  returns string</summary>
        MMCPath,
        /// <summary>Get the command line COM STRING #1.  returns string</summary>
        ComString1,
        /// <summary>Get the command line COM STRING #2.  returns string</summary>
        ComString2,
        /// <summary>ZX Next mode - returns bool</summary>
        ZXNextMode,
        /// <summary>ZX Next ROM mode - returns bool</summary>
        NextRom,
        /// <summary>ZX 128 mode - returns bool</summary>
        ZX128,
        /// <summary>esxDOS mode active - returns bool</summary>
        esxDOS,
        /// <summary>verify Eight Dot Three filenames- returns bool</summary>
        verify_EightDotThree,
        /// <summary>Exit opcode enabled - returns bool</summary>
        ExitOpcode,
        /// <summary>BRK opcode enabled - returns bool</summary>
        BRKOpcode,
        /// <summary>Do Quit opcode enabled - returns bool</summary>
        DoQuit,
        /// <summary>Allow escape key enabled - returns bool</summary>
        AllowEscape,
        /// <summary>Joysticks enabled? - returns bool</summary>
        JoyEnable,
        /// <summary>60Hz mode? - returns bool</summary>
        _60Hz,
        /// <summary>VSync enabled? - returns bool</summary>
        vsync,
        /// <summary>Start in fullscreen mode? - returns bool (can't set)</summary>
        FullScreen,
        /// <summary>HDMI mode timing? - returns bool</summary>
        hdmi_mode,
        /// <summary>Is the .NEX divmmc address mapping over ride on?</summary>
        div_mmc_mapping,
        /// <summary>Is the "display" copper wait on?</summary>
        copper_wait,
        /// <summary>Is the "display" copper wait on?</summary>
        irq_wait,
        /// <summary>Get the primary window handle (can't set)</summary>
        window_handle,
        /// <summary>Special file handle being passed on on .NEX loading read/write</summary>
        file_handle,
        /// <summary>Command line filename</summary>
        file_name,
        /// <summary>the next file handle (0 to 255)</summary>
        next_file_handle,
        /// <summary>What LOW ROM is paged in ($0000-$1fff)</summary>
        low_rom,
        /// <summary>What HIGH ROM is paged in ($2000-$3fff)</summary>
        high_rom,

        /// <summary>2mb array holding profile information for reading at each memory location</summary>
        profile_read,
        /// <summary>2mb array holding profile information for writing at each memory location</summary>
        profile_write,
        /// <summary>2mb array holding profile information for executing at each memory location</summary>
        profile_exe
    }

    // ********************************************************************************************************************************
    /// <summary>
    ///     What rom is currently paged in (what does the CPU "see")
    /// </summary>
    // ********************************************************************************************************************************
    public enum eRom
    {
        /// <summary>48k rom paged in</summary>
        zx48k,
        /// <summary>128k rom paged in</summary>
        zx128k,
        /// <summary>NEXT rom 0 paged in</summary>
        zxnext0,
        /// <summary>NEXT rom 1 paged in</summary>
        zxnext1,
        /// <summary>NEXT rom 2 paged in</summary>
        zxnext2,
        /// <summary>NEXT rom 3 paged in</summary>
        zxnext3,
        /// <summary>multiface paged in($0000-$3FFF)</summary>
        multiface,
        /// <summary>DivMMC paged in($0000-$3FFF)</summary>
        divmmc,
        /// <summary>ALT ROM paged in, low 16k of 32k ROM</summary>
        altrom_low,
        /// <summary>ALT ROM paged in, high 16k of 32k ROM</summary>
        altrom_high,


        /// <summary>RAM is paged in</summary>
        ram,
        /// <summary>multiface RAM in ($2000-$3FFF)</summary>
        multiface_ram,
        /// <summary>DivMMC RAM in ($2000-$3FFF)</summary>
        divmmc_ram,
        /// <summary>layer2 READ mode is paged in</summary>
        layer2,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram0,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram1,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram2,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram3,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram4,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram5,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram6,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram7,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram8,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram9,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram10,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram11,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram12,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram13,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram14,
        /// <summary>DivMMC RAM.</summary>
        mmc_ram15,
    }

    // ********************************************************************************************************************************
    /// <summary>
    ///     When you disassemble a line of assembly
    /// </summary>
    // ********************************************************************************************************************************
    public class DissassemblyLine
    {
        /// <summary>The actual disassembly line</summary>
        public string line;
        /// <summary>The number of bytes this instruction takes up</summary>
        public int bytes;
        /// <summary>Number of primary TStates</summary>
        public int TStates1;
        /// <summary>Secondary TStates - uses when brances are taken</summary>
        public int TStates2;
    }

    // ********************************************************************************************************************************
    /// <summary>
    ///     Interface back intgo #CSpect
    /// </summary>
    // ********************************************************************************************************************************
    public unsafe interface iCSpect
    {
        // ------------------------------------------------------------
        /// <summary>
        ///     Poke a byte into z80's 64k address space.
        ///     Follows all currently banked RAM and ROM rules
        /// </summary>
        /// <param name="_address">16bit address</param>
        /// <param name="_values">Array of values to poke</param>
        // ------------------------------------------------------------
        void Poke(ushort _address, byte[] _value);

        // ------------------------------------------------------------
        /// <summary>
        ///     Poke a byte into z80's 64k address space.
        ///     Follows all currently banked RAM and ROM rules
        /// </summary>
        /// <param name="_address">16bit address</param>
        /// <param name="_values">single value to poke</param>
        // ------------------------------------------------------------
        void Poke(ushort _address, byte _value);

        // ------------------------------------------------------------
        /// <summary>
        ///     Poke a byte into the nexts 2Mb address range
        /// </summary>
        /// <param name="_address">Physical address to poke into</param>
        /// <param name="_values">Array of values to poke</param>
        // ------------------------------------------------------------
        void PokePhysical(int _address, byte[] _value);

        // ------------------------------------------------------------
        /// <summary>
        ///     Poke a byte into the nexts 2Mb address range
        /// </summary>
        /// <param name="_address">Physical address to poke into</param>
        /// <param name="_value">Single value to poke</param>
        // ------------------------------------------------------------
        void PokePhysical(int _address, byte _value);

        // ------------------------------------------------------------
        /// <summary>
        ///     Poke a byte into the 16K of sprite image memory
        /// </summary>
        /// <param name="_address">Sprite data address</param>
        /// <param name="_values">Array of values to poke</param>
        /// <returns>
        ///     Byte at the requested location
        /// </returns>        
        // ------------------------------------------------------------
        void PokeSprite(int _address, byte[] _values);

        // ------------------------------------------------------------
        /// <summary>
        ///     Peek "count" worth of bytes from z80's 64k address space.
        ///     Follows all currently banked RAM and ROM rules
        /// </summary>
        /// <param name="_address">Address to peek</param>
        /// <param name="_count">number of bytes to "peek"</param>
        /// <returns>
        ///     Array holding the requested location
        /// </returns>
        // ------------------------------------------------------------
        byte[] Peek(ushort _address, int _count);

        // ------------------------------------------------------------
        /// <summary>
        ///     Peek a single byte from z80's 64k address space.
        ///     Follows all currently banked RAM and ROM rules
        /// </summary>
        /// <param name="_address">Address to peek</param>
        /// <returns>
        ///     byte of peeked memory from the requested location
        /// </returns>
        // ------------------------------------------------------------
        byte Peek(ushort _address);

        // ------------------------------------------------------------
        /// <summary>
        ///     Peek a byte from Nexts 2Mb address space
        /// </summary>
        /// <param name="_address">Address to peek</param>
        /// <param name="_count">number of bytes to "peek"</param>
        /// <returns>
        ///     Array holding the requested location
        /// </returns>
        // ------------------------------------------------------------
        byte[] PeekPhysical(int _address, int _count);

        // ------------------------------------------------------------
        /// <summary>
        ///     Peek a single byte from Nexts 2Mb address space
        /// </summary>
        /// <param name="_address">Address to peek</param>
        /// <returns>
        ///     byte of peeked memory from the requested location
        /// </returns>
        // ------------------------------------------------------------
        byte PeekPhysical(int _address);

        // ------------------------------------------------------------
        /// <summary>
        ///     Peek a byte from the 16K of sprite image memory
        /// </summary>
        /// <param name="_address">Sprite data address (0-$3fff)</param>
        /// <param name="_count">number of bytes to "peek"</param>
        /// <param name="_buffer">optional buffer to fill (from 0)</param>
        /// <returns>
        ///     Array holding the requested location
        /// </returns>
        // ------------------------------------------------------------
        byte[] PeekSprite(int _address, int _count, byte[] _buffer = null);


        // ------------------------------------------------------------
        /// <summary>
        ///     Set a Next Register
        /// </summary>
        /// <param name="_reg">Register to set</param>
        /// <param name="_value">value to set</param>
        // ------------------------------------------------------------
        void SetNextRegister(byte _reg, byte _value);

        // ------------------------------------------------------------
        /// <summary>
        ///     Read a next register
        /// </summary>
        /// <param name="_reg">register to read</param>
        /// <returns>
        ///     register value
        /// </returns>
        // ------------------------------------------------------------
        byte GetNextRegister(byte _reg);

        // ------------------------------------------------------------
        /// <summary>
        ///     Send a value to Z80 port
        /// </summary>
        /// <param name="_port">port to write to</param>
        /// <param name="_value">value to write</param>
        // ------------------------------------------------------------
        void OutPort(ushort _port, byte _value);

        // ------------------------------------------------------------
        /// <summary>
        ///     Read from a Z80 port
        /// </summary>
        /// <param name="_port">port to read from</param>
        /// <returns>
        ///     Read value
        /// </returns>
        // ------------------------------------------------------------
        byte InPort(ushort _port);


        // ------------------------------------------------------------
        /// <summary>
        ///     Get all Z80 registers
        /// </summary>
        /// <returns>
        ///     a class holding all the register info
        /// </returns>
        // ------------------------------------------------------------
        Z80Regs GetRegs();

        // ------------------------------------------------------------
        /// <summary>
        ///     Set all Z80 registers
        /// </summary>
        /// <param name="_regs">Register class holding all registers</param>
        // ------------------------------------------------------------
        void SetRegs(Z80Regs _regs);




        // ------------------------------------------------------------
        /// <summary>
        ///     Execute a debugger command
        /// </summary>
        /// <param name="_cmd">The command to execute</param>
        /// <returns>
        ///     return value on GET operations, otherwise 0
        /// </returns>        
        // ------------------------------------------------------------
        int Debugger(eDebugCommand _cmd, int _value = 0);

        // ------------------------------------------------------------
        /// <summary>
        ///     Disassemble a memory address and return it
        /// </summary>
        /// <param name="_address">Address to disassemble</param>
        /// <param name="_is24bit">Is this a 24bit address?</param>
        /// <returns>
        ///     Disassembly info
        /// </returns>
        // ------------------------------------------------------------
        DissassemblyLine DissasembleMemory(int _address, bool _is24bit);

        // ------------------------------------------------------------
        /// <summary>
        ///     Lookup a symbol
        /// </summary>
        /// <param name="_address">The 24bit address to lookup</param>
        /// <returns>
        ///     The string found - or ""
        /// </returns>
        // ------------------------------------------------------------
        string LookUpSymbol(int _address);

        // ------------------------------------------------------------
        /// <summary>
        ///     Get a sprite
        /// </summary>
        /// <param name="_index">The sprite index</param>
        /// <returns>
        ///     A sprite structure holding the sprite attributes
        /// </returns>        
        // ------------------------------------------------------------
        SSprite GetSprite(int _index);

        // ------------------------------------------------------------
        /// <summary>
        ///     Get a sprite
        /// </summary>
        /// <param name="_index">The sprite index</param>
        /// <param name="_sprite">A sprite structure holding the sprite attributes</param>
        // ------------------------------------------------------------
        void SetSprite(int _index, SSprite _sprite);


        // ------------------------------------------------------------
        /// <summary>
        ///     Read copper memory (0-2047)
        /// </summary>
        /// <param name="_address">Copper address to "peek"</param>
        /// <returns>Byte of copper memory</returns>
        // ------------------------------------------------------------
        byte CopperRead(int _address);
        // ------------------------------------------------------------
        /// <summary>
        ///     Read copper memory
        /// </summary>
        /// <param name="_address">Copper address (0 to 2047) to "poke"</param>
        /// <param name="_value">byte value to poke into copper</param>
        // ------------------------------------------------------------
        void CopperWrite(int _address, byte _value);
        // ------------------------------------------------------------
        /// <summary>
        ///     Has this copper address "ever" been written to?
        /// </summary>
        /// <param name="_address">Copper address (0 to 2047) to "poke"</param>
        /// <returns>TRUE for yes, FALSE for program has never uploaded to here</returns>
        // ------------------------------------------------------------
        bool CopperIsWritten(int _address);

        // ------------------------------------------------------------
        /// <summary>
        ///     Get a global item/flag.
        /// </summary>
        /// <param name="_item">The item</param>
        // ------------------------------------------------------------
        object GetGlobal(eGlobal _item);

        // ------------------------------------------------------------
        /// <summary>
        ///     Set a global item/flag.
        /// </summary>
        /// <param name="_item">The item</param>
        /// <param name="_value">The value to set - it MUST be the correct type</param>
        // ------------------------------------------------------------
        bool SetGlobal(eGlobal _item, object _value);


        // ------------------------------------------------------------
        /// <summary>
        ///     Get a NEXT colour from the palette
        ///         000 = ULA first palette
        ///         100 = ULA second palette
        ///         001 = Layer 2 first palette
        ///         101 = Layer 2 second palette
        ///         010 = Sprites first palette 
        ///         110 = Sprites second palette
        ///         011 = Tilemap first palette
        ///         111 = Tilemap second palette
        /// </summary>
        /// <param name="_palette">The palette index (0 to 7)</param>
        /// <param name="_index">The colour index (0-255)</param>
        // ------------------------------------------------------------
        uint GetColour(int _palette, int _index);

        // ------------------------------------------------------------
        /// <summary>
        ///     Set a NEXT colour from the palette
        ///         000 = ULA first palette
        ///         100 = ULA second palette
        ///         001 = Layer 2 first palette
        ///         101 = Layer 2 second palette
        ///         010 = Sprites first palette 
        ///         110 = Sprites second palette
        ///         011 = Tilemap first palette
        ///         111 = Tilemap second palette
        /// </summary>
        /// <param name="_palette">The palette index (0 to 7)</param>
        /// <param name="_index">The colour index (0-255)</param>
        /// <param name="_value">The colour value (0-511)</param>
        // ------------------------------------------------------------
        void SetColour(int _palette, int _index, int _value);

        // ------------------------------------------------------------
        /// <summary>
        ///     Gets the colours we actually DRAW with (what next colours MAP to)
        /// </summary>
        // ------------------------------------------------------------
        uint* Get32BITColours();

        // ------------------------------------------------------------
        /// <summary>
        ///     Load a file from an SD card, or relative MMC path 
        ///     if SD card is not active
        /// </summary>
        /// <param name="_name">The full path from root (no drive letter etc)</param>
        /// <returns>A byte array holding the file - or null for not found</returns>
        // ------------------------------------------------------------
        byte[] LoadFile(string _name);

        // ------------------------------------------------------------
        /// <summary>
        ///     Execute another plugin command
        /// </summary>
        /// <param name="_command">the command string to execute</param>
        /// <param name="args">Any arguments needing to be passed</param>
        /// <returns>any return or null</returns>
        // ------------------------------------------------------------
        object Execute(string _command, params object[] args);
    }
}
