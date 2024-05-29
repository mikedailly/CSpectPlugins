using Plugin;
using System;
using System.IO;

namespace esxDOS
{
    public class HDFileSystem : IFileIO
    {
        /// <summary>Total number of open handles allowed</summary>
        public const int MAX_HANDLES = 256;
        /// <summary>Open handles</summary>
        FileHandle[] HandleLookUps;

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Create a filesystem
        /// </summary>
        // ******************************************************************************************************************************************************
        public HDFileSystem()
        {
            HandleLookUps = new FileHandle[MAX_HANDLES];

            for (int i=0;i<MAX_HANDLES;i++)
            {
                HandleLookUps[i] = null;
            }
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Get the filename of the open file
        /// </summary>
        /// <param name="handle">handle to file</param>
        /// <returns>
        ///     name or null if not open
        /// </returns>
        // ******************************************************************************************************************************************************
        public string GetName(int handle)
        {
            if(handle<0 || handle> MAX_HANDLES) return null;
            FileHandle fhandle = HandleLookUps[handle];
            if (fhandle == null) return null;
            return fhandle.Name;
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Allocate a handle slot. 0 is never used.
        /// </summary>
        /// <returns>
        ///     handle from 1 to MAX_HANDLES
        /// </returns>
        // ******************************************************************************************************************************************************
        public int AllocHandle()
        {
            for(int i=1;i< MAX_HANDLES;i++)
            {
                if (HandleLookUps[i]==null)
                {
                    return i;
                }
            }
            return -1;
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Allocate and set file stream and return it's file handle
        /// </summary>
        /// <param name="handle">FileStream to set</param>
        /// <param name="name">name of file</param>
        /// <returns>
        ///     File handle
        /// </returns>
        // ******************************************************************************************************************************************************
        public int SetFileHandle(FileStream filestream, string name)
        {
            int handle = AllocHandle();
            if (handle <= 0) return -1;

            FileHandle fhandle = new FileHandle();
            fhandle.File= filestream;
            fhandle.Name= name;

            HandleLookUps[handle] = fhandle;
            return handle;
        }

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
        public bool IsOpen(int handle)
        {
            if(handle<=0 || handle>=MAX_HANDLES) return false;
            if (HandleLookUps[handle]==null) return false;
            return true;
        }

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
        public long GetLength(int handle)
        {
            if (handle <= 0 || handle >= MAX_HANDLES) return -1;
            return HandleLookUps[handle].Length;
        }

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
        public int Open(string path, FileMode mode, FileAccess access, FileShare share)
        {
            int handle = AllocHandle();
            if (handle < 0) return -1;

            try
            {
                FileStream file_handle = File.Open(path, mode, access, share);
                FileHandle fhandle = new FileHandle();
                fhandle.File= file_handle;
                fhandle.Name = path;
                HandleLookUps[handle] = fhandle;
                return handle;
            }
            catch //(Exception ex)
            {
                HandleLookUps[handle] = null;
                return -1;
            }
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Close the file
        /// </summary>
        /// <param name="handle">0 to MAX_HANDLES</param>
        // ******************************************************************************************************************************************************
        public bool Close(int handle)
        {
            if (handle <= 0 || handle >= MAX_HANDLES) return false;

            FileHandle file_handle = HandleLookUps[handle];
            if( file_handle == null) return false;

            file_handle.Close();

            HandleLookUps[handle] = null;
            return true;
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Close ALL open files
        /// </summary>
        // ******************************************************************************************************************************************************
        public void CloseAll()
        {
            for(int i=1;i<MAX_HANDLES;i++)
            {
                Close(i);
            }
        }


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
        public int Read(int handle, byte[] buffer, int offset, int size)
        {
            if (handle <= 0 || handle >= MAX_HANDLES) return -1;

            FileHandle file_handle = HandleLookUps[handle];
            if(file_handle==null) return -1;

            int val = file_handle.Read(buffer, offset, size);
            return val;
        }

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
        public int Write(int handle, byte[] buffer, int offset, int size)
        {
            if (handle <= 0 || handle >= MAX_HANDLES) return -1;

            FileHandle file_handle = HandleLookUps[handle];
            if (file_handle == null) return -1;

            file_handle.Write(buffer, offset, size);
            return size;
        }

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
        public long Seek(int handle, long offset, SeekOrigin origin)
        {
            if (handle <= 0 || handle >= MAX_HANDLES) return -1;

            FileHandle file_handle = HandleLookUps[handle];
            if (file_handle == null) return -1;

            long index = file_handle.Seek(offset, origin);
            return index;
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Get the position into the file - in bytes
        /// </summary>
        /// <param name="handle">handle to get position of</param>
        /// <returns>
        ///     index into the file in bytes - or -1 if not open/error
        /// </returns>
        // ******************************************************************************************************************************************************
        public long GetPosition(int handle)
        {
            if (handle <= 0 || handle >= MAX_HANDLES) return -1;

            FileHandle file_handle = HandleLookUps[handle];
            if (file_handle == null) return -1;

            return file_handle.Position;
        }


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
        public bool Exists(string filename)
        {
            return File.Exists(filename);
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Flush all cached contents to disk
        /// </summary>
        // ******************************************************************************************************************************************************
        public void FlushToDisk(){ }

    }
}
