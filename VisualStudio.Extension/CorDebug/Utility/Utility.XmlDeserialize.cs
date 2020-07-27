//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System.IO;
using System.Xml.Serialization;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public partial class Utility
    {
        public static object XmlDeserialize(string filename, XmlSerializer xmls)
        {
            object o = null;

            using (FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                o = xmls.Deserialize(stream);
            }

            return o;
        }
    }
}
