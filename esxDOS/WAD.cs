using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace esxDOS
{
    internal class WAD : IFileIO
    {
        /// <summary>Name of the WAD file itself</summary>
        public string WADFileName;

        /// <summary>List of WAD files</summary>
        public List<WADFile> Files;

        /// <summary>Total number of open handles allowed</summary>
        public const int MAX_HANDLES = 256;
        /// <summary>Open handles</summary>
        FileHandle[] HandleLookUps;


        #region WAD File
        // ************************************************************************************************************
        /// <summary>
        ///     Create a new WAD file
        /// </summary>
        /// <param name="wadFileName">Name of the wad file to load/save to</param>
        // ************************************************************************************************************
        public WAD(string wadFileName)
        {
            WADFileName = wadFileName;
            Files = new List<WADFile>();

            if ( File.Exists(WADFileName) ) 
            {
                ReadWADFile(WADFileName);
            }

            HandleLookUps = new FileHandle[MAX_HANDLES];
            for (int i = 0; i < MAX_HANDLES; i++)
            {
                HandleLookUps[i] = null;
            }

        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Search for a file in the wad package
        /// </summary>
        /// <param name="name">Name of file to find</param>
        /// <returns>
        ///     null or a wadfile
        /// </returns>
        // ******************************************************************************************************************************************************
        public WADFile FindFile(string name)
        {
            foreach(WADFile file in Files)
            {
                if (file.Name == name) return file;
            }
            return null;
        }

        /// <summary>
        ///     Load in the WAD file
        /// </summary>
        /// <param name="name">Name of WAD file</param>
        public void ReadWADFile(string name)
        {
            try
            {
                byte[] buff = File.ReadAllBytes(name);
                FileHandle handle = new FileHandle();
                handle.Data = buff;
                handle.Length= buff.Length;
                handle.Position = 0;

                // clear out old wad
                Files = new List<WADFile>();
                string header = handle.ReadString();
                while (handle.Position<handle.Length)
                {
                    int ChunkSize = handle.ReadS32();
                    string Name = handle.ReadString();
                    int BlockSize = handle.ReadS32();
                    byte[] data= new byte[BlockSize];
                    handle.Read(data,0, BlockSize);

                    WADFile file = new WADFile(Name);
                    file.Data= data;
                    file.Dirty = false;
                    Files.Add(file);
                }
            }
            catch (Exception e)
            {

            }
        }


        // ******************************************************************************************************************************************************
        /// <summary>
        ///     If dirty, then save a new WAD
        /// </summary>
        // ******************************************************************************************************************************************************
        public void SaveWAD()
        {
            bool dirty = false;
            foreach(WADFile file in Files)
            {
                if (file.Dirty)
                {
                    dirty = true;
                    break;
                }
            }
            if (!dirty) return;


            FileHandle handle = new FileHandle();
            handle.Data = new byte[16384];
            handle.Position = 0;
            handle.Length = 0;
            handle.Write("WAD");                                                // WAD,0 header
            foreach (WADFile file in Files)
            {
                handle.Write(file.Name.Length+1 + 4 + file.Data.Length);        // length of block
                handle.Write(file.Name);                                        // Name,0
                handle.Write(file.Data.Length);                                 // Data length
                handle.Write(file.Data,0, file.Data.Length);                    // Data[]
            }
            handle.Close();
            File.WriteAllBytes(WADFileName, handle.Data);
            handle.Dispose();
        }

        #endregion 



        #region IFileIO
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
            if (handle < 0 || handle > MAX_HANDLES) return null;
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
            for (int i = 1; i < MAX_HANDLES; i++)
            {
                if (HandleLookUps[i] == null)
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
            fhandle.File = filestream;
            fhandle.Name = name;

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
            if (handle <= 0 || handle >= MAX_HANDLES) return false;
            if (HandleLookUps[handle] == null) return false;
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
        ///     Open a file from the WAD. If that doesn't exist, then open from disk.
        ///     All "new" files, are saved the to WAD space.
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
                WADFile file = FindFile(path);

                FileHandle fhandle = null;
                if (file != null)
                {
                    // File exists inside the WAD so open that.
                    switch(mode)
                    {
                        case FileMode.OpenOrCreate:             // file found - so open
                        case FileMode.Open:
                            fhandle= new FileHandle();
                            fhandle.Data = file.Data;
                            fhandle.Name = file.Name;
                            fhandle.Length = file.Data.Length;
                            fhandle.Position = 0;
                            fhandle.UserData = file;
                            break;
                        case FileMode.Truncate:                 // truncate the file and write
                            fhandle = new FileHandle();
                            fhandle.Data = new byte[16384];
                            fhandle.Name = file.Name;
                            fhandle.Length = 0;
                            fhandle.Position = 0;
                            fhandle.UserData = file;
                            file.Dirty = true;
                            break;
                        case FileMode.CreateNew:                // only create if file does not exist
                        default:
                            return -1;
                    }
                }
                else
                {
                    // File exists inside the WAD so open that.
                    switch (mode)
                    {
                        case FileMode.OpenOrCreate:
                            // File NOT found, so open...
                            if (File.Exists(path))
                            {
                                // we don't open an existing file to write
                                FileStream file_handle = File.Open(path, mode, access, share);
                                fhandle = new FileHandle();
                                fhandle.File = file_handle;
                                fhandle.Name = path;
                                fhandle.UserData = null;
                            }
                            else
                            {
                                file = new WADFile(path, 0);
                                fhandle = new FileHandle(); 
                                fhandle.Data = new byte[16384];
                                fhandle.Name = file.Name;
                                fhandle.Length = 0;
                                fhandle.Position = 0;
                                fhandle.UserData = file;
                                file.Dirty = true;
                            }
                            break;
                        case FileMode.Open:
                            if (File.Exists(path))
                            {
                                // we don't open an existing file to write
                                FileStream file_handle = File.Open(path, mode, access, share);
                                fhandle = new FileHandle();
                                fhandle.File = file_handle;
                                fhandle.Name = path;
                                fhandle.UserData = null;
                            }
                            else
                            {
                                return -1;
                            }
                            break;
                        case FileMode.Truncate:
                        case FileMode.CreateNew:                // only create if file does not exist
                            file = new WADFile(path, 0);
                            fhandle = new FileHandle();         // always create a new file
                            fhandle.Data = new byte[16384];
                            fhandle.Name = file.Name;
                            fhandle.Length = 0;
                            fhandle.Position = 0;
                            fhandle.UserData = file;
                            file.Dirty = true;
                            break;
                    }
                }
                HandleLookUps[handle] = fhandle;
                return handle;
            }
            catch (Exception ex)
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
            if (file_handle == null) return false;

            file_handle.Close();

            if (file_handle.Data != null)
            {
                WADFile whandle = (WADFile)file_handle.UserData;
                whandle.Data = file_handle.Data;

                WADFile found_file = FindFile(whandle.Name);
                if (found_file == null)
                {
                    Files.Add(whandle);
                }
            }
            HandleLookUps[handle] = null;
            file_handle.Dispose();

            SaveWAD();
            return true;
        }

        // ******************************************************************************************************************************************************
        /// <summary>
        ///     Close ALL open files
        /// </summary>
        // ******************************************************************************************************************************************************
        public void CloseAll()
        {
            for (int i = 1; i < MAX_HANDLES; i++)
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
            if (file_handle == null) return -1;

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
            WADFile file = FindFile(filename);
            if( file != null) return true;

            return File.Exists(filename);
        }

        /// <summary>
        ///     Flush all dirty files to disk, 
        /// </summary>
        public void FlushToDisk()
        {
            SaveWAD();
        }



        #endregion

    }
}
