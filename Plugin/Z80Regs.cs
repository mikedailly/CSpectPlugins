using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin
{
    // ***************************************************************************************************
    /// <summary>
    ///     All Z80 registers + 8 bit accessors
    /// </summary>
    // ***************************************************************************************************
    public class Z80Regs
    {
        public UInt16 AF;
        public UInt16 BC;
        public UInt16 DE;
        public UInt16 HL;

        public UInt16 _AF;
        public UInt16 _BC;
        public UInt16 _DE;
        public UInt16 _HL;

        public UInt16 IX;
        public UInt16 IY;
        public UInt16 PC;
        public UInt16 SP;

        public byte R;
        public byte I;
        public bool IFF1;
        public bool IFF2;
        public byte IM;

        #region A Register access
        /// <summary>Get/Set "A" register</summary>
        public int A
        {
            get { return (AF >> 8) & 0xff; }
            set { AF = (UInt16) ((AF&0xff)|(value<<8)); }
        }
        /// <summary>Get/Set "_A" register</summary>
        public int _A
        {
            get { return (_AF >> 8) & 0xff; }
            set { _AF = (UInt16)((_AF & 0xff) | (value << 8)); }
        }
        #endregion


        #region BC Register access
        /// <summary>Get/Set "B" register</summary>
        public int B
        {
            get { return (BC >> 8) & 0xff; }
            set { BC = (UInt16)((BC & 0xff) | (value << 8)); }
        }
        /// <summary>Get/Set "_B" register</summary>
        public int _B
        {
            get { return (_BC >> 8) & 0xff; }
            set { _BC = (UInt16)((_BC & 0xff) | (value << 8)); }
        }
        /// <summary>Get/Set "C" register</summary>
        public int C
        {
            get { return (BC & 0xff); }
            set { BC = (UInt16)((BC & 0xff00) | value); }
        }
        /// <summary>Get/Set "_C" register</summary>
        public int _C
        {
            get { return (_BC & 0xff); }
            set { _BC = (UInt16)((_BC & 0xff00) | value); }
        }
        #endregion


        #region DE Register access
        /// <summary>Get/Set "D" register</summary>
        public int D
        {
            get { return (DE >> 8) & 0xff; }
            set { DE = (UInt16)((DE & 0xff) | (value << 8)); }
        }
        /// <summary>Get/Set "_B" register</summary>
        public int _D
        {
            get { return (_DE >> 8) & 0xff; }
            set { _DE = (UInt16)((_DE & 0xff) | (value << 8)); }
        }
        /// <summary>Get/Set "E" register</summary>
        public int E
        {
            get { return (DE & 0xff); }
            set { DE = (UInt16)((DE & 0xff00) | value); }
        }
        /// <summary>Get/Set "_E" register</summary>
        public int _E
        {
            get { return (_DE & 0xff); }
            set { _DE = (UInt16)((_DE & 0xff00) | value); }
        }
        #endregion


        #region HL Register access
        /// <summary>Get/Set "H" register</summary>
        public int H
        {
            get { return (HL >> 8) & 0xff; }
            set { HL = (UInt16)((HL & 0xff) | (value << 8)); }
        }
        /// <summary>Get/Set "_H" register</summary>
        public int _H
        {
            get { return (_HL >> 8) & 0xff; }
            set { _HL = (UInt16)((_HL & 0xff) | (value << 8)); }
        }
        /// <summary>Get/Set "L" register</summary>
        public int L
        {
            get { return (HL & 0xff); }
            set { HL = (UInt16)((HL & 0xff00) | value); }
        }
        /// <summary>Get/Set "_L" register</summary>
        public int _L
        {
            get { return (_HL & 0xff); }
            set { _HL = (UInt16)((_HL & 0xff00) | value); }
        }
        #endregion
    }
}
