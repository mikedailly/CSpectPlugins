using Plugin;
using System;
using System.IO;

namespace esxDOS
{
    public class FileHandle : IDisposable
    {
        /// <summary>File handle - or null if in memory buffer is used</summary>
        public Stream File;
        /// <summary>Generic user data</summary>
        public object UserData;
        /// <summary>Disposable pattern</summary>
        private bool disposedValue;

        /// <summary>Name of the file</summary>
        public string Name { get; set; }
        
        /// <summary>R/W head into file</summary>
        public long Position {
            get {
                if (File == null) return -1;
                return File.Position;
            }
            set
            {
                if (File == null) return;
                File.Position = value;
            }
        }
        
        /// <summary>Length in bytes of the file - NOT the length of the buffer, which may be larger</summary>
        public long Length { get { return File.Length;  } }



        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Create a file interface
        /// </summary>
        // ******************************************************************************************************************************************************
        public FileHandle()
        {
            Name=string.Empty; 
            Position=-1;
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Close the file
        /// </summary>
        // ******************************************************************************************************************************************************
        public void Close()
        {
            File.Flush();
            File.Close();
        }


        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Read bytes into a buffer
        /// </summary>
        /// <param name="buffer">buffer to read into</param>
        /// <param name="offset">offset into the buffer</param>
        /// <param name="size">size in bytes to read</param>
        /// <returns>
        ///     Number of bytes read
        /// </returns>
        // ******************************************************************************************************************************************************
        public int Read(byte[] buffer, int offset, int size)
        {
            int bytesread = File.Read(buffer, offset, size);
            return bytesread;
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Read S32 from an input stream
        /// </summary>
        /// <returns>
        ///     The S32 value
        /// </returns>
        // ******************************************************************************************************************************************************
        public int ReadS32()
        {
            byte[] buff = new byte[4];
            Read(buff, 0, 4);
            int v = buff[0] + (buff[1] << 8) + (buff[2] << 16) + (buff[3] << 24);
            return v;
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Read U32 from an input stream
        /// </summary>
        /// <returns>
        ///     The S32 value
        /// </returns>
        // ******************************************************************************************************************************************************
        public UInt32 ReadU32()
        {
            byte[] buff = new byte[4];
            Read(buff, 0, 4);
            UInt32 v = ((UInt32)buff[0]) + (((UInt32)buff[1]) << 8) + (((UInt32)buff[2]) << 16) + (((UInt32)buff[3]) << 24);
            return v;
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Read U8 from an input stream
        /// </summary>
        /// <returns>
        ///     The S8 value
        /// </returns>
        // ******************************************************************************************************************************************************
        public byte ReadU8()
        {
            byte[] buff = new byte[1];
            Read(buff, 0, 1);
            return buff[0];
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Read a string,0 from an input stream
        /// </summary>
        /// <returns>
        ///     string...
        /// </returns>
        // ******************************************************************************************************************************************************
        public string ReadString()
        {
            byte[] buff = new byte[1];
            string str = string.Empty;
            while (Position<Length)
            {
                Read(buff, 0, 1);
                byte b = (byte)buff[0];
                if (b == 0) break;
                str += (char)b;
            }
            return str;
        }
        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Write a number of bytes to disk
        /// </summary>
        /// <param name="buffer">buffer we're writing from</param>
        /// <param name="offset">offset into buffer</param>
        /// <param name="size">number of bytes to write</param>
        /// <returns>
        ///     total bytes written
        /// </returns>
        // ******************************************************************************************************************************************************
        public int Write(byte[] buffer, int offset, int size)
        {
            if (!File.CanWrite) return -1;
            File.Write(buffer, offset, size);
            return size;
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Write a INT to the buffer
        /// </summary>
        /// <param name="value">INT value</param>
        // ******************************************************************************************************************************************************
        public void Write(int value)
        {
            byte[] buff = new byte[4];
            buff[0] = (byte)(value & 0xff);
            buff[1] = (byte)((value>>8) & 0xff);
            buff[2] = (byte)((value>>16) & 0xff);
            buff[3] = (byte)((value>>24) & 0xff);
            Write(buff, 0, 4);
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Write an UINT to the buffer
        /// </summary>
        /// <param name="value">UINT value</param>
        // ******************************************************************************************************************************************************
        public void Write(UInt32 value)
        {
            byte[] buff = new byte[4];
            buff[0] = (byte)(value & 0xff);
            buff[1] = (byte)((value >> 8) & 0xff);
            buff[2] = (byte)((value >> 16) & 0xff);
            buff[3] = (byte)((value >> 24) & 0xff);
            Write(buff, 0, 4);
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Write a INT to the buffer
        /// </summary>
        /// <param name="value">INT 16 value</param>
        // ******************************************************************************************************************************************************
        public void Write(Int16 value)
        {
            byte[] buff = new byte[2];
            buff[0] = (byte)(value & 0xff);
            buff[1] = (byte)((value >> 8) & 0xff);
            Write(buff, 0, 2);
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Write a INT to the buffer
        /// </summary>
        /// <param name="value">INT 16 value</param>
        // ******************************************************************************************************************************************************
        public void Write(UInt16 value)
        {
            byte[] buff = new byte[2];
            buff[0] = (byte)(value & 0xff);
            buff[1] = (byte)((value >> 8) & 0xff);
            Write(buff, 0, 2);
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Write an INT to the buffer
        /// </summary>
        /// <param name="value">INT value</param>
        // ******************************************************************************************************************************************************
        public void Write(byte value)
        {
            byte[] buff = new byte[1];
            buff[0] = (byte)(value & 0xff);
            Write(buff, 0, 1);
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Write an INT to the buffer
        /// </summary>
        /// <param name="value">INT value</param>
        // ******************************************************************************************************************************************************
        public void Write(string text)
        {
            byte[] buff = new byte[text.Length+1];
            for(int i=0;i< text.Length;i++)
            {
                buff[i] = (byte)((int)text[i] & 0xff);
            }
            buff[text.Length] = 0;
            Write(buff, 0, text.Length + 1);
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Seek into a file from a given offset
        /// </summary>
        /// <param name="offset">offset to seek (can be realative)</param>
        /// <param name="origin">file origin</param>
        /// <returns>
        ///     New file position
        /// </returns>
        // ******************************************************************************************************************************************************
        public long Seek(long offset, SeekOrigin origin)
        {
            return File.Seek(offset, origin);
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Dispose and free up WAD file
        /// </summary>
        /// <param name="disposing"></param>
        // ******************************************************************************************************************************************************
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    if (File != null)
                    {
                        File.Dispose();
                        File = null;
                    }
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // ******************************************************************************************************************************************************
        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~FileHandle()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }
        // ******************************************************************************************************************************************************
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
