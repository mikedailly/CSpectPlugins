using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace esxDOS
{
    public interface IFile
    {
        /// <summary>Length of the file in bytes</summary>
        long Length { get; }
        /// <summary>Position in the file</summary>
        long Position { get; }
        /// <summary>Name of the file</summary>
        string Name { get; }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Close the file
        /// </summary>
        // ******************************************************************************************************************************************************
        void Close();

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Read some bytes from an open file
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="size"></param>
        /// <returns>
        ///     Total bytes read
        /// </returns>
        /// <exception cref="NotImplementedException"></exception>
        // ******************************************************************************************************************************************************
        int Read(byte[] buffer, int offset, int size);

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
        int Write(byte[] buffer, int offset, int size);

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
        long Seek(long offset, SeekOrigin origin);
    }
}
