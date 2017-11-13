using System.IO;
using System.Xml.Serialization;

namespace nanoFramework.Tools.VisualStudio.Debugger
{
    public partial class Utility
    {
        public static object XmlDeserialize(string filename, XmlSerializer xmls)
        {
            object o = null;

            /*
                          
             Using the XmlSerializers have a significant cost to building a temporary assembly to do the serialization.
             SGen can build a new assembly at build time, but we aren't changing our xml structures very often.  To 
             avoid the overhead of another assembly in the build/install, we can just use sgen to generate the deserialization
             code and build that into this assembly.
             
             1.  Run sgen.  Something like the command line below.  Point it at CorDebug, tell it about the type that
                needs to be serialized, and give it an output directory
             
             sgen /assembly:Microsoft.SPOT.Debugger.CorDebug.dll 
              /type:Microsoft.SPOT.Debugger.Pdbx+PdbxFile 
              /keep 
              /force 
              /compiler:/keyfile:%SPOCLIENT%\framework\key.snk 
              /out:d:\temp

             2.  Take the temporary file (given a cryptic filename *.cs in the output directory), and add it
                 to CorDebug.
             3.  Add a #pragma warning disable 0219 around the file, perhaps, to avoid compilation errors about
                 unused variables.                  
             4.  Create the Serializer and call this class.
             */

            using (FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                o = xmls.Deserialize(stream);
            }

            return o;
        }

    }
}
