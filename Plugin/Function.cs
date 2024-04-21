using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin
{
    // ********************************************************************************************************************************************
    /// <summary>
    ///     Function attribute tag
    /// </summary>
    // ********************************************************************************************************************************************
    [Serializable()]
    [AttributeUsage(AttributeTargets.Method)]
    public class Function : System.Attribute
    {
        /// <summary>Function command</summary>
        public string Name;
        /// <summary>The command we'll bind to - we get executed when this command is run (load "ide_load")</summary>
        public string Params;
        /// <summary>Simple description that can be displayed to the user/coder if needed</summary>
        public string Description;


        // ********************************************************************************************************************************************
        /// <summary>
        ///     Custom attribute for tagging functions
        /// </summary>
        /// <param name="_name">The name to disaply to users</param>
        /// <param name="_params">par</param>
        /// <param name="_description">description to display to the user</param>
        // ********************************************************************************************************************************************
        public Function(string _name, string _params, string _description)
        {
            Name = _name;
            Params = _params;
            Description = _description;
        }
    }
}
