using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;



namespace DeZogPlugin
{
    /// <summary>
    /// This static class implements a simple logging functionality.
    /// </summary>
    /// <remarks>
    /// 
    /// Use e.g.
    /// ~~~
    /// if(Log.Enabled)
    ///     Log.Write("bytesRead={0}, MsgLength={1}", bytesRead, state.MsgLength);
    /// ~~~
    /// 
    /// Usage:
    /// - if(Log.Enabled)  Log.Write(....
    ///   For conditional logs. Logs will not appear if logging is not enabled.
    /// - Log.Write(....
    ///   For Logs that will always appear. E.g. for errors or exceptions.
    /// - Log.ConsoleWrite(...
    ///   For 'normal' user information. These logs will always be printed also on the console.
    ///   This is for future, if I decide to log also into a file. Then the normal Log.Write
    ///   would not be visible to the user.
    /// 
    /// </remarks>
    public class Log
    {
        /// <summary>
        ///  Use to enable logging.
        /// </summary>
        static public bool Enabled = false;

        /// <summary>
        /// Use this to print in front of each log.
        /// Is done automatically normally.
        /// </summary>
        static public string Prefix = "Dezog Plugin: ";

        /// <summary>
        /// To decide when to print the Prefix.
        /// </summary>
        static private int PrefixColumn = 0;

        /// <summary>
        ///     Writes a formatted string.
        ///     E.g. use Log.Write("bytesRead={0}, MsgLength={1}", bytesRead, state.MsgLength);
        /// </summary>
        /// <param name="format"></param>
        /// <param name="args"></param>
        static public void Write(string format, params object[] args)
        {
            if(PrefixColumn==0)
                Console.Write(Prefix);
            string text = string.Format(format, args);
            Console.Write(text);
            PrefixColumn += text.Length;
        }

        /// <summary>
        ///     Writes a formatted string and adds a newline.
        ///     E.g. use Log.Write("bytesRead={0}, MsgLength={1}", bytesRead, state.MsgLength);
        /// </summary>
        /// <param name="format"></param>
        /// <param name="args"></param>
        static public void WriteLine(string format, params object[] args)
        {
            if (PrefixColumn == 0)
                Console.Write(Prefix);
            string text = string.Format(format, args);
            Console.WriteLine(text);
            PrefixColumn = 0;
        }

        /// <summary>
        ///     Writes an empty line.
        /// </summary>
        static public void WriteLine()
        {
            Console.WriteLine();
            PrefixColumn = 0;
        }

    }

}
