using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NextRegisterViewer
{
    // ********************************************************************************************************
    /// <summary>
    ///     Tool tip item "type"
    /// </summary>
    // ********************************************************************************************************
    public enum eTTItemType
    {
        /// <summary>Colour box type</summary>
        ColourBox,
        /// <summary>Bitmap Image</summary>
        Image,
    }

    // ********************************************************************************************************
    /// <summary>
    ///     Tool Tip "item" to draw 
    /// </summary>
    // ********************************************************************************************************
    public class TTItem
    {
        /// <summary>X offset from the top left of the tool tip</summary>
        public int X;
        /// <summary>Y offset from the top left of the tool tip</summary>
        public int Y;
        /// <summary>Width of the item</summary>
        public int Width;
        /// <summary>Height of the item</summary>
        public int Height;
        /// <summary>Type of tool tip item</summary>
        public eTTItemType TTType;
        /// <summary>Colour Box item</summary>
        public uint Colour;
    }
}
