using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin
{
    public unsafe interface iWindow
    {
        /// <summary>Screeen - updated on request</summary>
        UInt32[] Screen { get; }
        /// <summary>Screeen is dirty</summary>
        bool IsDirty { get; set; }

        /// <summary>X Coordinate</summary>
        int X { get; set; }
        /// <summary>Y Coordinate</summary>
        int Y { get; set; }
        /// <summary>Window width</summary>
        int Width { get; }
        /// <summary>Window Height</summary>
        int Height { get; }

        /// <summary>Window moving?</summary>
        bool WindowMoving { get; set; }
        /// <summary>Window Title if any</summary>
        string Title { get; set; }


        /// <summary>Mouse X Coordinate</summary>
        int MouseX { get; set; }
        /// <summary>Mouse Y Coordinate</summary>
        int MouseY { get; set; }
        /// <summary>Mouse Y Coordinate</summary>
        int MouseButtons { get; set; }
        /// <summary>Mouse Y Coordinate</summary>
        int Wheel { get; set; }

        /// <summary>Are we in fullscreen mode?</summary>
        IntPtr WindowHandle { get; }

        bool Focused { get; }

        /// <summary>On close callback</summary>
        event EventHandler OnClosed;
        
        /// <summary>
        ///     Close the window - you must still NULL your reference.
        /// </summary>
        void Close();

        // ******************************************************************************************
        // Simple Rendering Functions - into Screen[] array
        // ******************************************************************************************
        unsafe void DrawImage(UInt32[] sprite, int x, int y, int SrcWidth, int SrcHeight, int destWidth, int destHeight); 


    }
}
