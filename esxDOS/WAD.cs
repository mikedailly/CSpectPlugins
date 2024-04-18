using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace esxDOS
{
    internal class WAD //: IFileIO
    {
        /// <summary>Name of the WAD file itself</summary>
        public string WADFileName;

        public List<WADFile> Files;

        #region IFileIO

        #endregion


        #region WAD File
        /// <summary>
        ///     
        /// </summary>
        /// <param name="WADFileName"></param>
        public WAD(string WADFileName)
        {
            Files = new List<WADFile>();

            if ( File.Exists(WADFileName) ) 
            {
                //ReadWadFile(WADFileName);
            }
        }
        #endregion

    }
}
