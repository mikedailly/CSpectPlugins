using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace esxDOS
{
    // ################################################################################
    /// <summary>
    ///     ALL SPI commands possible - 0x40 is "sync" bit in stream
    /// </summary>
    // ################################################################################
    public enum eSPI_CMD
    {
        NONE = -1,
        /// <summary>GO_IDLE_STATE</summary>
        CMD0 = 0x40,
        /// <summary>SEND_OP_COND</summary>
        CMD1 = 0x40 + 1,
        /// <summary>SEND_IF_COND</summary>
        CMD8 = 0x40 + 8,
        /// <summary>SEND_CSD</summary>
        CMD9 = 0x40 + 9,
        /// <summary>SEND_CID</summary>
        CMD10 = 0x40 + 10,
        /// <summary>STOP_TRANSMISSION </summary>
        CMD12 = 0x40 + 12,
        /// <summary>SEND_STATUS</summary>
        CMD13 = 0x40 + 13,
        /// <summary>SET_BLOCKLEN </summary>
        CMD16 = 0x40 + 16,
        /// <summary>READ_SINGLE_BLOCK</summary>
        CMD17 = 0x40 + 17,
        /// <summary>READ_MULTIPLE_BLOCK </summary>
        CMD18 = 0x40 + 18,
        /// <summary>WRITE_BLOCK </summary>
        CMD24 = 0x40 + 24,
        /// <summary>WRITE_MULTIPLE_BLOCK</summary>
        CMD25 = 0x40 + 25,
        /// <summary>SEND_OP_COND (ACMD) </summary>
        CMD41 = 0x40 + 41,
        /// <summary>APP_CMD </summary>
        CMD55 = 0x40 + 55,
        /// <summary>READ_OCR </summary>
        CMD58 = 0x40 + 58,
        /// <summary>CRC_ON_OFF</summary>
        CMD59 = 0x40 + 59,
        /// <summary>same as CMD41?</summary>
        ACMD41 = 0x40 + 41
    }


    public class CMD_Packet
    {
        public eSPI_CMD cmd;
        public byte arg0;
        public byte arg1;
        public byte arg2;
        public byte arg3;
        public byte crc;

        public UInt32 arg32
        {
            get
            {
                return (UInt32)arg0 << 24 | (UInt32)arg1 << 16 | (UInt32)arg2 << 8 | (UInt32)arg3;
            }
        }

        public CMD_Packet()
        {
            cmd = 0;
            arg0 = 0;
            arg1 = 0;
            arg2 = 0;
            arg3 = 0;
            crc = 0;
        }
    }

}
