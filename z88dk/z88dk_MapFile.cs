using Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Threading.Tasks;

namespace Pasta80Symbols
{
    public class z88dk_MapFile
    {
        iCSpect CSpect;
        /// <summary>Z88DK section lookup</summary>
        public static Dictionary<string, bool> KnownSections;
        public static Dictionary<string, SectionDef> SectionLookup;

        public z88dk_MapFile(iCSpect _CSpect)
        {
            CSpect=_CSpect;
        }


        public class SectionDef
        {
            public string label;
            public int page;
            public int address;
            public int offset;
        }



        // ************************************************************************************************************************************************************
        /// <summary>
        ///     Scan Z88DK map file and define all PAGE segments - including those without "PAGE" in their name
        /// </summary>
        /// <param name="pBuffer">Map file in lines</param>
        // ************************************************************************************************************************************************************
        public void DefineAllSegments(string[] pBuffer, out Dictionary<string, SectionDef> SectionLookup)
        {
            SectionLookup = new Dictionary<string, SectionDef>();               // found sections
            KnownSections = new Dictionary<string, bool>();                     // labels we've found in the "section" area

            int index = 0;
            while (index < pBuffer.Length)
            {
                //AttrScreen                      = $5800 ; const, local, , Kernal_asm, PAGE_02_KERNEL_CODE, includes.inc:9
                string line = pBuffer[index++];
                string[] splitup = line.Split(new char[] { '=', ';', ',', ':' });

                string Label = splitup[0].Trim();
                string Address = splitup[1].Trim();
                string type = splitup[2].Trim();
                string local_pub = splitup[3].Trim();       // "local", "public"
                string deftype = splitup[4].Trim();         // "" or "def"
                string filename = splitup[5].Trim();        // "file.c"/"file.asm" etc.
                string section = splitup[6].Trim();         // "PAGE_02_???????"  or "?????"
                string fullpath = splitup[7].Trim();        // "path/path/path/file.c"
                string linenumber = string.Empty;
                if (!string.IsNullOrEmpty(fullpath))
                {
                    linenumber = splitup[8].Trim();         // "" or the line number
                }

                if (Address[0] == '$') Address = Address.Substring(1);
                long Address64k = CSpect.HexToInt64(Address);
                long AddressPhysical = Address64k;
                //if (Label.StartsWith("_")) Label = Label.Substring(1);

                if (!string.IsNullOrEmpty(section))
                {
                    // new SECTION - add it
                    if (!KnownSections.TryGetValue(section, out bool found))
                    {
                        KnownSections.Add(section, true);
                    }
                }
                else
                {
                    // found a section PAGE definition
                    if (Label.StartsWith("__PAGE_") && Label.EndsWith("_head"))
                    {
                        string[] page = Label.Split('_');
                        string page_num = page[3];
                        int PageNumber = -1;
                        if (!Int32.TryParse(page_num, out PageNumber))
                        {
                            // can't define
                            continue;
                        }
                        SectionDef d = new SectionDef();
                        d.label = Label;
                        d.page = PageNumber;
                        d.address = (int)Address64k;
                        d.offset = ((int)Address64k) & 0x1fff;
                        SectionDef dd;
                        if (!SectionLookup.TryGetValue(Label, out dd))
                        {
                            // if already in - for some reason, don't add
                            SectionLookup.Add(Label, d);
                        }
                    }
                    else if (Label.StartsWith("__BANK_") && Label.EndsWith("_head"))
                    {
                        // BANK (16k pages)
                        string[] page = Label.Split('_');
                        string page_num = page[3];
                        int PageNumber = -1;
                        if (!Int32.TryParse(page_num, out PageNumber))
                        {
                            // can't define
                            continue;
                        }
                        SectionDef d = new SectionDef();
                        d.label = Label;
                        d.page = PageNumber * 2;
                        d.address = (int)Address64k;
                        d.offset = ((int)Address64k) & 0x1fff;
                        SectionDef dd;
                        if (!SectionLookup.TryGetValue(Label, out dd))
                        {
                            SectionLookup.Add(Label, d);
                        }
                    }
                    else
                    {
                        // a normal/found section without a PAGE
                        if (Label.StartsWith("__") && Label.EndsWith("_head"))
                        {
                            int[] pages = new int[] { -1, -1, 10, 11, 4, 5, 0, 1 };
                            SectionDef d = new SectionDef();
                            d.label = Label;
                            d.page = -1; // pages[Address64k>>13];
                            d.address = (int)Address64k;
                            d.offset = ((int)Address64k) & 0x1fff;
                            SectionDef dd;
                            if (!SectionLookup.TryGetValue(Label, out dd))
                            {
                                SectionLookup.Add(Label, d);
                            }
                        }
                    }
                }
            }
        }

