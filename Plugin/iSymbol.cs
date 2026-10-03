using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin
{
    // ************************************************************************************************
    /// <summary>
    ///     Label type - Equates can be dropped from things like"address" referencing
    /// </summary>
    // ************************************************************************************************
    public enum eLabelType
    {
        /// <summary>Normal address label</summary>
        Address = 0,
        /// <summary>Equate/define/constant</summary>
        Equate,
        /// <summary>unknown</summary>
        Unknown = -1
    }


    public interface iSymbol
    {
        // ****************************************************************************************************************
        /// <summary>
        ///     Initialise the plugin - called before anything else.
        /// </summary>
        /// <param name="_CSpectInterface"></param>
        /// <returns>TRUE for okay, FALSE for error</returns>
        // ****************************************************************************************************************
        bool Init(iCSpect _CSpectInterface);


        // ****************************************************************************************************************
        /// <summary>
        ///     Shutdown
        /// </summary>
        // ****************************************************************************************************************
        void Quit();


        // ****************************************************************************************************************
        /// <summary>
        ///     Get Extension name
        /// </summary>
        /// <returns>Name of symbols being loaded</returns>
        // ****************************************************************************************************************
        string GetName();

        // ****************************************************************************************************************
        /// <summary>
        ///     Get Extension description
        /// </summary>
        /// <returns>Description of symbols being loaded - shown on the command line</returns>
        // ****************************************************************************************************************
        string GetDescription();

        // ****************************************************************************************************************
        /// <summary>
        ///     Get the command line option we're hooking into
        /// </summary>
        /// <returns>the command line i.e. "pasta80" for a "-pasta80=[file]" command line</returns>
        // ****************************************************************************************************************
        string GetCommandLineOption();

        // ****************************************************************************************************************
        /// <summary>
        ///     Load a symbol file
        /// </summary>
        /// <param name="_path">Full path to the symbol file</param>
        /// <returns>
        ///     TRUE for loaded okay, FALSE for error
        /// </returns>
        // ****************************************************************************************************************
        bool LoadSymbols(string _path);
    }
}
