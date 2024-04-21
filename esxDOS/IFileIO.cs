using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace esxDOS
{
    public interface IFileIO
    {
        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Get the filename of the open file
        /// </summary>
        /// <param name="handle">handle to file</param>
        /// <returns>
        ///     name or null if not open
        /// </returns>
        // ******************************************************************************************************************************************************
        string GetName(int handle);

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Allocate and set file stream and return it's file handle
        /// </summary>
        /// <param name="filestream">FileStream to set</param>
        /// <param name="name">name of file</param>
        /// <returns>
        ///     File handle
        /// </returns>
        // ******************************************************************************************************************************************************
        int SetFileHandle(FileStream filestream, string name);

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Check to see if a file is open
        /// </summary>
        /// <param name="handle">handle to check</param>
        /// <returns>
        ///     true for yes
        ///     false for no
        /// </returns>
        // ******************************************************************************************************************************************************
        bool IsOpen(int handle);

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Get the length of an open file
        /// </summary>
        /// <param name="handle">file handle</param>
        /// <returns>
        ///     -1 for not open/error
        ///     size = size in bytes
        /// </returns>
        // ******************************************************************************************************************************************************
        long GetLength(int handle);

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Open a file
        /// </summary>
        /// <param name="path">full path</param>
        /// <param name="mode">file open mode</param>
        /// <param name="access">File Read/Write access</param>
        /// <param name="share">share mode</param>
        /// <returns>
        ///     -1 for can't open
        ///     0-MAX_HANDLES = file handle
        /// </returns>
        // ******************************************************************************************************************************************************
        int Open(string path, FileMode mode, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.ReadWrite);

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Close the file
        /// </summary>
        /// <param name="handle">0 to MAX_HANDLES</param>
        /// <returns>
        ///     true for closed
        ///     false for error
        /// </returns>
        // ******************************************************************************************************************************************************
        bool Close(int handle);

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Close ALL open files
        /// </summary>
        // ******************************************************************************************************************************************************
        void CloseAll();

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Read some bytes from an open file
        /// </summary>
        /// <param name="handle"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="size"></param>
        /// <returns>
        ///     Total bytes read
        /// </returns>
        /// <exception cref="NotImplementedException"></exception>
        // ******************************************************************************************************************************************************
        int Read(int handle, byte[] buffer, int offset, int size);

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Write a number of bytes to disk
        /// </summary>
        /// <param name="handle">handle we're writing to</param>
        /// <param name="buffer">buffer we're writing from</param>
        /// <param name="offset">offset into buffer</param>
        /// <param name="size">number of bytes to write</param>
        /// <returns>
        ///     total bytes written
        /// </returns>
        // ******************************************************************************************************************************************************
        int Write(int handle, byte[] buffer, int offset, int size);

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Seek into a file from a given offset
        /// </summary>
        /// <param name="handle">file handle</param>
        /// <param name="offset">offset to seek (can be realative)</param>
        /// <param name="origin">file origin</param>
        /// <returns>
        ///     New file position
        /// </returns>
        // ******************************************************************************************************************************************************
        long Seek(int handle, long offset, SeekOrigin origin);

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Get the position into the file - in bytes
        /// </summary>
        /// <param name="handle">handle to get position of</param>
        /// <returns>
        ///     index into the file in bytes - or -1 if not open/error
        /// </returns>
        // ******************************************************************************************************************************************************
        long GetPosition(int handle);

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Does the file exist?
        /// </summary>
        /// <param name="filename">Filename to search for</param>
        /// <returns>
        ///     true = yes
        ///     false = no
        /// </returns>
        // ******************************************************************************************************************************************************
        bool Exists(string filename);

    }
}