        // ****************************************************************************************************************************
        /// <summary>
        ///     Load a Z88DK map file
        /// </summary>
        /// <param name="pBuffer"></param>
        /// <returns>
        /// </returns>
        // ****************************************************************************************************************************
        public bool LoadMapFile(string[] pBuffer)
        {
            Dictionary<string, SectionDef> seclookup;
            DefineAllSegments(pBuffer, out seclookup);
            SectionLookup = seclookup;

            int index = 0;
            while (index < pBuffer.Length)
            {
                //AttrScreen                      = $5800 ; const, local, , Kernal_asm, PAGE_02_KERNEL_CODE, includes.inc:9
                string line = pBuffer[index++];
                string[] splitup = line.Split(new char[] { '=', ';', ',', ':' });

                string Label = splitup[0].Trim();
                string Address = splitup[1].Trim();
                string type = splitup[2].Trim();            // "const", "addr"
                string local_pub = splitup[3].Trim();       // "local", "public"
                string deftype = splitup[4].Trim();         // "" or "def"
                string ASMName = splitup[5].Trim();        // "file.c"/"file.asm" etc.
                string section = splitup[6].Trim();         // "PAGE_02_???????"  or "?????"
                string fullpath = splitup[7].Trim();        // "path/path/path/file.c"
                string linenumber = string.Empty;
                if (!string.IsNullOrEmpty(fullpath))
                {
                    linenumber = splitup[8].Trim();      // "" or the line number
                }

                if (Address[0] == '$') Address = Address.Substring(1);
                long Address64k = CSpect.HexToInt64(Address);
                long AddressPhysical = Address64k;
                if (section.ToUpper().StartsWith("PAGE_"))
                {
                    string[] split = section.Split('_');
                    string pagenumber = split[1];
                    int bank = 0;
                    if (!Int32.TryParse(pagenumber, out bank))
                    {
                        AddressPhysical = Address64k;
                    }
                    else
                    {
                        int bank_offset = (int)Address64k & 0x1fff;
                        AddressPhysical = bank_offset + (bank * 8192);
                    }
                }
                else
                {
                    // got a section that DOESN'T have a PAGE_ in it. So look it up
                    string lookup = "__" + section + "_head";
                    SectionDef res;
                    if (seclookup.TryGetValue(lookup, out res))
                    {
                        if (res.page == -1)
                        {
                            // found one, so try and find a match for the ADDRESS that has a PAGE in it
                            foreach (KeyValuePair<string, SectionDef> pair in seclookup)
                            {
                                // find a label that CONTAINS this one ("__PAGE_??_SECTIONNAME_head" etc)
                                SectionDef d = pair.Value;
                                if (d.label.Contains(section) && d.page != -1)
                                {
                                    // update the original - make loading a little quicker
                                    res.page = d.page;
                                    // found a page with the same start address
                                    int bank_offset = (int)Address64k & 0x1fff;
                                    AddressPhysical = bank_offset + (d.page * 8192);
                                }
                            }
                        }
                        else
                        {
                            int bank_offset = (int)Address64k & 0x1fff;
                            AddressPhysical = bank_offset + (res.page * 8192);
                        }
                    }
                }

                if (Label.StartsWith("_")) Label = Label.Substring(1);

                eLabelType labType = eLabelType.Unknown;
                if (type == "const") labType = eLabelType.Equate;
                if (type == "addr") labType = eLabelType.Address;

                Symbol pSym = CSpect.AddSymbol(Label, (int)Address64k, (int)AddressPhysical, labType);
                if (pSym != null && !string.IsNullOrEmpty(fullpath))
                {
                    int FinalLineNumber = -1;
                    if (!string.IsNullOrEmpty(linenumber))
                    {
                        if (!Int32.TryParse(linenumber, out FinalLineNumber)) FinalLineNumber = -1;
                    }
                    pSym.pFileName = fullpath;
                    if (FinalLineNumber != -1) pSym.LineNumber = FinalLineNumber;
                }
            }
            return true;
        }
    }
}

